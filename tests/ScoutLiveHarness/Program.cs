using SeekerSvc.Dispatcher;
using SeekerSvc.Pipeline;
using SeekerSvc.Scout;
using SeekerSvc.Store;

int passed = 0, failed = 0;
void Check(string n, bool c, string? d = null)
{ if (c) { passed++; Console.WriteLine($"  PASS  {n}"); } else { failed++; Console.WriteLine($"  FAIL  {n}{(d is null ? "" : $"  -- {d}")}"); } }

var boards = args.Length == 0
    ? DefaultBoards()
    : args.Select(ParseBoardArg).ToList();

Console.WriteLine("=== CareerSeeker Scout live board smoke ===\n");
Console.WriteLine($"Boards: {string.Join(", ", boards.Select(b => $"{b.Ats}:{b.Handle}"))}");

Console.WriteLine("\n[ registry + channel detection ]");
Check("new Greenhouse host is ATS form",
    ChannelDetector.Detect("https://job-boards.greenhouse.io/remotecom/jobs/123456", null) == DispatchChannel.AtsForm);
Check("old Greenhouse host is ATS form",
    ChannelDetector.Detect("https://boards.greenhouse.io/remotecom/jobs/123456", null) == DispatchChannel.AtsForm);
Check("Greenhouse public board URL parses",
    BoardRegistry.TryParse("https://job-boards.greenhouse.io/remotecom", out var ghPublic) &&
    ghPublic.Ats == AtsKind.Greenhouse &&
    ghPublic.Handle == "remotecom");
Check("Greenhouse API board URL parses",
    BoardRegistry.TryParse("https://boards-api.greenhouse.io/v1/boards/remotecom/jobs?content=true", out var ghApi) &&
    ghApi.Ats == AtsKind.Greenhouse &&
    ghApi.Handle == "remotecom");
Check("Lever API board URL parses",
    BoardRegistry.TryParse("https://api.lever.co/v0/postings/mistral?mode=json", out var leverApi) &&
    leverApi.Ats == AtsKind.Lever &&
    leverApi.Handle == "mistral");
Check("Ashby API board URL parses",
    BoardRegistry.TryParse("https://api.ashbyhq.com/posting-api/job-board/deel?includeCompensation=true", out var ashbyApi) &&
    ashbyApi.Ats == AtsKind.Ashby &&
    ashbyApi.Handle == "deel");

Console.WriteLine("\n[ live Scout ingest ]");
var options = ScoutOptions.Default with
{
    MaxConcurrency = 4,
    PerHostConcurrency = 1,
    MinDelayPerHost = TimeSpan.FromMilliseconds(300),
    RequestTimeout = TimeSpan.FromSeconds(30),
};

using var fetcher = new HttpBoardFetcher(options);
var scout = new Scout(fetcher, options);
using var runTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));

