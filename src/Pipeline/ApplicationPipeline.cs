using System.Text.Json;
using SeekerSvc.Scorer;
using SeekerSvc.Store;
using SeekerSvc.Verifier;

namespace SeekerSvc.Pipeline;

/// <summary>Tunables for the pipeline.</summary>
public sealed record PipelineOptions
{
    /// <summary>The Source-of-Truth profile id whose claims the Gate verifies against.</summary>
    public long ProfileId { get; init; } = 1;

    /// <summary>How many rework attempts after a fabrication block before escalating to the human.</summary>
    public int ReworkAttempts { get; init; } = 1;

    public DispatchChannel Channel { get; init; } = DispatchChannel.AtsForm;

    /// <summary>Optional Gate tuning; default preserves exhaustive semantic comparison.</summary>
    public GateVerificationOptions Gate { get; init; } = GateVerificationOptions.Default;

    public static PipelineOptions Default { get; } = new();
}

/// <summary>Outcome of admitting a job into the pipeline.</summary>
public sealed record AdmitResult(long ApplicationId, AppState FinalState, VerificationResult? Gate, DispatchOutcome? Dispatch);

/// <summary>Outcome of a crash-recovery reconciliation pass over one application.</summary>
public enum ReconcileOutcome
{
    /// <summary>Nothing to reconcile.</summary>
    NoAction,
    /// <summary>An external effect had SUCCEEDED but the state transition was lost; the transition was completed without re-calling the provider.</summary>
    CompletedFromRecord,
    /// <summary>A PENDING attempt exists: the provider outcome is unknown. Automatic retry is refused; a human (or provider reconciliation) must resolve it.</summary>
    ManualReviewRequired,
    /// <summary>The last attempt FAILED (known no-effect): a retry is safe.</summary>
    RetryAvailable,
}

/// <summary>
/// Drives an application through the lifecycle (spec section 3.3). Every transition is checked against
/// <see cref="Lifecycle"/> first — an illegal edge throws rather than executing — and is persisted via a
/// compare-and-swap on the Store, which appends a hash-chained audit event atomically with the state
/// write. CAS makes every state edge single-winner: two racing actors cannot both take the same edge,
/// which is what makes L2 approval submit-once. External side effects (Gmail draft, ATS submit) are
/// bracketed by durable effect-attempt records written before and after the call, so a crash between
/// the provider succeeding and the local state committing is detectable and recoverable without a
/// second side effect (<see cref="ReconcileAsync"/>). The L2 in-flight payload and the pre-pause state
/// are persisted through the Store — process memory holds no lifecycle state, so restarts lose nothing.
/// The Fabrication Gate runs for real at the VERIFIED step; because READY has no predecessor but
/// VERIFIED, nothing the dispatcher does is reachable unless the gate passed.
/// </summary>
public sealed class ApplicationPipeline
{
    private const string DraftKind = "draft";
    private const string SubmitKind = "submit";

    private readonly ISeekerStore _store;
    private readonly ITailor _tailor;
    private readonly IDispatcher _dispatcher;
    private readonly ISemanticMatcher _matcher;
    private readonly PipelineOptions _opt;

    /// <summary>Durable serialization envelope for the L2 in-flight tailored payload.</summary>
    private sealed record PendingDispatch(PipelineJob Job, TailoredApplication Tailored);

    public ApplicationPipeline(
        ISeekerStore store, ITailor tailor, IDispatcher dispatcher,
        ISemanticMatcher matcher, PipelineOptions? options = null)
    {
        _store = store;
        _tailor = tailor;
        _dispatcher = dispatcher;
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _opt = options ?? PipelineOptions.Default;
    }

    /// <summary>
    /// Admit a scored job: create the application, screen and evaluate it, and — only if the Scorer
    /// said Act — tailor, verify, and route per the autonomy level. Returns where it came to rest.
    /// </summary>
    public async Task<AdmitResult> AdmitAsync(
        PipelineJob job, AutonomyLevel level, Dispatch decision, CancellationToken ct = default)
    {
        var appId = await _store.CreateApplicationAsync(job.JobId, level.ToString(), ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.SCREENED, "engine", ct: ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.EVALUATED, "engine", ct: ct).ConfigureAwait(false);

        if (decision != Dispatch.Act)
        {
            await TransitionAsync(appId, AppState.REJECTED_BY_ENGINE, "engine",
                $"\"dispatch\":\"{decision}\"", ct).ConfigureAwait(false);
            return new AdmitResult(appId, AppState.REJECTED_BY_ENGINE, null, null);
        }

        var (reached, tailored, gate) = await RunToReadyAsync(appId, job, ct).ConfigureAwait(false);
        if (reached != AppState.READY)
            return new AdmitResult(appId, reached, gate, null); // escalated at BLOCKED_FABRICATION / GATE_UNAVAILABLE

        var (finalState, dispatch) = await RouteAsync(appId, job, level, tailored!, ct).ConfigureAwait(false);
        return new AdmitResult(appId, finalState, gate, dispatch);
    }

