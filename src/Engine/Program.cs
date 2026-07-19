using System.Text;
using SeekerSvc.Dispatcher;
using SeekerSvc.Engine;
using SeekerSvc.Gateway;
using SeekerSvc.Pipeline;
using SeekerSvc.Scorer;
using SeekerSvc.Scout;
using SeekerSvc.Store;
using SeekerSvc.Tailor;
using SeekerSvc.Verifier;

var mode = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "demo";
if (HasFlag("--help") || HasFlag("-h"))
{
    PrintUsage();
    return 0;
}

if (mode.Equals("demo", StringComparison.OrdinalIgnoreCase))
    return await RunDemoAsync().ConfigureAwait(false);
if (mode.Equals("alpha", StringComparison.OrdinalIgnoreCase))
    return await RunAlphaAsync().ConfigureAwait(false);
if (mode.Equals("import-byok", StringComparison.OrdinalIgnoreCase))
    return RunImportByok();
if (mode.Equals("clear-byok", StringComparison.OrdinalIgnoreCase))
    return RunClearByok();
if (mode.Equals("disconnect-gmail", StringComparison.OrdinalIgnoreCase))
    return await RunDisconnectGmailAsync().ConfigureAwait(false);
return Fail($"Unknown mode '{mode}'.");

async Task<int> RunDemoAsync()
{
    var port = IntArg("--port", 7777);
    var intervalSeconds = IntArg("--interval-seconds", 30);
    var once = HasFlag("--once");

    var store = await SeededStoreAsync().ConfigureAwait(false);
    var counters = new EngineCounters();
    var cycle = BuildDemoCycle(store, counters);

    if (once)
    {
        await cycle.TickAsync().ConfigureAwait(false);
        PrintCounters(counters);
        return 0;
    }

    await using var host = new EngineHost(cycle, counters, TimeSpan.FromSeconds(intervalSeconds), port);
    using var stop = new CancellationTokenSource();
    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    Console.CancelKeyPress += OnCancel;
    try
    {
        host.Start();
        Console.WriteLine("CareerSeeker alpha demo host is running.");
        Console.WriteLine($"Dashboard: http://localhost:{port}/");
        Console.WriteLine("Press Enter or Ctrl+C to stop.");

        var readLine = Task.Run(Console.ReadLine, stop.Token);
        await Task.WhenAny(readLine, stopped.Task).ConfigureAwait(false);
        return 0;
    }
    finally
    {
        Console.CancelKeyPress -= OnCancel;
        stop.Cancel();
        PrintCounters(counters);
    }

    void OnCancel(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        stopped.TrySetResult();
    }
}

EngineCycle BuildDemoCycle(InMemorySeekerStore store, EngineCounters counters)
    => BuildDemoCycleCore(store, counters, new DemoGmailDraftClient(), new DemoPostingSource(
        new PostingDispatchInfo(DispatchChannel.Email, "jobs@feed.example")), "CareerSeeker Alpha",
        "alpha@careerseeker.app", new DemoFeed(), "feed", "Discovered");

