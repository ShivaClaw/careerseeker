using System.Collections.Concurrent;
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

    public static PipelineOptions Default { get; } = new();
}

/// <summary>Outcome of admitting a job into the pipeline.</summary>
public sealed record AdmitResult(long ApplicationId, AppState FinalState, VerificationResult? Gate, DispatchOutcome? Dispatch);

/// <summary>
/// Drives an application through the lifecycle (spec section 3.3). Every transition is checked against
/// <see cref="Lifecycle"/> first — an illegal edge throws rather than executing — and is written through
/// the Store, which appends a hash-chained audit event for each. The Fabrication Gate runs for real at
/// the VERIFIED step; because READY has no predecessor but VERIFIED, nothing the dispatcher does is
/// reachable unless the gate passed. The Scorer's dispatch decision is honored at EVALUATED: anything
/// not <see cref="Dispatch.Act"/> ends at REJECTED_BY_ENGINE, so the legitimacy floor holds end to end.
/// </summary>
public sealed class ApplicationPipeline
{
    private readonly ISeekerStore _store;
    private readonly ITailor _tailor;
    private readonly IDispatcher _dispatcher;
    private readonly ISemanticMatcher _matcher;
    private readonly PipelineOptions _opt;

    // In production the tailored artifacts live on disk (content-addressed, per the Store); here we
    // keep the in-flight tailored application in memory so an L2 gate can submit it on approval.
    private readonly ConcurrentDictionary<long, (PipelineJob Job, TailoredApplication Tailored)> _pending = new();
    private readonly ConcurrentDictionary<long, AppState> _pausedFrom = new();

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
            return new AdmitResult(appId, reached, gate, null); // escalated at BLOCKED_FABRICATION

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

            lastResult = await FabricationGate.VerifyAsync(claims, tailored.Claims, _matcher, ct: ct).ConfigureAwait(false);
            if (lastResult.Passed)
            {
                await TransitionAsync(appId, AppState.VERIFIED, "engine", ct: ct).ConfigureAwait(false);
                await TransitionAsync(appId, AppState.READY, "engine", ct: ct).ConfigureAwait(false);
                return (AppState.READY, tailored, lastResult);
            }

            if (lastResult.Deferred)
            {
                await TransitionAsync(appId, AppState.GATE_UNAVAILABLE, "engine",
                    $"\"unavailableClaims\":{lastResult.UnavailableClaims}", ct).ConfigureAwait(false);
                return (AppState.GATE_UNAVAILABLE, tailored, lastResult);
            }

            await TransitionAsync(appId, AppState.BLOCKED_FABRICATION, "engine",
                $"\"violations\":{lastResult.Violations.Count}", ct).ConfigureAwait(false);
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
                var draft = await _dispatcher.CreateDraftAsync(job, tailored, ct).ConfigureAwait(false);
                // A failed Gmail call must leave the application READY, never falsely DRAFTED.
                await TransitionAsync(appId, target, "engine", ct: ct).ConfigureAwait(false);
                return (AppState.DRAFTED, draft); // the human reviews and sends from Gmail

            case AutonomyLevel.L2:
                await TransitionAsync(appId, target, "engine", ct: ct).ConfigureAwait(false);
                _pending[appId] = (job, tailored);
                await _store.AppendEventAsync(new EventInput("engine", "gate_request", "gate", appId.ToString(),
                    "{\"kind\":\"apply\"}"), ct).ConfigureAwait(false);
                return (AppState.GATE_PENDING, null); // awaiting one tap from the phone

