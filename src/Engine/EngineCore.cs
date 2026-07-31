using System.Collections.Concurrent;
using SeekerSvc.Dispatcher;
using SeekerSvc.Pipeline;
using SeekerSvc.Scorer;
using SeekerSvc.Scout;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

/// <summary>Live, thread-safe tallies the dashboard renders. One run cycle updates these.</summary>
public sealed class EngineCounters
{
    private long _discovered, _acted, _drafted, _blocked, _rejected, _quarantined, _errors, _cycles;
    private long _lastCycleUtcTicks;
    public long Discovered => Interlocked.Read(ref _discovered);
    public long Acted => Interlocked.Read(ref _acted);
    public long Drafted => Interlocked.Read(ref _drafted);
    public long Blocked => Interlocked.Read(ref _blocked);
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>
    /// Postings Scout flagged with prompt-injection signals. They are stored as evidence but never
    /// enter the tailor/gate path, so their text can never reach a model as instructions. Counted apart
    /// from <see cref="Rejected"/> because "we refused to read this" is a different fact from
    /// "we read it and scored it too low".
    /// </summary>
    public long Quarantined => Interlocked.Read(ref _quarantined);
    public long Errors => Interlocked.Read(ref _errors);
    public long Cycles => Interlocked.Read(ref _cycles);
    public DateTimeOffset? LastCycleUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastCycleUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    internal void AddDiscovered(long n) => Interlocked.Add(ref _discovered, n);
    internal void IncActed() => Interlocked.Increment(ref _acted);
    internal void IncDrafted() => Interlocked.Increment(ref _drafted);
    internal void IncBlocked() => Interlocked.Increment(ref _blocked);
    internal void IncRejected() => Interlocked.Increment(ref _rejected);
    internal void IncQuarantined() => Interlocked.Increment(ref _quarantined);
    internal void IncErrors() => Interlocked.Increment(ref _errors);
    internal void IncCycles()
    {
        Interlocked.Increment(ref _cycles);
        Interlocked.Exchange(ref _lastCycleUtcTicks, DateTimeOffset.UtcNow.Ticks);
    }
}

/// <summary>
/// Source of candidate postings for a cycle. The real implementation is the Scout (ATS feeds over the
/// network); the host injects it so cycles are testable offline with a fixed batch.
/// </summary>
public interface IJobFeed
{
    Task<IReadOnlyList<JobPosting>> DiscoverAsync(CancellationToken ct = default);
}

/// <summary>
/// A discovered posting carrying the real identity it must be stored under, plus the dispatch facts the
/// posting itself expressed. Synthetic feeds cannot supply this — they have no company, no external id,
/// and no apply URL — which is exactly why the cycle keeps a separate path for them.
/// </summary>
public sealed record IdentifiedPosting(
    JobPosting Posting,
    CompanyUpsert Company,
    JobUpsert Job,
    string? ApplyUrl = null,
    bool LikelyInjected = false);

/// <summary>
/// A feed backed by real boards. Unlike <see cref="IJobFeed"/>, every item knows which company it came
/// from and what its stable external id is, so the cycle can persist it under its true identity and
/// dedupe it across runs instead of inventing a key per tick.
/// </summary>
public interface IIdentifiedJobFeed : IJobFeed
{
    Task<IReadOnlyList<IdentifiedPosting>> DiscoverIdentifiedAsync(CancellationToken ct = default);
}

/// <summary>
/// Supplies the model-judgment sub-scores the Scorer needs (CV match, growth). In production these come
/// from the LLM Gateway's QuickScore/FullEvaluation stages; injected here so scoring is deterministic
/// offline. The Scorer computes everything else without a model.
/// </summary>
public interface ISemanticScorer
{
    Task<SemanticScores> ScoreAsync(JobPosting posting, CancellationToken ct = default);
}