    /// <summary>TAILORED → (Gate) → VERIFIED → READY, or a fail-closed stop at BLOCKED_FABRICATION / GATE_UNAVAILABLE.</summary>
    private async Task<(AppState State, TailoredApplication? Tailored, VerificationResult? Gate)> RunToReadyAsync(
        long appId, PipelineJob job, CancellationToken ct)
    {
        var claims = ClaimMapping.ToSourceClaims(await _store.GetClaimsAsync(_opt.ProfileId, ct).ConfigureAwait(false));
        IReadOnlyList<Violation> prior = Array.Empty<Violation>();
        VerificationResult? lastResult = null;
        TailoredApplication? tailored = null;

        for (var attempt = 0; attempt <= _opt.ReworkAttempts; attempt++)
        {
            tailored = await _tailor.TailorAsync(job, claims, prior, ct).ConfigureAwait(false);
            await TransitionAsync(appId, AppState.TAILORED, "engine", ct: ct).ConfigureAwait(false);

            lastResult = await FabricationGate.VerifyAsync(claims, tailored.Claims, _matcher, options: _opt.Gate, ct: ct).ConfigureAwait(false);
            if (lastResult.Passed)
            {
                await TransitionAsync(appId, AppState.VERIFIED, "engine", ct: ct).ConfigureAwait(false);
                await TransitionAsync(appId, AppState.READY, "engine", ct: ct).ConfigureAwait(false);
                return (AppState.READY, tailored, lastResult);
            }

            if (lastResult.Deferred)
            {
                await TransitionAsync(appId, AppState.GATE_UNAVAILABLE, "engine",
                    GateUnavailablePayload(lastResult), ct).ConfigureAwait(false);
                return (AppState.GATE_UNAVAILABLE, tailored, lastResult);
            }

            await TransitionAsync(appId, AppState.BLOCKED_FABRICATION, "engine",
                GateBlockedPayload(lastResult), ct).ConfigureAwait(false);
            prior = lastResult.Violations;
        }

        // still blocked after rework: the gate is never bypassed — escalate to the human.
        return (AppState.BLOCKED_FABRICATION, tailored, lastResult);
    }

    /// <summary>Route a READY application per autonomy level (spec section 2.1).</summary>
    private async Task<(AppState State, DispatchOutcome? Dispatch)> RouteAsync(
        long appId, PipelineJob job, AutonomyLevel level, TailoredApplication tailored, CancellationToken ct)
    {
        var target = Lifecycle.RouteFromReady(level);

        switch (level)
        {
            case AutonomyLevel.L1:
            {
                // Bracket the external call. Crash after Gmail succeeds but before DRAFTED commits
                // leaves state=READY + attempt=SUCCEEDED, which ReconcileAsync completes without a
                // second draft. A failed Gmail call resolves the attempt FAILED and leaves READY.
                var attemptId = await _store.BeginEffectAttemptAsync(appId, DraftKind, ct).ConfigureAwait(false);
                DispatchOutcome draft;
                try
                {
                    draft = await _dispatcher.CreateDraftAsync(job, tailored, ct).ConfigureAwait(false);
                }
                catch
                {
                    await _store.ResolveEffectAttemptAsync(attemptId, "FAILED", null, ct).ConfigureAwait(false);
                    throw;
                }
                await _store.ResolveEffectAttemptAsync(attemptId, "SUCCEEDED", draft.Reference, ct).ConfigureAwait(false);
                await _store.SaveApplicationArtifactsAsync(
                    appId,
                    draft.ResumePath,
                    draft.CoverPath,
                    draft.AnswersJson,
                    ct).ConfigureAwait(false);
                await TransitionAsync(appId, target, "engine", ct: ct).ConfigureAwait(false);
                return (AppState.DRAFTED, draft); // the human reviews and sends from Gmail
            }

            case AutonomyLevel.L2:
                // Persist the in-flight payload BEFORE the state advertises approvability: an approval
                // arriving after a restart must find its content in the Store, not in process memory.
                await _store.SavePendingDispatchAsync(appId,
                    JsonSerializer.Serialize(new PendingDispatch(job, tailored)), ct).ConfigureAwait(false);
                await TransitionAsync(appId, target, "engine", ct: ct).ConfigureAwait(false);
                await _store.AppendEventAsync(new EventInput("engine", "gate_request", "gate", appId.ToString(),
                    "{\"kind\":\"apply\"}"), ct).ConfigureAwait(false);
                return (AppState.GATE_PENDING, null); // awaiting one tap from the phone

            case AutonomyLevel.L3:
            default:
            {
                await TransitionAsync(appId, target, "engine", ct: ct).ConfigureAwait(false);
                var outcome = await SubmitOnceAsync(appId, job, tailored, ct).ConfigureAwait(false);
                await TransitionAsync(appId, AppState.APPLIED, "engine", ct: ct).ConfigureAwait(false);
                await TransitionAsync(appId, AppState.AWAITING_RESPONSE, "engine", ct: ct).ConfigureAwait(false);
                return (AppState.AWAITING_RESPONSE, outcome);
            }
        }
    }

