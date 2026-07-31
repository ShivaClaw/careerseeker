using SeekerSvc.Dispatcher;
using SeekerSvc.Pipeline;
using SeekerSvc.Scorer;
using SeekerSvc.Scout;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

/// <summary>Boards to sweep, and how hard to sweep them.</summary>
public sealed record ScoutFeedOptions(
    IReadOnlyList<CompanyBoard> Boards,
    ScoutOptions Scout,
    string JobDescriptionDirectory,
    TimeSpan DiscoveryTimeout);

/// <summary>
/// The engine's live source of postings: a real Scout sweep of real ATS boards, mapped into the shape the
/// cycle persists and scores. This is what makes an unattended run mean anything — every other
/// <see cref="IJobFeed"/> in this repo returns a fixed batch of invented postings and can only ever
/// demonstrate the pipeline, never do the user's job.
///
/// Description text is UNTRUSTED throughout (Scout's contract). It is written to disk as evidence and
/// carried on the posting for the Scorer to pattern-match as DATA; postings Scout flagged as
/// injection-bearing are marked here and dropped by the cycle before any model sees them.
/// </summary>
public sealed class ScoutJobFeed : IIdentifiedJobFeed
{
    private readonly SeekerSvc.Scout.Scout _scout;
    private readonly ScoutFeedOptions _options;
    private readonly Func<DiscoveredJob, string, string?> _writeJobDescription;

    public ScoutJobFeed(
        SeekerSvc.Scout.Scout scout,
        ScoutFeedOptions options,
        Func<DiscoveredJob, string, string?> writeJobDescription)
    {
        _scout = scout;
        _options = options;
        _writeJobDescription = writeJobDescription;
    }

    /// <summary>
    /// Present so a <see cref="ScoutJobFeed"/> still satisfies <see cref="IJobFeed"/>, but it discards the
    /// identity the cycle needs. The cycle always prefers <see cref="DiscoverIdentifiedAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<JobPosting>> DiscoverAsync(CancellationToken ct = default)
    {
        var identified = await DiscoverIdentifiedAsync(ct).ConfigureAwait(false);
        return identified.Select(i => i.Posting).ToArray();
    }

    public async Task<IReadOnlyList<IdentifiedPosting>> DiscoverIdentifiedAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.DiscoveryTimeout);

        DiscoveryResult result;
        try
        {
            result = await _scout.DiscoverAsync(_options.Boards, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The sweep outran its budget. An empty batch makes this a quiet cycle rather than a failed
            // one; the boards are still there to try again on the next tick.
            return Array.Empty<IdentifiedPosting>();
        }

        var identified = new List<IdentifiedPosting>(result.Jobs.Count);
        foreach (var job in result.Jobs)
        {
            var jdPath = _writeJobDescription(job, _options.JobDescriptionDirectory);
            var (company, upsert) = Ingest.From(job, jdPath);

            var applyUrl = string.IsNullOrWhiteSpace(job.ApplyUrl) ? job.Url : job.ApplyUrl;

            identified.Add(new IdentifiedPosting(
                Posting: JobPosting.FromDiscovered(job),
                Company: company,
                Job: upsert,
                ApplyUrl: applyUrl,
                LikelyInjected: job.DescriptionLikelyInjected));
        }

        return identified;
    }
}

/// <summary>
/// Per-job dispatch facts read from the store, so each posting is dispatched to what that posting
/// actually specified. The demo source answers every job with one hard-coded recipient, which is
/// harmless for a single scripted posting and wrong the moment a real sweep returns many.
/// </summary>
public sealed class StorePostingSource : IPostingSource
{
    private readonly ISeekerStore _store;
    private readonly Func<string?, CancellationToken, Task<string?>> _readJobDescription;

    public StorePostingSource(
        ISeekerStore store,
        Func<string?, CancellationToken, Task<string?>> readJobDescription)
    {
        _store = store;
        _readJobDescription = readJobDescription;
    }

    public async Task<PostingDispatchInfo> GetDispatchInfoAsync(long jobId, CancellationToken ct = default)
    {
        var summary = await _store.GetJobSummaryAsync(jobId, ct).ConfigureAwait(false);
        if (summary is null)
            return new PostingDispatchInfo(DispatchChannel.ManualFinish);

        var stored = await _store.GetJobAsync(jobId, ct).ConfigureAwait(false);
        var applyUrl = string.IsNullOrWhiteSpace(summary.ApplyUrl) ? summary.JobUrl : summary.ApplyUrl;
        var postingText = await _readJobDescription(stored?.JdPath, ct).ConfigureAwait(false);
        var applicationEmail = ChannelDetector.ResolveApplicationEmail(applyUrl, postingText);

        return new PostingDispatchInfo(
            ChannelDetector.Detect(applyUrl, applicationEmail),
            ApplicationEmail: applicationEmail,
            ApplyUrl: applyUrl,
            PostingText: postingText);
    }
}
