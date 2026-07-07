namespace SeekerSvc.Scout;

/// <summary>
/// Orchestrates discovery across many boards: fetches each board's feed (through the injected
/// <see cref="IBoardFetcher"/>), parses it with the matching provider, then deduplicates the
/// combined set. Boards run under a global concurrency cap; per-host politeness is handled inside
/// the fetcher. Every board is failure-isolated — a fetch error, an unparseable body, or an
/// unexpected exception yields a failed <see cref="BoardResult"/> and never sinks the run.
/// Aggregation is deterministic: jobs come out in board-input order, then dedup runs.
/// </summary>
public sealed class Scout
{
    private readonly IBoardFetcher _fetcher;
    private readonly ScoutOptions _opts;
    private readonly IReadOnlyDictionary<AtsKind, IAtsProvider> _providers;

    public Scout(
        IBoardFetcher fetcher,
        ScoutOptions? options = null,
        IEnumerable<IAtsProvider>? providers = null)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _opts = options ?? ScoutOptions.Default;
        _providers = (providers ?? DefaultProviders()).ToDictionary(p => p.Kind);
    }

    /// <summary>The three built-in ATS adapters.</summary>
    public static IReadOnlyList<IAtsProvider> DefaultProviders() => new IAtsProvider[]
    {
        new GreenhouseProvider(),
        new LeverProvider(),
        new AshbyProvider(),
    };

    public async Task<DiscoveryResult> DiscoverAsync(
        IReadOnlyList<CompanyBoard> boards, CancellationToken ct = default)
    {
        var results = new BoardResult[boards.Count];
        var jobLists = new List<DiscoveredJob>[boards.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, _opts.MaxConcurrency));

        async Task RunOne(int index, CompanyBoard board)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var (result, jobs) = await IngestBoardAsync(board, ct).ConfigureAwait(false);
                results[index] = result;
                jobLists[index] = jobs;
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(boards.Select((b, i) => RunOne(i, b))).ConfigureAwait(false);

        // Aggregate in stable input order, then dedup the combined set.
        var all = new List<DiscoveredJob>();
        var boardResults = new List<BoardResult>(boards.Count);
        for (var i = 0; i < boards.Count; i++)
        {
            boardResults.Add(results[i]);
            all.AddRange(jobLists[i]);
        }

        var (kept, collapsed) = Deduplicator.Collapse(all, _opts);
        return new DiscoveryResult(kept, boardResults, collapsed);
    }

    private async Task<(BoardResult Result, List<DiscoveredJob> Jobs)> IngestBoardAsync(
        CompanyBoard board, CancellationToken ct)
    {
        if (!_providers.TryGetValue(board.Ats, out var provider))
            return (new BoardResult(board, false, 0, $"No provider registered for {board.Ats}"), new());

        try
        {
            var url = provider.BuildListUrl(board);
            var outcome = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
            if (!outcome.Ok || outcome.Body is null)
                return (new BoardResult(board, false, 0, outcome.Error ?? "Fetch failed", outcome.Status), new());

            var jobs = provider.Parse(board, outcome.Body).ToList();
            return (new BoardResult(board, true, jobs.Count, null, outcome.Status), jobs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // whole-run cancellation propagates
        }
        catch (Exception ex)
        {
            // Unparseable body or any other provider/transport fault: isolate to this board.
            return (new BoardResult(board, false, 0, ex.Message), new());
        }
    }
}

/// <summary>
/// Resolves a board URL or shorthand into a <see cref="CompanyBoard"/>. Recognizes the public board
/// hosts for each ATS (e.g. <c>boards.greenhouse.io/acme</c>, <c>jobs.lever.co/acme</c>,
/// <c>jobs.ashbyhq.com/beacon</c>) as well as their API hosts, and a <c>kind:handle</c> shorthand.
/// </summary>
public static class BoardRegistry
{
    public static bool TryParse(string urlOrHandle, out CompanyBoard board)
    {
        board = default!;
        if (string.IsNullOrWhiteSpace(urlOrHandle)) return false;
        var s = urlOrHandle.Trim();

        // shorthand: "greenhouse:acme" / "lever:acme" / "ashby:beacon"
        var colon = s.IndexOf(':');
        if (colon > 0 && !s.Contains("//"))
        {
            var kindPart = s[..colon].Trim().ToLowerInvariant();
            var handlePart = s[(colon + 1)..].Trim();
            if (handlePart.Length > 0 && KindFromToken(kindPart) is { } k1)
            {
                board = new CompanyBoard(k1, handlePart);
                return true;
            }
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            var seg = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var handle = seg.Length > 0 ? seg[0] : null;
            if (handle is null) return false;

            var kind = KindFromHost(host);
            if (kind is { } k2)
            {
                board = new CompanyBoard(k2, handle);
                return true;
            }
        }

        return false;
    }

    private static AtsKind? KindFromHost(string host) => host switch
    {
        _ when host.Contains("greenhouse.io") => AtsKind.Greenhouse,
        _ when host.Contains("lever.co") => AtsKind.Lever,
        _ when host.Contains("ashbyhq.com") => AtsKind.Ashby,
        _ => null,
    };

    private static AtsKind? KindFromToken(string token) => token switch
    {
        "greenhouse" or "gh" => AtsKind.Greenhouse,
        "lever" or "lvr" => AtsKind.Lever,
        "ashby" => AtsKind.Ashby,
        _ => null,
    };

    /// <summary>
    /// Illustrative boards used by the demo and tests. These are example handles ("acme", "beacon"),
    /// not real companies — supply your own boards (or parse them with <see cref="TryParse"/>).
    /// </summary>
    public static IReadOnlyList<CompanyBoard> Seed { get; } = new[]
    {
        new CompanyBoard(AtsKind.Greenhouse, "acme", "Acme"),
        new CompanyBoard(AtsKind.Lever, "acme", "Acme"),
        new CompanyBoard(AtsKind.Ashby, "beacon", "Beacon Labs"),
    };
}
