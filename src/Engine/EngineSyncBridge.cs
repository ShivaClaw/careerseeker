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
    private readonly InboundPump? _inbound;
    private readonly int _maxDrainPages;
    private readonly int _appLimit;
    private readonly int _jobLimit;
    private int _snapshotSent;

    /// <param name="inbound">The phone→engine drain, or null when nothing can receive yet (no pairing,
    /// or no configured Play licence key — see Program.cs's BuildSyncBridge). Null leaves
    /// <see cref="DrainInboundAsync"/> a no-op, exactly as a null publisher sink would.</param>
    /// <param name="maxDrainPages">How many pages one drain will follow before returning, so a relay
    /// that always claims "more available" cannot hold the engine's tick open indefinitely. The
    /// remainder is not lost — the cursor persists, and the next tick continues from it.</param>
    public EngineSyncBridge(
        EngineCounters counters,
        LocalDashboardEvidence evidence,
        SyncPublisher publisher,
        int appLimit = 25,
        int jobLimit = 25,
        InboundPump? inbound = null,
        int maxDrainPages = 8)
    {
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _inbound = inbound;
        _maxDrainPages = maxDrainPages > 0 ? maxDrainPages : 1;
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

        // The flag flips only AFTER a successful snapshot push (audit finding, 2026-07-24): marking
        // it sent up front meant a failed or thrown first push burned the seq and every later
        // publish was a delta -- which a fresh phone merges into demo fixture rows, presenting demo
        // data as real. On failure (or throw) the flag stays unset, so the next cycle retries the
        // snapshot; the burned seq is a legitimate gap.
        if (!SnapshotSent)
        {
            var ok = await _publisher.PublishSnapshotAsync(counters, apps, jobs, ct).ConfigureAwait(false);
            if (ok) Volatile.Write(ref _snapshotSent, 1);
            return ok;
        }

        // since_seq is the last envelope this publisher sent; the phone applies latest-wins over it.
        return await _publisher.PublishDeltaAsync(_publisher.HighestSeq, counters, apps, jobs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Drain what the phone has sent, following the relay's paging up to <c>maxDrainPages</c>.
    ///
    /// Called on the same tick as <see cref="PublishAsync"/> and, like it, its failures are swallowed by
    /// the scheduler — an unreachable relay must never stall an engine cycle. Returns the reports in
    /// order so a caller can log or surface them; an empty list means there was nothing to drain (no
    /// inbound pump configured).
    ///
    /// The loop stops on the first page that reports no more available, on the first transport failure,
    /// and unconditionally at the page bound. It never stops early on rejections: a page of envelopes
    /// this engine cannot read is exactly the case where the cursor must keep moving (§6.2).
    /// </summary>
    public async Task<IReadOnlyList<InboundReport>> DrainInboundAsync(CancellationToken ct = default)
    {
        if (_inbound is null) return Array.Empty<InboundReport>();

        var reports = new List<InboundReport>();
        for (var page = 0; page < _maxDrainPages; page++)
        {
            var report = await _inbound.DrainAsync(ct).ConfigureAwait(false);
            reports.Add(report);
            if (report.PullFailed || !report.MoreAvailable) break;
        }
        return reports;
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
        ScoreToWire(r.Total),
        r.Outcome);

    /// <summary>
    /// The engine scores on a 0–5 axis (Scorer: total = min(fit, legitimacy) · multiplier, all ≤5);
    /// the phone's UI speaks 0–100. The wire unit is 0–100 int (Sync-Protocol.md §4.3), so a real
    /// score renders on the same scale as the demo fixture rather than as a bare "4" beside an "82".
    /// </summary>
    public static int ScoreToWire(double? engineTotal) =>
        engineTotal is { } t ? (int)Math.Round(Math.Clamp(t * 20.0, 0.0, 100.0), MidpointRounding.AwayFromZero) : 0;

    public static JobSummary MapJob(JobSummaryRow r) => new(
        r.JobId.ToString(CultureInfo.InvariantCulture),
        r.CompanyName ?? r.CompanyDomain ?? "-",
        r.Title,
        Repost: r.RepostCount > 0,
        InjectionFlag: r.Injected);
}