    /// <summary>
    /// Resolve an L2 apply-gate. Approve elects a single winner via CAS (GATE_PENDING → APPROVED):
    /// exactly one concurrent caller wins and submits; every other caller returns the observed state
    /// without side effects. Skip is likewise race-tolerant. The submission payload comes from the
    /// Store, so approval works across restarts — and if the payload is missing, the gate refuses
    /// (it never transitions to APPLIED without having actually submitted).
    /// </summary>
    public async Task<AppState> ResolveApplyGateAsync(long appId, bool approve, string? note = null, CancellationToken ct = default)
    {
        await _store.AppendEventAsync(new EventInput("user", "gate_resolve", "gate", appId.ToString(),
            $"{{\"decision\":\"{(approve ? "approve" : "skip")}\"}}"), ct).ConfigureAwait(false);

        if (!approve)
        {
            var skipped = await TryStepAsync(appId, AppState.GATE_PENDING, AppState.SKIPPED, "user",
                note is null ? null : $"\"reason\":\"{note}\"", ct: ct).ConfigureAwait(false);
            if (skipped)
                await _store.DeletePendingDispatchAsync(appId, ct).ConfigureAwait(false);
            return await GetStateAsync(appId, ct).ConfigureAwait(false);
        }

        // A late or duplicate resolution (double-tap, digest arriving after the phone) is a benign
        // no-op: if the gate is no longer pending, report the settled state and do nothing.
        var current = await GetStateAsync(appId, ct).ConfigureAwait(false);
        if (current != AppState.GATE_PENDING)
            return current;

        // Load the durable payload BEFORE electing a winner: if it is unavailable, no election
        // happens and the gate stays pending rather than advancing toward a submit that cannot run.
        var payloadJson = await _store.GetPendingDispatchAsync(appId, ct).ConfigureAwait(false);
        if (payloadJson is null)
        {
            // Close the read race: a concurrent resolution may have consumed the payload between the
            // state read and this load. Only a live gate with no payload is genuine corruption.
            var reread = await GetStateAsync(appId, ct).ConfigureAwait(false);
            if (reread != AppState.GATE_PENDING) return reread;
            throw new InvalidOperationException(
                $"Application {appId}: no durable dispatch payload for this gate. Re-run tailoring; never mark APPLIED without submitting.");
        }
        var pending = JsonSerializer.Deserialize<PendingDispatch>(payloadJson)
            ?? throw new InvalidOperationException($"Application {appId}: pending dispatch payload failed to deserialize.");

        // The election. CAS admits exactly one winner per GATE_PENDING; losers take no action.
        var won = await TryStepAsync(appId, AppState.GATE_PENDING, AppState.APPROVED, "user", ct: ct).ConfigureAwait(false);
        if (!won)
            return await GetStateAsync(appId, ct).ConfigureAwait(false);

        await TransitionAsync(appId, AppState.SUBMITTING, "engine", ct: ct).ConfigureAwait(false);
        var outcome = await SubmitOnceAsync(appId, pending.Job, pending.Tailored, ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.APPLIED, "engine",
            outcome.Reference is null ? null : $"\"ref\":{JsonSerializer.Serialize(outcome.Reference)}", ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.AWAITING_RESPONSE, "engine", ct: ct).ConfigureAwait(false);
        await _store.DeletePendingDispatchAsync(appId, ct).ConfigureAwait(false);
        return AppState.AWAITING_RESPONSE;
    }