async Task<int> RunAlphaAsync()
{
    var envFilePath = StringArg("--secrets") ?? Path.Combine("secrets", "env.secrets");
    var email = StringArg("--email")
                ?? Environment.GetEnvironmentVariable("CAREERSEEKER_GMAIL_TEST_EMAIL")
                ?? EnvFileValue(envFilePath, "CAREERSEEKER_GMAIL_TEST_EMAIL");
    var clientPath = StringArg("--client") ?? DefaultExisting("secrets/google-oauth-client.json", "client_secret.json");
    var vaultPath = StringArg("--vault") ?? Path.Combine(".appdata", "oauth", "gmail-token.dpapi");
    var dbPath = StringArg("--db") ?? Path.Combine(".appdata", "careerseeker-alpha.db");
    var llmMode = StringArg("--llm") ?? "fake";
    var keyVaultPath = StringArg("--key-vault") ?? Path.Combine(".appdata", "secrets", "byok-keys.dpapi");

    if (!string.IsNullOrWhiteSpace(email) && (email.StartsWith("--", StringComparison.Ordinal) || !email.Contains('@')))
        return Fail("Alpha mode received an invalid --email value.");
    if (string.IsNullOrWhiteSpace(clientPath) || !File.Exists(clientPath))
        return Fail($"Alpha mode cannot find OAuth client JSON at '{clientPath ?? "<none>"}'.");

    var dbDir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrWhiteSpace(dbDir)) Directory.CreateDirectory(dbDir);

    await using var store = SqliteSeekerStore.ForFile(dbPath);
    await store.InitializeAsync().ConfigureAwait(false);
    var profileId = await SeedProfileAsync(store).ConfigureAwait(false);

    using var http = new HttpClient();
    var client = GoogleOAuthClient.Load(clientPath);
    var vault = new DpapiTokenVault(vaultPath);
    var tokens = new GoogleOAuthTokenSource(http, client, vault, allowInteractive: true,
        authorizationUrlSink: url =>
        {
            Console.WriteLine("Open this URL if the browser did not appear:");
            Console.WriteLine(url);
        });

    Console.WriteLine("CareerSeeker alpha live Gmail smoke");
    Console.WriteLine($"  db: {dbPath}");
    Console.WriteLine($"  oauth client: {clientPath}");
    Console.WriteLine($"  token vault: {vaultPath} ({(File.Exists(vaultPath) ? "present" : "will create")})");
    Console.WriteLine($"  gmail account: {(string.IsNullOrWhiteSpace(email) ? "will read from Gmail profile" : email)}");
    Console.WriteLine($"  llm mode: {llmMode}");

    await tokens.GetTokenAsync().ConfigureAwait(false);
    Console.WriteLine("  OAuth token: available");
    var gmail = new GmailDraftClient(http, tokens);
    await gmail.PreflightDraftAccessAsync().ConfigureAwait(false);
    Console.WriteLine("  Gmail drafts API: reachable");
    if (string.IsNullOrWhiteSpace(email))
        email = await gmail.GetProfileEmailAsync().ConfigureAwait(false);
    Console.WriteLine("  Gmail profile: available");

    var gateway = BuildGateway(llmMode, envFilePath, keyVaultPath, http, out var keySourceName, out var byokProviders);
    if (llmMode.Equals("byok", StringComparison.OrdinalIgnoreCase))
        Console.WriteLine($"  BYOK providers ({keySourceName}): " + string.Join(", ", byokProviders));

    var counters = new EngineCounters();
    var cycle = BuildDemoCycleCore(
        store,
        counters,
        gmail,
        new DemoPostingSource(new PostingDispatchInfo(DispatchChannel.Email, email)),
        "CareerSeeker Alpha",
        email,
        new AlphaSmokeFeed(),
        "alpha",
        "CareerSeeker Alpha",
        profileId,
        gateway);

    await cycle.TickAsync().ConfigureAwait(false);
    PrintCounters(counters);

    var audit = await store.VerifyAuditAsync().ConfigureAwait(false);
    Console.WriteLine($"  audit chain: {(audit.Ok ? "ok" : "FAILED")}");
    return counters.Errors == 0 && counters.Drafted == 1 && audit.Ok ? 0 : 1;
}

int RunImportByok()
{
    var envFilePath = StringArg("--secrets") ?? Path.Combine("secrets", "env.secrets");
    var keyVaultPath = StringArg("--key-vault") ?? Path.Combine(".appdata", "secrets", "byok-keys.dpapi");
    var source = EnvironmentApiKeySource.Load(envFilePath);
    var providers = source.ProvidersPresent();
    if (providers.Count == 0)
        return Fail($"No BYOK keys found in environment variables or '{envFilePath}'. Expected ANTHROPIC_API_KEY and GEMINI_API_KEY or GOOGLE_API_KEY.");

    var values = providers.ToDictionary(provider => provider, source.GetKey, StringComparer.OrdinalIgnoreCase);
    new DpapiSecretVault(keyVaultPath).Save(values);

    Console.WriteLine("CareerSeeker BYOK import");
    Console.WriteLine($"  key vault: {keyVaultPath}");
    Console.WriteLine("  imported providers: " + string.Join(", ", providers));
    Console.WriteLine("  values were not printed.");
    return 0;
}

int RunClearByok()
{
    var keyVaultPath = StringArg("--key-vault") ?? Path.Combine(".appdata", "secrets", "byok-keys.dpapi");
    var vault = new DpapiSecretVault(keyVaultPath);
    var existed = vault.Exists;
    vault.Delete();

    Console.WriteLine("CareerSeeker BYOK clear");
    Console.WriteLine($"  key vault: {keyVaultPath}");
    Console.WriteLine(existed ? "  local provider-key vault deleted." : "  no local provider-key vault was found.");
    return 0;
}