DiscoveryResult result;
try
{
    result = await scout.DiscoverAsync(boards, runTimeout.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("  FAIL  live Scout run completed  -- timed out after 4 minutes");
    return 1;
}

foreach (var board in result.Boards)
{
    var name = $"{board.Board.Ats}:{board.Board.Handle}";
    Check($"{name} feed reachable", board.Ok, board.Ok ? $"jobs={board.JobCount}" : $"{board.HttpStatus} {board.Error}");
    if (board.Ok && board.JobCount == 0)
        Console.WriteLine($"  WARN  {name} returned zero jobs");
}

var rawCount = result.Boards.Where(b => b.Ok).Sum(b => b.JobCount);
var remoteOrHybrid = result.Jobs.Count(j => j.Remote is RemoteMode.Remote or RemoteMode.Hybrid);
var compPresent = result.Jobs.Count(j => j.Compensation is not null);
var structuredComp = result.Jobs.Count(j => j.Compensation?.Source == CompSource.Structured);
var parsedComp = result.Jobs.Count(j => j.Compensation?.Source == CompSource.ParsedFromText);
var largeHourly = result.Jobs
    .Where(j => j.Compensation is { Source: CompSource.ParsedFromText, Interval: CompInterval.Hour, Max: > 1_000m })
    .Take(5)
    .ToList();

Check("all configured boards responded", result.BoardsFailed == 0, $"{result.BoardsOk} ok, {result.BoardsFailed} failed");
Check("deduped jobs discovered", result.Jobs.Count > 0, result.Jobs.Count.ToString());
Check("dedup count is not larger than raw feed count", result.Jobs.Count <= rawCount, $"dedup={result.Jobs.Count}, raw={rawCount}");
Check("all three ATS kinds produced jobs",
    Enum.GetValues<AtsKind>().All(kind => result.Jobs.Any(j => j.Source == kind)),
    string.Join(", ", result.Jobs.Select(j => j.Source).Distinct().OrderBy(k => k)));
Check("live mix has useful volume", rawCount >= 100, rawCount.ToString());
Check("remote/hybrid postings detected", remoteOrHybrid > 0, remoteOrHybrid.ToString());
Check("compensation detected", compPresent > 0, compPresent.ToString());
Check("structured compensation detected", structuredComp > 0, structuredComp.ToString());
Check("parsed text compensation detected", parsedComp > 0, parsedComp.ToString());
Check("large parsed ranges are not marked hourly", largeHourly.Count == 0,
    string.Join("; ", largeHourly.Select(j => $"{j.Source}:{j.BoardHandle}:{j.Title}:{j.Compensation?.RawText}")));

Console.WriteLine($"  raw jobs: {rawCount}");
Console.WriteLine($"  deduped jobs: {result.Jobs.Count}");
Console.WriteLine($"  duplicates collapsed: {result.DuplicatesCollapsed}");
Console.WriteLine($"  remote/hybrid jobs: {remoteOrHybrid}");
Console.WriteLine($"  compensation present: {compPresent}");
Console.WriteLine($"  structured comp: {structuredComp}");
Console.WriteLine($"  parsed-from-text comp: {parsedComp}");
Console.WriteLine($"  prompt-injection signals: {result.FlaggedCount}");

Console.WriteLine("\n[ Store ingest ]");
var store = new InMemorySeekerStore();
await store.InitializeAsync();

var inserted = 0;
var reposts = 0;
JobWriteResult? firstWrite = null;
foreach (var job in result.Jobs)
{
    var write = await store.IngestAsync(job);
    firstWrite ??= write;
    if (write.Inserted) inserted++; else reposts++;
}

Check("all discovered jobs reached Store", inserted + reposts == result.Jobs.Count, $"{inserted} inserted, {reposts} reposts");
var firstRow = firstWrite is null ? null : await store.GetJobAsync(firstWrite.JobId);
Check("stored job round-trips", firstRow is not null);

Console.WriteLine("\n[ sample ]");
foreach (var job in result.Jobs.Take(10))
{
    var comp = job.Compensation is null
        ? "comp n/a"
        : $"{job.Compensation.Currency ?? "?"} {job.Compensation.Min}-{job.Compensation.Max} {job.Compensation.Interval} {job.Compensation.Source}";
    Console.WriteLine($"  {job.Source}:{job.BoardHandle} | {job.Title} | {job.Remote} | {comp}");
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

static CompanyBoard ParseBoardArg(string arg)
{
    if (!BoardRegistry.TryParse(arg, out var board))
        throw new ArgumentException($"Could not parse board argument '{arg}'. Use kind:handle or a board URL.");
    return board;
}

static List<CompanyBoard> DefaultBoards() => new()
{
    new CompanyBoard(AtsKind.Greenhouse, "remotecom", "Remote.com"),
    new CompanyBoard(AtsKind.Greenhouse, "xai", "xAI"),
    new CompanyBoard(AtsKind.Greenhouse, "grafanalabs", "Grafana Labs"),
    new CompanyBoard(AtsKind.Lever, "mistral", "Mistral"),
    new CompanyBoard(AtsKind.Lever, "gohighlevel", "HighLevel"),
    new CompanyBoard(AtsKind.Lever, "rws", "RWS"),
    new CompanyBoard(AtsKind.Lever, "lever", "Lever"),
    new CompanyBoard(AtsKind.Ashby, "deel", "Deel"),
    new CompanyBoard(AtsKind.Ashby, "ramp", "Ramp"),
    new CompanyBoard(AtsKind.Ashby, "suno", "Suno"),
    new CompanyBoard(AtsKind.Ashby, "notable", "Notable"),
};
