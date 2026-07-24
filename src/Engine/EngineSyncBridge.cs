using System.Globalization;
using SeekerSvc.Store;
using SeekerSvc.Sync;

namespace SeekerSvc.Engine;

/// <summary>
/// Projects live engine state into the read-only dashboard payloads (Sync-Protocol.md §4.3) and
/// drives a <see cref="SyncPublisher"/>. The first publish is a full <c>snapshot</c>; every
/// subsequent publish is a <c>delta</c> carrying the current counters and the recent
/// application/job summaries. A counters-only <c>heartbeat</c> is available for a liveness timer.
///
/// This is the seam between the engine and the sync library: it reads the same
/// <see cref="EngineCounters"/> and <see cref="LocalDashboardEvidence"/> the local dashboard
/// renders, maps them into the sync record types, and hands them to the publisher. It holds no key
/// material and does no crypto — the publisher seals and the sink transports.
///
/// Untrusted-text rule (CLAUDE.md): the projection carries only short structured fields — state,
/// company, title, score, injection/repost flags — so a raw posting body structurally cannot ride
/// to the phone. The full JD stays in the engine.
///
/// Publishing is opt-in (sync.enabled, default off) and this bridge is only ever constructed when a
/// pairing exists, so with sync disabled the engine behaves exactly as before.
/// </summary>
public sealed class EngineSyncBridge
{
    private readonly EngineCounters _counters;
    private readonly LocalDashboardEvidence _evidence;
    private readonly SyncPublisher _publisher;
    private readonly int _appLimit;
    private readonly int _jobLimit;
    private int _snapshotSent;

    public EngineSyncBridge(
        EngineCounters counters,
        LocalDashboardEvidence evidence,
        SyncPublisher publisher,
        int appLimit = 25,
        int jobLimit = 25)
    {
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _appLimit = appLimit;
        _jobLimit = jobLimit;
    }

    /// <summary>The highest e2p sequence number the underlying publisher has assigned.</summary>
    public long HighestSeq => _publisher.HighestSeq;

    /// <summary>True once the initial snapshot has been published; deltas follow.</summary>
    public bool SnapshotSent => Volatile.Read(ref _snapshotSent) != 0;

    /// <summary>
    /// Publish current dashboard state: a full snapshot the first time, a delta thereafter. Called
    /// after each engine cycle. Returns the sink's success; a false or a throw here never affects the
    /// cycle (the scheduler runs this after the tick and swallows failures), so a flaky relay cannot
    /// stall the engine.
    /// </summary>
    public async Task<bool> PublishAsync(CancellationToken ct = default)
    {
        var evidence = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        var counters = MapCounters(_counters);
        var apps = evidence.RecentApplications.Take(_appLimit).Select(MapApplication).ToArray();
        var jobs = evidence.RecentJobs.Take(_jobLimit).Select(MapJob).ToArray();

        if (Interlocked.CompareExchange(ref _snapshotSent, 1, 0) == 0)
            return await _publisher.PublishSnapshotAsync(counters, apps, jobs, ct).ConfigureAwait(false);

        // since_seq is the last envelope this publisher sent; the phone applies latest-wins over it.
        return await _publisher.PublishDeltaAsync(_publisher.HighestSeq, counters, apps, jobs, ct).ConfigureAwait(false);
    }

    /// <summary>A cheap counters-only liveness beat for the phone's "last seen"; no store read.</summary>
    public Task<bool> PublishHeartbeatAsync(CancellationToken ct = default)
        => _publisher.PublishHeartbeatAsync(_counters.Cycles, MapCounters(_counters), ct);

    /// <summary>
    /// Publish the engine's audit-chain verdict + recent event metadata for the Evidence screen.
    /// Sourced from the same <see cref="DashboardEvidence"/> the local dashboard's evidence page
    /// renders, so the phone sees exactly what the desktop reports — never a raw event payload.
    /// </summary>
    public async Task<bool> PublishEvidenceAsync(CancellationToken ct = default)
    {
        var e = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        var events = e.RecentEvents
            .Select(x => new EvidenceEvent(x.Seq, x.Ts, x.Actor, x.Kind, x.Entity, x.EntityId))
            .ToArray();
        return await _publisher.PublishEvidenceAsync(e.AuditOk, e.FirstBrokenSeq, e.EventCount, events, ct).ConfigureAwait(false);
    }

    public static Counters MapCounters(EngineCounters c) =>
        new(c.Discovered, c.Acted, c.Drafted, c.Blocked, c.Rejected, c.Errors, c.Cycles);

    public static AppSummary MapApplication(ApplicationSummaryRow r) => new(
        r.ApplicationId.ToString(CultureInfo.InvariantCulture),
        r.State,
        r.CompanyName ?? r.CompanyDomain ?? "-",
        r.JobTitle,
        r.Total is { } total ? (int)Math.Round(total, MidpointRounding.AwayFromZero) : 0);

    public static JobSummary MapJob(JobSummaryRow r) => new(
        r.JobId.ToString(CultureInfo.InvariantCulture),
        r.CompanyName ?? r.CompanyDomain ?? "-",
        r.Title,
        Repost: r.RepostCount > 0,
        InjectionFlag: r.Injected);
}
