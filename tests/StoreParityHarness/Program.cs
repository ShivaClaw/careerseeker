using SeekerSvc.Store;

try
{
    var memoryStamps = new Queue<DateTimeOffset>(Enumerable.Range(0, 100)
        .Select(i => new DateTimeOffset(2026, 7, 8, 18, 0, 0, TimeSpan.Zero).AddSeconds(i)));
    var sqliteStamps = new Queue<DateTimeOffset>(memoryStamps);
    DateTimeOffset MemoryClock() => memoryStamps.Dequeue();
    DateTimeOffset SqliteClock() => sqliteStamps.Dequeue();

    await using var sqlite = SqliteSeekerStore.ForFile(Path.Combine(Path.GetTempPath(), $"careerseeker-parity-{Guid.NewGuid():N}.db"), SqliteClock);
    var memory = new InMemorySeekerStore(MemoryClock);

    await memory.InitializeAsync();
    await sqlite.InitializeAsync();

    var company = new CompanyUpsert("greenhouse", "acme", "Acme Bio", "acme.example");
    var memoryCompany = await memory.UpsertCompanyAsync(company);
    var sqliteCompany = await sqlite.UpsertCompanyAsync(company);
    AssertEqual(memoryCompany, sqliteCompany, "company id");

    var job = new JobUpsert(
        Source: "greenhouse",
        ExternalId: "job-1",
        Url: "https://jobs.example/job-1",
        Title: "Senior Software Engineer",
        TitleCanon: "senior software engineer",
        DedupKey: "acme:senior-software-engineer",
        Remote: "Remote",
        SimHash: 12345,
        FirstSeen: "2026-07-01T00:00:00.0000000+00:00",
        ApplyUrl: "https://jobs.example/apply/job-1",
        CompMin: 150000m,
        CompMax: 190000m,
        CompCurrency: "USD",
        CompInterval: "Year",
        CompSource: "Structured");

    var memoryJob = await memory.UpsertJobAsync(memoryCompany, job);
    var sqliteJob = await sqlite.UpsertJobAsync(sqliteCompany, job);
    AssertEqual(memoryJob, sqliteJob, "job insert");
    AssertEqual(await memory.GetJobAsync(memoryJob.JobId), await sqlite.GetJobAsync(sqliteJob.JobId), "job row insert");

    var repost = job with { Url = "https://jobs.example/job-1-updated", ApplyUrl = "https://jobs.example/apply/job-1-updated", CompMin = null, CompMax = 200000m };
    memoryJob = await memory.UpsertJobAsync(memoryCompany, repost);
    sqliteJob = await sqlite.UpsertJobAsync(sqliteCompany, repost);
    AssertEqual(memoryJob, sqliteJob, "job repost");
    AssertEqual(await memory.GetJobAsync(memoryJob.JobId), await sqlite.GetJobAsync(sqliteJob.JobId), "job row repost");

    var memoryProfile = await memory.UpsertProfileAsync("{\"name\":\"Jordan\"}");
    var sqliteProfile = await sqlite.UpsertProfileAsync("{\"name\":\"Jordan\"}");
    AssertEqual(memoryProfile, sqliteProfile, "profile id");

    var claims = new[]
    {
        new ClaimRow("c1", memoryProfile, "Skill", "distributed systems", "Verified", "resume"),
        new ClaimRow("c2", memoryProfile, "Skill", "Go", "weak", "resume"),
        new ClaimRow("c3", memoryProfile, "Metric", "reduced p99 latency 30%", "stated", "resume"),
    };

    foreach (var claim in claims)
    {
        await memory.AddClaimAsync(claim);
        await sqlite.AddClaimAsync(claim with { ProfileId = sqliteProfile });
    }

    AssertEqual(await memory.GetClaimsAsync(memoryProfile), await sqlite.GetClaimsAsync(sqliteProfile), "claims");

    Console.WriteLine("PARITY OK");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label} mismatch: expected {expected}, got {actual}");
}

static void AssertEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{label} mismatch:\nexpected: {string.Join(", ", expected)}\nactual:   {string.Join(", ", actual)}");
}