/// <summary>Knobs for a run cycle.</summary>
public sealed record EngineOptions(
    UserPreferences Preferences,
    AutonomyLevel Level = AutonomyLevel.L1,
    DispatchChannel Channel = DispatchChannel.Email,
    long ProfileId = 1,
    string CompanyHandle = "feed",
    string CompanyName = "Discovered",
    /// <summary>
    /// Ceiling on how many postings one cycle may push through the tailor/gate/draft path. A real board
    /// sweep can return hundreds of matches at once, and an unattended loop that turned all of them into
    /// Gmail drafts in a single tick would be indistinguishable from a runaway. Postings over the cap are
    /// already stored, so the next tick picks them up. 0 means no ceiling (the offline fixtures).
    /// </summary>
    int MaxActionsPerCycle = 0,
    /// <summary>
    /// Whether an <see cref="Dispatch.Act"/> decision may enter Tailor/Gate/Dispatcher. False is the
    /// honest discovery-only path: jobs are stored and scored, but no simulated or real draft is
    /// recorded. This is what <c>run --dry-run</c> uses.
    /// </summary>
    bool DraftsEnabled = true);

/// <summary>
/// One discovery→decision→action cycle, the loop body the engine runs on a schedule. It does for a batch
/// exactly what the vertical slice does for one job: store the posting, score it, and admit it to the
/// Pipeline, which tailors, runs the Fabrication Gate, and (only on a pass) drafts. The scam floor and the
/// Gate are enforced inside those components — the cycle just tallies where each job came to rest.
/// </summary>
public sealed class EngineCycle
{
    private readonly ISeekerStore _store;
    private readonly IJobFeed _feed;
    private readonly ISemanticScorer _semantic;
    private readonly ApplicationPipeline _pipeline;
    private readonly EngineOptions _opt;
    private readonly EngineCounters _counters;

    public EngineCycle(
        ISeekerStore store, IJobFeed feed, ISemanticScorer semantic,
        ApplicationPipeline pipeline, EngineOptions opt, EngineCounters counters)
    {
        _store = store; _feed = feed; _semantic = semantic;
        _pipeline = pipeline; _opt = opt; _counters = counters;
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        try
        {
            if (_feed is IIdentifiedJobFeed identified)
                await TickIdentifiedAsync(identified, ct).ConfigureAwait(false);
            else
                await TickSyntheticAsync(ct).ConfigureAwait(false);

            _counters.IncCycles();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            _counters.IncErrors();
            _counters.IncCycles();
        }
    }