            case AutonomyLevel.L3:
            default:
                await TransitionAsync(appId, target, "engine", ct: ct).ConfigureAwait(false);
                var outcome = await _dispatcher.SubmitAsync(job, tailored, ct).ConfigureAwait(false);
                await TransitionAsync(appId, AppState.APPLIED, "engine", ct: ct).ConfigureAwait(false);
                await TransitionAsync(appId, AppState.AWAITING_RESPONSE, "engine", ct: ct).ConfigureAwait(false);
                return (AppState.AWAITING_RESPONSE, outcome);
        }
    }

    /// <summary>Resolve an L2 apply-gate from the phone or digest. Approve submits; skip records the decline.</summary>
    public async Task<AppState> ResolveApplyGateAsync(long appId, bool approve, string? note = null, CancellationToken ct = default)
    {
        await _store.AppendEventAsync(new EventInput("user", "gate_resolve", "gate", appId.ToString(),
            $"{{\"decision\":\"{(approve ? "approve" : "skip")}\"}}"), ct).ConfigureAwait(false);

        if (!approve)
        {
            await TransitionAsync(appId, AppState.SKIPPED, "user",
                note is null ? null : $"\"reason\":\"{note}\"", ct).ConfigureAwait(false);
            _pending.TryRemove(appId, out _);
            return AppState.SKIPPED;
        }

        await TransitionAsync(appId, AppState.APPROVED, "user", ct: ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.SUBMITTING, "engine", ct: ct).ConfigureAwait(false);
        if (_pending.TryGetValue(appId, out var p))
            await _dispatcher.SubmitAsync(p.Job, p.Tailored, ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.APPLIED, "engine", ct: ct).ConfigureAwait(false);
        await TransitionAsync(appId, AppState.AWAITING_RESPONSE, "engine", ct: ct).ConfigureAwait(false);
        _pending.TryRemove(appId, out _);
        return AppState.AWAITING_RESPONSE;
    }

    /// <summary>72h elapsed with no tap: expire the gate (it falls back to the digest email).</summary>
    public Task ExpireGateAsync(long appId, CancellationToken ct = default)
        => TransitionAsync(appId, AppState.GATE_EXPIRED, "engine", ct: ct);

    /// <summary>Kill switch: from any state to USER_KILLED.</summary>
    public Task KillAsync(long appId, CancellationToken ct = default)
        => TransitionAsync(appId, AppState.USER_KILLED, "user", ct: ct);

    /// <summary>Pause an active application, remembering where to resume.</summary>
    public async Task PauseAsync(long appId, CancellationToken ct = default)
    {
        var from = await GetStateAsync(appId, ct).ConfigureAwait(false);
        _pausedFrom[appId] = from;
        await TransitionAsync(appId, AppState.PAUSED, "user", ct: ct).ConfigureAwait(false);
    }

    /// <summary>Resume a paused application to its prior state (a trusted control op, audited).</summary>
    public async Task<AppState> ResumeAsync(long appId, CancellationToken ct = default)
    {
        if (!_pausedFrom.TryGetValue(appId, out var prior))
            throw new InvalidOperationException($"Application {appId} has no remembered pre-pause state.");
        await _store.TransitionApplicationAsync(appId, prior.ToString(), "user",
            $"{{\"to\":\"{prior}\",\"resume\":true}}", ct).ConfigureAwait(false);
        _pausedFrom.TryRemove(appId, out _);
        return prior;
    }

    public async Task<AppState> GetStateAsync(long appId, CancellationToken ct = default)
    {
        var app = await _store.GetApplicationAsync(appId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"No application {appId}");
        return Enum.Parse<AppState>(app.State);
    }

    /// <summary>The one guarded transition path: validate against the state machine, then persist + audit.</summary>
    /// <param name="extraJson">Optional inner JSON fields (no braces), merged after the always-present "to".</param>
    private async Task TransitionAsync(long appId, AppState to, string actor, string? extraJson = null, CancellationToken ct = default)
    {
        var from = await GetStateAsync(appId, ct).ConfigureAwait(false);
        if (!Lifecycle.CanTransition(from, to))
            throw new InvalidOperationException($"Illegal transition {from} -> {to} for application {appId}.");
        var payload = extraJson is null ? $"{{\"to\":\"{to}\"}}" : $"{{\"to\":\"{to}\",{extraJson}}}";
        await _store.TransitionApplicationAsync(appId, to.ToString(), actor, payload, ct).ConfigureAwait(false);
    }
}