    /// <summary>
    /// The one place a submission is performed. Refuses to call the provider while any prior submit
    /// attempt is PENDING (outcome unknown — an automatic retry could double-apply); FAILED attempts
    /// (known no-effect) do not block. Brackets the call with a durable attempt record.
    /// </summary>
    private async Task<DispatchOutcome> SubmitOnceAsync(long appId, PipelineJob job, TailoredApplication tailored, CancellationToken ct)
    {
        var attempts = await _store.GetEffectAttemptsAsync(appId, SubmitKind, ct).ConfigureAwait(false);
        if (attempts.Any(a => a.Status == "PENDING"))
            throw new InvalidOperationException(
                $"Application {appId}: a submission attempt with unknown outcome exists. Reconcile before retrying — an automatic retry could submit twice.");

        var attemptId = await _store.BeginEffectAttemptAsync(appId, SubmitKind, ct).ConfigureAwait(false);
        DispatchOutcome outcome;
        try
        {
            outcome = await _dispatcher.SubmitAsync(job, tailored, ct).ConfigureAwait(false);
        }
        catch
        {
            await _store.ResolveEffectAttemptAsync(attemptId, "FAILED", null, ct).ConfigureAwait(false);
            throw; // state remains SUBMITTING; the attempt is known-failed, so a later retry is safe
        }
        await _store.ResolveEffectAttemptAsync(attemptId, "SUCCEEDED", outcome.Reference, ct).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Crash recovery for one application. Completes transitions whose external effect already
    /// SUCCEEDED (without re-calling the provider), flags unknown-outcome attempts for manual review,
    /// and reports when a known-failed attempt makes a retry safe. Never performs a side effect.
    /// </summary>
    public async Task<ReconcileOutcome> ReconcileAsync(long appId, CancellationToken ct = default)
    {
        var app = await _store.GetApplicationAsync(appId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"No application {appId}");
        var state = Enum.Parse<AppState>(app.State);

        if (state == AppState.SUBMITTING)
        {
            var submits = await _store.GetEffectAttemptsAsync(appId, SubmitKind, ct).ConfigureAwait(false);
            var last = submits.LastOrDefault();
            if (last is { Status: "SUCCEEDED" })
            {
                // The provider acted; only the local commit was lost. Finish it — no second submit.
                await TryStepAsync(appId, AppState.SUBMITTING, AppState.APPLIED, "engine",
                    "\"reconciled\":true", ct: ct).ConfigureAwait(false);
                await TryStepAsync(appId, AppState.APPLIED, AppState.AWAITING_RESPONSE, "engine", ct: ct).ConfigureAwait(false);
                await _store.DeletePendingDispatchAsync(appId, ct).ConfigureAwait(false);
                return ReconcileOutcome.CompletedFromRecord;
            }
            if (last is { Status: "PENDING" }) return ReconcileOutcome.ManualReviewRequired;
            return ReconcileOutcome.RetryAvailable; // FAILED or never attempted: known no-effect
        }

        if (state == AppState.READY)
        {
            var drafts = await _store.GetEffectAttemptsAsync(appId, DraftKind, ct).ConfigureAwait(false);
            if (drafts.LastOrDefault() is { Status: "SUCCEEDED" })
            {
                await TryStepAsync(appId, AppState.READY, AppState.DRAFTED, "engine",
                    "\"reconciled\":true", ct: ct).ConfigureAwait(false);
                return ReconcileOutcome.CompletedFromRecord;
            }
            if (drafts.LastOrDefault() is { Status: "PENDING" }) return ReconcileOutcome.ManualReviewRequired;
            return ReconcileOutcome.NoAction;
        }

        return ReconcileOutcome.NoAction;
    }

    /// <summary>72h elapsed with no tap: expire the gate. Race-tolerant: a no-op if already resolved.</summary>
    public Task ExpireGateAsync(long appId, CancellationToken ct = default)
        => TryStepAsync(appId, AppState.GATE_PENDING, AppState.GATE_EXPIRED, "engine", ct: ct);

    /// <summary>Kill switch: from any state to USER_KILLED. Retries the CAS so the control op wins races.</summary>
    public async Task KillAsync(long appId, CancellationToken ct = default)
    {
        for (var i = 0; i < 5; i++)
        {
            var from = await GetStateAsync(appId, ct).ConfigureAwait(false);
            if (from == AppState.USER_KILLED) return;
            if (!Lifecycle.CanTransition(from, AppState.USER_KILLED)) return;
            if (await TryStepAsync(appId, from, AppState.USER_KILLED, "user", ct: ct).ConfigureAwait(false)) return;
        }
        throw new InvalidOperationException($"Application {appId}: kill lost {5} consecutive races; store contention is pathological.");
    }

    /// <summary>Pause an active application. The pre-pause state is persisted on the row (survives restart).</summary>
    public async Task PauseAsync(long appId, CancellationToken ct = default)
    {
        for (var i = 0; i < 5; i++)
        {
            var from = await GetStateAsync(appId, ct).ConfigureAwait(false);
            if (!Lifecycle.CanTransition(from, AppState.PAUSED))
                throw new InvalidOperationException($"Cannot pause application {appId} from {from}.");
            if (await TryStepAsync(appId, from, AppState.PAUSED, "user",
                    $"\"from\":\"{from}\"", recordPausedFrom: from.ToString(), ct: ct).ConfigureAwait(false))
                return;
        }
        throw new InvalidOperationException($"Application {appId}: pause lost {5} consecutive races.");
    }

    /// <summary>Resume a paused application to its durably remembered prior state (a trusted control op, audited).</summary>
    public async Task<AppState> ResumeAsync(long appId, CancellationToken ct = default)
    {
        var app = await _store.GetApplicationAsync(appId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"No application {appId}");
        if (!Enum.TryParse<AppState>(app.State, out var state) || state != AppState.PAUSED || app.PausedFrom is null)
            throw new InvalidOperationException($"Application {appId} has no remembered pre-pause state.");
        var prior = Enum.Parse<AppState>(app.PausedFrom);
        var ok = await _store.TryTransitionApplicationAsync(appId, AppState.PAUSED.ToString(), prior.ToString(),
            "user", $"{{\"to\":\"{prior}\",\"resume\":true}}", recordPausedFrom: null, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new InvalidOperationException($"Application {appId} left PAUSED concurrently; resume aborted.");
        return prior;
    }

    public async Task<AppState> GetStateAsync(long appId, CancellationToken ct = default)
    {
        var app = await _store.GetApplicationAsync(appId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"No application {appId}");
        return Enum.Parse<AppState>(app.State);
    }

    /// <summary>
    /// The one guarded transition path: validate against the state machine, then compare-and-swap in
    /// the Store (state write + audit event are atomic). A CAS loss means another actor moved the row
    /// first; for the linear engine path that is unexpected, so it throws rather than proceeding on a
    /// stale premise.
    /// </summary>
    private async Task TransitionAsync(long appId, AppState to, string actor, string? extraJson = null, CancellationToken ct = default)
    {
        var from = await GetStateAsync(appId, ct).ConfigureAwait(false);
        if (!Lifecycle.CanTransition(from, to))
            throw new InvalidOperationException($"Illegal transition {from} -> {to} for application {appId}.");
        if (!await TryStepAsync(appId, from, to, actor, extraJson, ct: ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                $"Application {appId}: transition {from} -> {to} lost a concurrent race (row no longer in {from}).");
    }

    /// <summary>Race-tolerant guarded step: validates the edge, then CAS. False = lost the race, nothing happened.</summary>
    private async Task<bool> TryStepAsync(long appId, AppState from, AppState to, string actor,
        string? extraJson = null, string? recordPausedFrom = null, CancellationToken ct = default)
    {
        if (!Lifecycle.CanTransition(from, to))
            throw new InvalidOperationException($"Illegal transition {from} -> {to} for application {appId}.");
        var payload = extraJson is null ? $"{{\"to\":\"{to}\"}}" : $"{{\"to\":\"{to}\",{extraJson}}}";
        return await _store.TryTransitionApplicationAsync(appId, from.ToString(), to.ToString(), actor,
            payload, recordPausedFrom, ct).ConfigureAwait(false);
    }

    private static string GateUnavailablePayload(VerificationResult result) =>
        $"\"unavailableClaims\":{result.UnavailableClaims},\"claimsChecked\":{result.ClaimsChecked}";

    private static string GateBlockedPayload(VerificationResult result) =>
        "\"violations\":" + result.Violations.Count +
        ",\"claimsChecked\":" + result.ClaimsChecked +
        ",\"violationSamples\":" + JsonSerializer.Serialize(result.Violations.Take(3).Select(v => new
        {
            kind = v.Claim.Kind.ToString(),
            claim = v.Claim.Text,
            reason = v.Kind.ToString(),
            nearest = v.NearestSource,
        }).ToArray());
}