    /// <summary>
    /// The real path: postings that arrived from actual boards, stored under their own company and
    /// external id, dispatched to whatever the posting itself specified.
    /// </summary>
    private async Task TickIdentifiedAsync(IIdentifiedJobFeed feed, CancellationToken ct)
    {
        var batch = await feed.DiscoverIdentifiedAsync(ct).ConfigureAwait(false);
        _counters.AddDiscovered(batch.Count);

        var actionsTaken = 0;
        var cap = _opt.MaxActionsPerCycle;

        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var companyId = await _store.UpsertCompanyAsync(item.Company, ct).ConfigureAwait(false);
                var write = await _store.UpsertJobAsync(companyId, item.Job, ct).ConfigureAwait(false);

                // Scout flagged prompt-injection signals in this description. It is persisted as evidence
                // and then dropped here, before any tailor or gate call: the whole point of the flag is
                // that this text must never be handed to a model. `draft-job` enforces the same refusal
                // interactively; an unattended loop cannot be laxer than the manual path.
                //
                // Checked before the action cap on purpose. Quarantining spends no action budget, and
                // counting it only until the cap fills would make a safety-relevant number quietly depend
                // on how many drafts happened to come first.
                if (item.LikelyInjected)
                {
                    _counters.IncQuarantined();
                    continue;
                }

                // A board sweep is periodic and upsert returns the same job id on every sighting.
                // Once that job has entered the application lifecycle, never admit it again: doing so
                // would create a fresh application and potentially another Gmail draft every interval.
                // Skipping before the cap also lets later, never-processed jobs advance on the next tick.
                if (await _store.HasApplicationForJobAsync(write.JobId, ct).ConfigureAwait(false))
                    continue;

                // Store everything, act on a bounded slice. Persisting past the cap keeps discovery
                // complete (and dedupe correct) while the action budget stays predictable.
                if (cap > 0 && actionsTaken >= cap)
                    continue;

                var sem = await _semantic.ScoreAsync(item.Posting, ct).ConfigureAwait(false);
                var score = SeekerSvc.Scorer.Scorer.Score(item.Posting, _opt.Preferences, sem);

                // Dry-run/discovery-only means exactly that. In particular, do not route an Act
                // decision through a fake Gmail client and then label the result DRAFTED: no Gmail
                // draft exists, and the dashboard must never say otherwise.
                if (score.Dispatch == Dispatch.Act && !_opt.DraftsEnabled)
                    continue;

                var job = new PipelineJob(
                    write.JobId,
                    item.Posting.Title,
                    item.Company.Name ?? item.Company.Handle,
                    item.ApplyUrl,
                    item.Posting.DescriptionText);

                if (score.Dispatch == Dispatch.Act)
                    actionsTaken++;

                var result = await _pipeline.AdmitAsync(job, _opt.Level, score.Dispatch, ct).ConfigureAwait(false);
                Tally(result.FinalState);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                _counters.IncErrors(); // one bad posting never takes the cycle down
            }
        }
    }

    /// <summary>The synthetic path, unchanged: fixed batches with no identity, used by demo and offline tests.</summary>
    private async Task TickSyntheticAsync(CancellationToken ct)
    {
        var batch = await _feed.DiscoverAsync(ct).ConfigureAwait(false);
        _counters.AddDiscovered(batch.Count);

        var companyId = await _store.UpsertCompanyAsync(
            new CompanyUpsert("feed", _opt.CompanyHandle, _opt.CompanyName), ct).ConfigureAwait(false);

        foreach (var posting in batch)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var jobId = await PersistAsync(companyId, posting, ct).ConfigureAwait(false);
                var sem = await _semantic.ScoreAsync(posting, ct).ConfigureAwait(false);
                var score = SeekerSvc.Scorer.Scorer.Score(posting, _opt.Preferences, sem);

                var job = new PipelineJob(jobId, posting.Title, _opt.CompanyName,
                    score.Dispatch == Dispatch.Act ? "mailto:jobs@" + _opt.CompanyHandle + ".com" : null);

                var result = await _pipeline.AdmitAsync(job, _opt.Level, score.Dispatch, ct).ConfigureAwait(false);
                Tally(result.FinalState);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                _counters.IncErrors(); // one bad posting never takes the cycle down
            }
        }
    }

    private void Tally(AppState state)
    {
        switch (state)
        {
            case AppState.DRAFTED: _counters.IncActed(); _counters.IncDrafted(); break;
            case AppState.AWAITING_RESPONSE:
            case AppState.GATE_PENDING: _counters.IncActed(); break;
            case AppState.BLOCKED_FABRICATION: _counters.IncBlocked(); break;
            case AppState.GATE_UNAVAILABLE: _counters.IncErrors(); break;
            case AppState.REJECTED_BY_ENGINE: _counters.IncRejected(); break;
        }
    }

    private async Task<long> PersistAsync(long companyId, JobPosting p, CancellationToken ct)
    {
        var r = await _store.UpsertJobAsync(companyId, new JobUpsert(
            Source: "feed", ExternalId: Guid.NewGuid().ToString("N"), Url: "about:blank",
            Title: p.Title, TitleCanon: p.TitleCanon, DedupKey: _opt.CompanyHandle + "|" + p.TitleCanon + "|" + p.DescriptionText.GetHashCode(),
            Remote: p.Remote.ToString(), SimHash: 0L, FirstSeen: DateTimeOffset.UtcNow.ToString("o"),
            Location: p.Location, CompMin: p.Compensation?.Min, CompMax: p.Compensation?.Max), ct).ConfigureAwait(false);
        return r.JobId;
    }
}