async Task<int> RunDisconnectGmailAsync()
{
    var vaultPath = StringArg("--vault") ?? Path.Combine(".appdata", "oauth", "gmail-token.dpapi");
    var clientPath = StringArg("--client") ?? DefaultExisting("secrets/google-oauth-client.json", "client_secret.json");
    var client = !string.IsNullOrWhiteSpace(clientPath) && File.Exists(clientPath)
        ? GoogleOAuthClient.Load(clientPath)
        : new GoogleOAuthClient(
            "",
            null,
            "https://accounts.google.com/o/oauth2/auth",
            "https://oauth2.googleapis.com/token",
            "https://oauth2.googleapis.com/revoke");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var vault = new DpapiTokenVault(vaultPath);
    var tokens = new GoogleOAuthTokenSource(http, client, vault);

    Console.WriteLine("CareerSeeker Gmail disconnect");
    Console.WriteLine($"  token vault: {vaultPath}");
    Console.WriteLine($"  oauth client: {(File.Exists(clientPath ?? "") ? clientPath : "default Google revoke endpoint")}");

    try
    {
        var disconnected = await tokens.DisconnectAsync().ConfigureAwait(false);
        Console.WriteLine(disconnected
            ? "  Gmail token revoked and local vault deleted."
            : "  No local Gmail token was found.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("  Gmail disconnect did not complete cleanly.");
        Console.Error.WriteLine("  " + ex.Message);
        return 1;
    }
}

EngineCycle BuildDemoCycleCore(
    ISeekerStore store,
    EngineCounters counters,
    IGmailDraftClient gmail,
    IPostingSource postingSource,
    string candidateName,
    string candidateEmail,
    IJobFeed feed,
    string companyHandle,
    string companyName,
    long profileId = 1,
    LlmGateway? gateway = null)
{
    gateway ??= BuildFakeGateway();

    ITailor tailor = new SeekerSvc.Tailor.Tailor(new GatewayTailorModel(gateway));
    var dispatcher = new SeekerSvc.Dispatcher.Dispatcher(
        postingSource,
        new AtsPdfDocumentRenderer(new AtsPdfRendererOptions(candidateName)),
        gmail,
        new DispatcherConfig(candidateName, candidateEmail));

    var pipeline = new ApplicationPipeline(
        store,
        tailor,
        dispatcher,
        matcher: new GatewaySemanticMatcher(gateway),
        options: new PipelineOptions
        {
            ProfileId = profileId,
            Channel = DispatchChannel.Email,
        });

    var prefs = new UserPreferences
    {
        Comp = new CompTarget(150000m, 180000m, 220000m),
        Remote = RemoteStance.Any,
        Seniority = SeniorityBand.Senior,
    };

    return new EngineCycle(
        store,
        feed,
        new DemoSemanticScorer(),
        pipeline,
        new EngineOptions(prefs, AutonomyLevel.L1, DispatchChannel.Email, profileId, companyHandle, companyName),
        counters);
}

LlmGateway BuildFakeGateway() => new(
    RoutingTable.Default(),
    GatewayMode.Managed,
    new BudgetMeter(1000m),
    new ILlmProvider[]
    {
        new FakeProvider("anthropic", respond: RespondForDemo),
        new FakeProvider("google"),
        new FakeProvider("local", isLocal: true),
    });

LlmGateway BuildGateway(
    string mode,
    string envFilePath,
    string keyVaultPath,
    HttpClient http,
    out string keySourceName,
    out IReadOnlyList<string> byokProviders)
{
    keySourceName = "fake";
    byokProviders = Array.Empty<string>();
    if (!mode.Equals("byok", StringComparison.OrdinalIgnoreCase))
    {
        if (!mode.Equals("fake", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported --llm value. Use 'fake' or 'byok'.");
        return BuildFakeGateway();
    }

    var keys = LoadByokKeySource(envFilePath, keyVaultPath, out keySourceName);
    byokProviders = keys.ProvidersPresent();
    var providers = new List<ILlmProvider>();
    if (keys.HasKey("anthropic")) providers.Add(new AnthropicProvider(http, keys));
    if (keys.HasKey("google")) providers.Add(new GoogleProvider(http, keys));
    if (providers.Count == 0)
        throw new InvalidOperationException(
            $"BYOK mode could not find provider keys in DPAPI vault '{keyVaultPath}', environment variables, or '{envFilePath}'. Expected ANTHROPIC_API_KEY and GEMINI_API_KEY or GOOGLE_API_KEY.");

    return new LlmGateway(
        RoutingTable.Default(),
        GatewayMode.Byok,
        new BudgetMeter(1000m),
        providers);
}

EnvironmentApiKeySource LoadByokKeySource(string envFilePath, string keyVaultPath, out string sourceName)
{
    var vault = new DpapiSecretVault(keyVaultPath);
    var vaulted = vault.Load();
    if (vaulted.Count > 0)
    {
        sourceName = "dpapi";
        return new EnvironmentApiKeySource(vaulted);
    }

    sourceName = File.Exists(envFilePath) ? "env.secrets" : "environment";
    return EnvironmentApiKeySource.Load(envFilePath);
}

async Task<InMemorySeekerStore> SeededStoreAsync()
{
    var store = new InMemorySeekerStore();
    await SeedProfileAsync(store).ConfigureAwait(false);
    return store;
}

async Task<long> SeedProfileAsync(ISeekerStore store)
{
    var profileId = await store.UpsertProfileAsync("{}").ConfigureAwait(false);

    foreach (var (kind, text) in new[]
    {
        ("Title", "Senior Software Engineer"),
        ("Skill", "distributed systems"),
        ("Skill", "Go"),
        ("Skill", "reliable"),
        ("Skill", "experience"),
        ("Skill", "team"),
        ("Employer", "Acme"),
        ("Metric", "reduced p99 latency 30%"),
        ("Other", "Senior Software Engineer experienced in distributed systems and Go"),
        ("Other", "I have built reliable distributed systems in Go and would bring that experience to your team"),
    })
    {
        await store.AddClaimAsync(new ClaimRow(Guid.NewGuid().ToString("N"), profileId, kind, text, "Verified"))
            .ConfigureAwait(false);
    }

    return profileId;
}

string RespondForDemo(ProviderCall call)
{
    var prompt = string.Join("\n", call.Messages.Select(m => m.Content));
    if (prompt.Contains("Decide whether SOURCE FACTS fully support", StringComparison.Ordinal))
    {
        var fabricated = prompt.Contains("increased company revenue", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("increased revenue 200", StringComparison.OrdinalIgnoreCase);
        return fabricated ? "{\"entailed\":false}" : "{\"entailed\":true}";
    }

    return prompt.Contains("Fabricator", StringComparison.OrdinalIgnoreCase)
        ? "{\"resume\":\"Senior Software Engineer.\",\"cover\":\"I personally increased company revenue by 200% in one quarter.\",\"claims\":[{\"kind\":\"Metric\",\"text\":\"increased revenue 200%\",\"number\":200,\"unit\":\"%\"}],\"answers\":{}}"
        : "{\"resume\":\"Senior Software Engineer experienced in distributed systems and Go.\",\"cover\":\"I am excited to apply. I have built reliable distributed systems in Go and would bring that experience to your team.\",\"claims\":[],\"answers\":{}}";
}

void PrintCounters(EngineCounters counters)
{
    Console.WriteLine();
    Console.WriteLine("Final counters");
    Console.WriteLine($"  cycles: {counters.Cycles}");
    Console.WriteLine($"  discovered: {counters.Discovered}");
    Console.WriteLine($"  acted: {counters.Acted}");
    Console.WriteLine($"  drafted: {counters.Drafted}");
    Console.WriteLine($"  blocked: {counters.Blocked}");
    Console.WriteLine($"  rejected: {counters.Rejected}");
    Console.WriteLine($"  errors: {counters.Errors}");
}

bool HasFlag(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

int IntArg(string name, int fallback)
{
    for (var i = 0; i + 1 < args.Length; i++)
    {
        if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
        return int.TryParse(args[i + 1], out var value) ? value : fallback;
    }
    return fallback;
}

string? StringArg(string name)
{
    for (var i = 0; i + 1 < args.Length; i++)
    {
        if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
        return args[i + 1].StartsWith("--", StringComparison.Ordinal) ? null : args[i + 1];
    }
    return null;
}

string? EnvFileValue(string path, string name)
{
    if (!File.Exists(path)) return null;
    foreach (var line in File.ReadLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var idx = trimmed.IndexOf('=');
        if (idx <= 0) continue;
        if (!trimmed[..idx].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
        var value = trimmed[(idx + 1)..].Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
    return null;
}

static string? DefaultExisting(params string[] paths) => paths.FirstOrDefault(File.Exists) ?? paths.FirstOrDefault();

int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}

void PrintUsage()
{
    Console.WriteLine("CareerSeeker alpha executable");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  SeekerSvc.Engine.exe demo [--once] [--port 7777] [--interval-seconds 30]");
    Console.WriteLine("  SeekerSvc.Engine.exe alpha --email you@gmail.com [--llm fake|byok] [--secrets secrets/env.secrets] [--key-vault .appdata/secrets/byok-keys.dpapi] [--client secrets/google-oauth-client.json] [--vault .appdata/oauth/gmail-token.dpapi] [--db .appdata/careerseeker-alpha.db]");
    Console.WriteLine("  SeekerSvc.Engine.exe import-byok [--secrets secrets/env.secrets] [--key-vault .appdata/secrets/byok-keys.dpapi]");
    Console.WriteLine("  SeekerSvc.Engine.exe clear-byok [--key-vault .appdata/secrets/byok-keys.dpapi]");
    Console.WriteLine("  SeekerSvc.Engine.exe disconnect-gmail [--client secrets/google-oauth-client.json] [--vault .appdata/oauth/gmail-token.dpapi]");
}

sealed class AlphaSmokeFeed : IJobFeed
{
    public Task<IReadOnlyList<JobPosting>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JobPosting>>(new[]
        {
            new JobPosting
            {
                Title = "Senior Software Engineer",
                TitleCanon = "senior software engineer",
                Location = "Remote",
                Remote = RemoteMode.Remote,
                Compensation = new Compensation(170000m, 210000m, "USD", CompInterval.Year, CompSource.Structured),
                DescriptionText = new string('x', 40) + " Build distributed systems in Go, own services, mentor peers, improve reliability. Clear team and mission.",
                RepostCount = 0,
                FirstPublished = DateTimeOffset.UtcNow.AddDays(-3),
                RecruiterIdentifiable = true,
                CompanyDomainVerified = true,
            },
        });
}

sealed class DemoFeed : IJobFeed
{
    public Task<IReadOnlyList<JobPosting>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JobPosting>>(new[]
        {
            HealthyPosting("Senior Software Engineer"),
            ScamPosting(),
            HealthyPosting("Fabricator Role"),
        });

    private static JobPosting HealthyPosting(string title) => new()
    {
        Title = title,
        TitleCanon = title.ToLowerInvariant(),
        Location = "Remote",
        Remote = RemoteMode.Remote,
        Compensation = new Compensation(170000m, 210000m, "USD", CompInterval.Year, CompSource.Structured),
        DescriptionText = new string('x', 40) + " Build distributed systems in Go, own services, mentor peers, improve reliability. Clear team and mission.",
        RepostCount = 0,
        FirstPublished = DateTimeOffset.UtcNow.AddDays(-3),
        RecruiterIdentifiable = true,
        CompanyDomainVerified = true,
    };

    private static JobPosting ScamPosting() => new()
    {
        Title = "URGENT WORK FROM HOME",
        TitleCanon = "data entry",
        Remote = RemoteMode.Remote,
        Compensation = null,
        DescriptionText = "Earn $$$ fast. Wire transfer required. Send SSN now. No experience.",
        RepostCount = 9,
        FirstPublished = DateTimeOffset.UtcNow.AddDays(-200),
        DescriptionLikelyInjected = true,
        RecruiterIdentifiable = false,
        CompanyDomainVerified = false,
    };
}

sealed class DemoSemanticScorer : ISemanticScorer
{
    public Task<SemanticScores> ScoreAsync(JobPosting posting, CancellationToken ct = default) =>
        Task.FromResult(new SemanticScores(4.6, 4.2));
}

sealed class DemoPostingSource : IPostingSource
{
    private readonly PostingDispatchInfo _info;

    public DemoPostingSource(PostingDispatchInfo info) => _info = info;

    public Task<PostingDispatchInfo> GetDispatchInfoAsync(long jobId, CancellationToken ct = default) =>
        Task.FromResult(_info);
}

sealed class DemoGmailDraftClient : IGmailDraftClient
{
    private int _drafts;

    public Task<string> CreateDraftAsync(string raw, IReadOnlyList<string> labelIds, CancellationToken ct = default) =>
        Task.FromResult("demo-draft-" + Interlocked.Increment(ref _drafts));
}
