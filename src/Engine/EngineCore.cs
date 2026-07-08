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
    private long _discovered, _acted, _drafted, _blocked, _rejected, _errors, _cycles;
    public long Discovered => Interlocked.Read(ref _discovered);
    public long Acted => Interlocked.Read(ref _acted);
    public long Drafted => Interlocked.Read(ref _drafted);
    public long Blocked => Interlocked.Read(ref _blocked);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Errors => Interlocked.Read(ref _errors);
    public long Cycles => Interlocked.Read(ref _cycles);
    public DateTimeOffset? LastCycleUtc { get; private set; }

    internal void AddDiscovered(long n) => Interlocked.Add(ref _discovered, n);
    internal void IncActed() => Interlocked.Increment(ref _acted);
    internal void IncDrafted() => Interlocked.Increment(ref _drafted);
    internal void IncBlocked() => Interlocked.Increment(ref _blocked);
    internal void IncRejected() => Interlocked.Increment(ref _rejected);
    internal void IncErrors() => Interlocked.Increment(ref _errors);
    internal void IncCycles() { Interlocked.Increment(ref _cycles); LastCycleUtc = DateTimeOffset.UtcNow; }
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
    string CompanyName = "Discovered");

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
            catch (Exception)
            {
                _counters.IncErrors(); // one bad posting never takes the cycle down
            }
        }
        _counters.IncCycles();
    }

    private void Tally(AppState state)
    {
        switch (state)
        {
            case AppState.DRAFTED: _counters.IncActed(); _counters.IncDrafted(); break;
            case AppState.AWAITING_RESPONSE:
            case AppState.GATE_PENDING: _counters.IncActed(); break;
            case AppState.BLOCKED_FABRICATION: _counters.IncBlocked(); break;
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
