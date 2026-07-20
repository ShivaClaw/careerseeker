using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SeekerSvc.Dispatcher;
using SeekerSvc.Engine;
using SeekerSvc.Gateway;
using SeekerSvc.Pipeline;
using SeekerSvc.Scorer;
using SeekerSvc.Scout;
using SeekerSvc.Store;
using SeekerSvc.Tailor;
using SeekerSvc.Verifier;

int passed = 0, failed = 0;
void Check(string n, bool c, string? d = null)
{ if (c) { passed++; Console.WriteLine($"  PASS  {n}"); } else { failed++; Console.WriteLine($"  FAIL  {n}{(d is null ? "" : $"  -- {d}")}"); } }

string HtmlRowContaining(string html, string marker)
{
    var markerAt = html.IndexOf(marker, StringComparison.Ordinal);
    if (markerAt < 0) return "";
    var start = html.LastIndexOf("<tr", markerAt, StringComparison.Ordinal);
    var end = html.IndexOf("</tr>", markerAt, StringComparison.Ordinal);
    return start >= 0 && end >= start ? html[start..(end + "</tr>".Length)] : "";
}

const string clean =
    "{\"resume\":\"Senior Software Engineer experienced in distributed systems and Go.\"," +
    "\"cover\":\"I am excited to apply. I have built reliable distributed systems in Go and would bring that experience to your team.\",\"claims\":[],\"answers\":{}}";
const string fabricated =
    "{\"resume\":\"Senior Software Engineer.\",\"cover\":\"I personally increased company revenue by 200% in one quarter.\"," +
    "\"claims\":[{\"kind\":\"Metric\",\"text\":\"increased revenue 200%\",\"number\":200,\"unit\":\"%\"}],\"answers\":{}}";

// one Tailor serving the whole cycle: fabricates only when the prompt names the "Fabricator" job
string Respond(ProviderCall call)
{
    var prompt = string.Join("\n", call.Messages.Select(m => m.Content));
    if (prompt.Contains("Decide whether SOURCE FACTS fully support", StringComparison.Ordinal))
        return "{\"entailed\":true}";
    return prompt.Contains("Fabricator") ? fabricated : clean;
}
var gateway = new LlmGateway(RoutingTable.Default(), GatewayMode.Managed, new BudgetMeter(1000m),
    new ILlmProvider[] { new FakeProvider("anthropic", respond: Respond), new FakeProvider("google"), new FakeProvider("local", true) });
ITailor tailor = new Tailor(new GatewayTailorModel(gateway));

async Task<InMemorySeekerStore> SeededStoreAsync()
{
    var store = new InMemorySeekerStore();
    await SeedProfileAsync(store);
    return store;
}

async Task<long> SeedProfileAsync(ISeekerStore store)
{
    var pid = await store.UpsertProfileAsync("{}");
    foreach (var (k, t) in new[] { ("Title","Senior Software Engineer"), ("Skill","distributed systems"),
        ("Skill","Go"), ("Skill","reliable"), ("Skill","experience"), ("Skill","team"), ("Employer","Acme"), ("Metric","reduced p99 latency 30%"),
        ("Other","Senior Software Engineer experienced in distributed systems and Go"),
        ("Other","I have built reliable distributed systems in Go and would bring that experience to your team") })
        await store.AddClaimAsync(new ClaimRow(Guid.NewGuid().ToString("N"), pid, k, t, "Verified"));
    return pid;
}

JobPosting Healthy(string title) => new()
{
    Title = title, TitleCanon = title.ToLowerInvariant(), Location = "Remote", Remote = RemoteMode.Remote,
    Compensation = new Compensation(170000m, 210000m, "USD", CompInterval.Year, CompSource.Structured),
    DescriptionText = new string('x', 40) + " Build distributed systems in Go, own services, mentor peers, improve reliability. Clear team and mission.",
    RepostCount = 0, FirstPublished = DateTimeOffset.UtcNow.AddDays(-3),
    RecruiterIdentifiable = true, CompanyDomainVerified = true,
};
JobPosting Scam() => new()
{
    Title = "URGENT WORK FROM HOME", TitleCanon = "data entry", Remote = RemoteMode.Remote, Compensation = null,
    DescriptionText = "Earn $$$ fast. Wire transfer required. Send SSN now. No experience.",
    RepostCount = 9, FirstPublished = DateTimeOffset.UtcNow.AddDays(-200), DescriptionLikelyInjected = true,
    RecruiterIdentifiable = false, CompanyDomainVerified = false,
};

var prefs = new UserPreferences { Comp = new CompTarget(150000m, 180000m, 220000m), Remote = RemoteStance.Any, Seniority = SeniorityBand.Senior };
var opt = new EngineOptions(prefs, AutonomyLevel.L1, DispatchChannel.Email);

Console.WriteLine("=== CareerSeeker Engine host (P2 shell) ===\n");

// ── 1) one real cycle over a mixed batch ──────────────────────────────────────────────────────────
Console.WriteLine("[ one cycle over a mixed batch ]");
var counters = new EngineCounters();
{
    var store = await SeededStoreAsync();
    var feed = new FakeFeed(new[] { Healthy("Senior Software Engineer"), Scam(), Healthy("Fabricator Role") });
    var pipeline = new ApplicationPipeline(store, tailor, MakeDispatcher(new FakeGmail()), new GatewaySemanticMatcher(gateway),
        new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });
    var cycle = new EngineCycle(store, feed, new FakeSemantic(), pipeline, opt, counters);

    await cycle.TickAsync();
    Check("discovered 3", counters.Discovered == 3, counters.Discovered.ToString());
    Check("drafted 1 (healthy, clean)", counters.Drafted == 1, counters.Drafted.ToString());
    Check("rejected 1 (scam floor)", counters.Rejected == 1, counters.Rejected.ToString());
    Check("blocked 1 (fabrication caught in-loop)", counters.Blocked == 1, counters.Blocked.ToString());
    Check("errors 0", counters.Errors == 0, counters.Errors.ToString());
    Check("cycles 1", counters.Cycles == 1);
    Check("audit chain intact after cycle", (await store.VerifyAuditAsync()).Ok);
}

Console.WriteLine("\n[ SQLite engine composition ]");
{
    var dbPath = Path.Combine(Path.GetTempPath(), "careerseeker-engineharness-" + Guid.NewGuid().ToString("N") + ".db");
    try
    {
        {
            await using var store = SqliteSeekerStore.ForFile(dbPath);
            await store.InitializeAsync();
            var profileId = await SeedProfileAsync(store);
            var sqliteCounters = new EngineCounters();
            var feed = new FakeFeed(new[] { Healthy("Senior Software Engineer") });
            var pipeline = new ApplicationPipeline(store, tailor, MakeDispatcher(new FakeGmail()), new GatewaySemanticMatcher(gateway),
                new PipelineOptions { ProfileId = profileId, Channel = DispatchChannel.Email });
            var cycle = new EngineCycle(store, feed, new FakeSemantic(), pipeline,
                opt with { ProfileId = profileId, CompanyHandle = "sqlite", CompanyName = "SQLite Demo" },
                sqliteCounters);

            await cycle.TickAsync();
            Check("SQLite-backed cycle drafts one application", sqliteCounters.Drafted == 1, sqliteCounters.Drafted.ToString());
            Check("SQLite-backed cycle audit chain intact", (await store.VerifyAuditAsync()).Ok);
        }
    }
    finally
    {
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(path))
                try { File.Delete(path); } catch (IOException) { }
    }
}

// ── 2) selected-job drafting refuses prompt-injection flags by default ─────────────────────────────
Console.WriteLine("\n[ selected-job draft prompt-injection rail ]");
{
    var root = Path.Combine(Path.GetTempPath(), "careerseeker-draftjob-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "alpha.db");
        var artifacts = Path.Combine(root, "artifacts");
        long jobId;
        await using (var sqlite = SqliteSeekerStore.ForFile(dbPath))
        {
            await sqlite.InitializeAsync();
            var companyId = await sqlite.UpsertCompanyAsync(
                new CompanyUpsert("greenhouse", "injection-test", "Injection Test"));
            var seeded = await sqlite.UpsertJobAsync(companyId, new JobUpsert(
                Source: "greenhouse",
                ExternalId: "injected-job",
                Url: "https://jobs.example/injected-job",
                Title: "Senior Software Engineer",
                TitleCanon: "senior software engineer",
                DedupKey: "injection-test|senior software engineer|remote",
                Remote: "Remote",
                SimHash: 42,
                FirstSeen: DateTimeOffset.UtcNow.ToString("O"),
                ApplyUrl: "https://apply.example/injected-job",
                Location: "Remote",
                Injected: true,
                InjectionSignals: "ignore_previous_instructions"));
            jobId = seeded.JobId;
        }

        var refused = await RunEngineCommandAsync(
            "draft-job",
            "--job-id", jobId.ToString(),
            "--dry-run",
            "--llm", "fake",
            "--db", dbPath,
            "--artifacts", artifacts);
        Check("draft-job refuses prompt-injection flagged jobs by default",
            refused.ExitCode != 0 &&
            refused.Output.Contains("refused job", StringComparison.OrdinalIgnoreCase) &&
            refused.Output.Contains("prompt-injection", StringComparison.OrdinalIgnoreCase),
            refused.Output);

        var allowed = await RunEngineCommandAsync(
            "draft-job",
            "--job-id", jobId.ToString(),
            "--dry-run",
            "--llm", "fake",
            "--allow-injected",
            "--db", dbPath,
            "--artifacts", artifacts);
        Check("draft-job allows flagged jobs only with explicit override",
            allowed.ExitCode == 0 &&
            allowed.Output.Contains("final state: DRAFTED", StringComparison.OrdinalIgnoreCase),
            allowed.Output);
    }
    finally
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}

// ── 3) scheduler runs repeatedly then stops cleanly ────────────────────────────────────────────────
Console.WriteLine("\n[ scheduler ]");
{
    var ticks = 0;
    var sched = new PeriodicScheduler(_ => { Interlocked.Increment(ref ticks); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(20));
    sched.Start();
    await Task.Delay(150);
    var seen = ticks;
    Check("scheduler fired repeatedly (immediate + interval)", seen >= 3, seen.ToString());
    await sched.DisposeAsync();
    var afterDispose = ticks;
    await Task.Delay(60);
    Check("scheduler stopped after dispose", ticks == afterDispose, $"{afterDispose}->{ticks}");
}

// ── 3) live localhost dashboard over HTTP ──────────────────────────────────────────────────────────
Console.WriteLine("\n[ localhost dashboard ]");
{
    var disconnects = 0;
    var appControls = 0;
    var auditExports = 0;
    var packageExports = 0;
    var evidenceStore = await SeededStoreAsync();
    var artifactDir = Path.Combine(Path.GetTempPath(), "careerseeker-engineharness-artifacts-" + Guid.NewGuid().ToString("N"));
    var evidenceCounters = new EngineCounters();
    var evidencePipeline = new ApplicationPipeline(evidenceStore, tailor, MakeDispatcher(new FakeGmail(), artifactDir), new GatewaySemanticMatcher(gateway),
        new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });
    var evidenceCycle = new EngineCycle(evidenceStore, new FakeFeed(new[] { Healthy("Senior Software Engineer") }),
        new FakeSemantic(), evidencePipeline, opt, evidenceCounters);
    await evidenceCycle.TickAsync();
    await evidenceStore.AppendEventAsync(new EventInput("engine", "dashboard-test", "application", "1"));
    var applicationId = (await evidenceStore.GetRecentApplicationsAsync()).First().ApplicationId;
    var actions = new LocalDashboardActions(
        DisconnectGmailAsync: _ =>
        {
            Interlocked.Increment(ref disconnects);
            return Task.FromResult(new DashboardControlResult(true, "Gmail disconnected."));
        },
        ControlApplicationAsync: async (id, action, ct) =>
        {
            Interlocked.Increment(ref appControls);
            if (action == "pause")
                await evidencePipeline.PauseAsync(id, ct);
            return new DashboardControlResult(true, "Application controlled.");
        },
        ExportAuditAsync: _ =>
        {
            Interlocked.Increment(ref auditExports);
            return Task.FromResult(new DashboardControlResult(true, "Audit JSON exported."));
        },
        ExportAlphaPackageAsync: _ =>
        {
            Interlocked.Increment(ref packageExports);
            return Task.FromResult(new DashboardControlResult(true, "Alpha package exported."));
        });
    var dash = new LocalDashboard(counters, 7777, actions, LocalDashboardEvidence.FromStore(evidenceStore));
    var listenerOk = true;
    try { dash.Start(); } catch (Exception e) { listenerOk = false; Console.WriteLine("    (HttpListener unavailable in sandbox: " + e.GetType().Name + ")"); }

    if (listenerOk)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var json = await http.GetStringAsync("http://localhost:7777/status");
        using var doc = JsonDocument.Parse(json);
        Check("/status serves JSON with live counters", doc.RootElement.GetProperty("drafted").GetInt64() == 1, json);
        Check("/status reports Gmail control availability", doc.RootElement.GetProperty("gmailDisconnectAvailable").GetBoolean(), json);
        Check("/status reports application control availability", doc.RootElement.GetProperty("applicationControlAvailable").GetBoolean(), json);
        Check("/status reports audit export availability", doc.RootElement.GetProperty("auditExportAvailable").GetBoolean(), json);
        Check("/status reports alpha package export availability", doc.RootElement.GetProperty("alphaPackageExportAvailable").GetBoolean(), json);
        Check("/status reports evidence availability", doc.RootElement.GetProperty("evidenceAvailable").GetBoolean(), json);
        Check("/status reports job evidence availability", doc.RootElement.GetProperty("jobsAvailable").GetBoolean(), json);
        var html = await http.GetStringAsync("http://localhost:7777/");
        Check("/ serves the HTML status page", html.Contains("CareerSeeker") && html.Contains("Drafted"));
        Check("/ exposes configured Gmail disconnect control", html.Contains("Disconnect Gmail"));
        Check("/ exposes configured audit export control", html.Contains("Export Audit JSON"));
        Check("/ exposes configured alpha package export control", html.Contains("Export Alpha Package"));
        Check("/ links to audit evidence", html.Contains("/evidence.html") && html.Contains("audit-chain"));
        Check("/ links to recent applications", html.Contains("/applications"));
        Check("/ links to recent jobs", html.Contains("/jobs"));
        var token = DashboardToken(html);

        var applicationsHtml = await http.GetStringAsync("http://localhost:7777/applications");
        Check("/applications serves recent job/state drill-down",
            applicationsHtml.Contains("Senior Software Engineer") &&
            applicationsHtml.Contains("DRAFTED") &&
            applicationsHtml.Contains("SUCCEEDED") &&
            applicationsHtml.Contains(">resume</a>"),
            applicationsHtml);
        Check("/applications links documents through localhost dashboard",
            applicationsHtml.Contains($@"/documents/{applicationId}/resume") &&
            applicationsHtml.Contains("token=") &&
            !applicationsHtml.Contains("file://", StringComparison.OrdinalIgnoreCase),
            applicationsHtml);
        var badDocument = await http.GetAsync($"http://localhost:7777/documents/{applicationId}/resume?token=wrong");
        Check("/documents rejects a bad token",
            badDocument.StatusCode == HttpStatusCode.Forbidden,
            badDocument.StatusCode.ToString());
        var resumePdf = await http.GetByteArrayAsync($"http://localhost:7777/documents/{applicationId}/resume?token={Uri.EscapeDataString(token)}");
        Check("/documents serves generated resume PDF bytes",
            resumePdf.Length >= 4 &&
            resumePdf[0] == 0x25 &&
            resumePdf[1] == 0x50 &&
            resumePdf[2] == 0x44 &&
            resumePdf[3] == 0x46,
            Convert.ToHexString(resumePdf));
        Check("/applications exposes local application controls",
            applicationsHtml.Contains("action=\"/controls/application\"") &&
            applicationsHtml.Contains("value=\"pause\"") &&
            applicationsHtml.Contains("value=\"kill\""),
            applicationsHtml);

        var jobsHtml = await http.GetStringAsync("http://localhost:7777/jobs");
        Check("/jobs serves recent job drill-down",
            jobsHtml.Contains("Senior Software Engineer") &&
            jobsHtml.Contains("Remote") &&
            jobsHtml.Contains("feed:"),
            jobsHtml);

        var evidenceHtml = await http.GetStringAsync("http://localhost:7777/evidence.html");
        Check("/evidence.html serves human audit evidence",
            evidenceHtml.Contains("Audit evidence") &&
            evidenceHtml.Contains("Hash chain verified") &&
            evidenceHtml.Contains("dashboard-test") &&
            evidenceHtml.Contains("/evidence"),
            evidenceHtml);

        var evidenceJson = await http.GetStringAsync("http://localhost:7777/evidence");
        using var evidenceDoc = JsonDocument.Parse(evidenceJson);
        Check("/evidence reports intact audit chain",
            evidenceDoc.RootElement.GetProperty("auditOk").GetBoolean(), evidenceJson);
        Check("/evidence exposes recent audit event metadata without payloads",
            evidenceDoc.RootElement.GetProperty("recentEvents").GetArrayLength() > 0 &&
            !evidenceJson.Contains("PayloadJson", StringComparison.OrdinalIgnoreCase),
            evidenceJson);
        Check("/evidence includes recent application metadata",
            evidenceDoc.RootElement.GetProperty("recentApplications").GetArrayLength() > 0 &&
            evidenceJson.Contains("Senior Software Engineer") &&
            !evidenceJson.Contains("resume\":\"", StringComparison.OrdinalIgnoreCase),
            evidenceJson);
        Check("/evidence includes recent job metadata without descriptions",
            evidenceDoc.RootElement.GetProperty("recentJobs").GetArrayLength() > 0 &&
            evidenceJson.Contains("Senior Software Engineer") &&
            !evidenceJson.Contains("DescriptionText", StringComparison.OrdinalIgnoreCase),
            evidenceJson);

        using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(3) };
        var forged = await noRedirect.PostAsync(
            "http://localhost:7777/controls/gmail/disconnect",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "wrong" }));
        Check("Gmail disconnect control rejects a bad token",
            forged.StatusCode == HttpStatusCode.Forbidden && disconnects == 0,
            $"{forged.StatusCode}, calls={disconnects}");

        using var wrongHost = new HttpRequestMessage(HttpMethod.Post, "http://localhost:7777/controls/gmail/disconnect")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
        };
        wrongHost.Headers.Host = "evil.test";
        var wrongHostResp = await noRedirect.SendAsync(wrongHost);
        Check("Gmail disconnect control rejects a foreign Host header",
            (int)wrongHostResp.StatusCode >= 400 && disconnects == 0,
            $"{wrongHostResp.StatusCode}, calls={disconnects}");

        var wrongContentType = await noRedirect.PostAsync(
            "http://localhost:7777/controls/gmail/disconnect",
            new StringContent($"token={Uri.EscapeDataString(token)}", Encoding.UTF8, "text/plain"));
        Check("Gmail disconnect control rejects non-form content",
            wrongContentType.StatusCode == HttpStatusCode.Forbidden && disconnects == 0,
            $"{wrongContentType.StatusCode}, calls={disconnects}");

        var post = await noRedirect.PostAsync(
            "http://localhost:7777/controls/gmail/disconnect",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        Check("Gmail disconnect control invokes the configured action",
            post.StatusCode == HttpStatusCode.SeeOther && disconnects == 1,
            $"{post.StatusCode}, calls={disconnects}");

        var forgedAuditExport = await noRedirect.PostAsync(
            "http://localhost:7777/controls/audit/export",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "wrong" }));
        Check("audit export control rejects a bad token",
            forgedAuditExport.StatusCode == HttpStatusCode.Forbidden && auditExports == 0,
            $"{forgedAuditExport.StatusCode}, calls={auditExports}");

        var wrongAuditExportContentType = await noRedirect.PostAsync(
            "http://localhost:7777/controls/audit/export",
            new StringContent($"token={Uri.EscapeDataString(token)}", Encoding.UTF8, "text/plain"));
        Check("audit export control rejects non-form content",
            wrongAuditExportContentType.StatusCode == HttpStatusCode.Forbidden && auditExports == 0,
            $"{wrongAuditExportContentType.StatusCode}, calls={auditExports}");

        var auditExportPost = await noRedirect.PostAsync(
            "http://localhost:7777/controls/audit/export",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        Check("audit export control invokes the configured action",
            auditExportPost.StatusCode == HttpStatusCode.SeeOther && auditExports == 1,
            $"{auditExportPost.StatusCode}, calls={auditExports}");

        var forgedExport = await noRedirect.PostAsync(
            "http://localhost:7777/controls/package/export",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "wrong" }));
        Check("alpha package export control rejects a bad token",
            forgedExport.StatusCode == HttpStatusCode.Forbidden && packageExports == 0,
            $"{forgedExport.StatusCode}, calls={packageExports}");

        var exportPost = await noRedirect.PostAsync(
            "http://localhost:7777/controls/package/export",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        Check("alpha package export control invokes the configured action",
            exportPost.StatusCode == HttpStatusCode.SeeOther && packageExports == 1,
            $"{exportPost.StatusCode}, calls={packageExports}");

        var forgedApp = await noRedirect.PostAsync(
            "http://localhost:7777/controls/application",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = "wrong",
                ["applicationId"] = applicationId.ToString(),
                ["action"] = "pause",
            }));
        Check("application control rejects a bad token",
            forgedApp.StatusCode == HttpStatusCode.Forbidden && appControls == 0,
            $"{forgedApp.StatusCode}, calls={appControls}");

        var wrongAppContentType = await noRedirect.PostAsync(
            "http://localhost:7777/controls/application",
            new StringContent(
                $"token={Uri.EscapeDataString(token)}&applicationId={applicationId}&action=pause",
                Encoding.UTF8,
                "text/plain"));
        Check("application control rejects non-form content",
            wrongAppContentType.StatusCode == HttpStatusCode.Forbidden && appControls == 0,
            $"{wrongAppContentType.StatusCode}, calls={appControls}");

        var appPost = await noRedirect.PostAsync(
            "http://localhost:7777/controls/application",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["applicationId"] = applicationId.ToString(),
                ["action"] = "pause",
            }));
        var controlled = await evidenceStore.GetApplicationAsync(applicationId);
        Check("application control invokes the configured action",
            appPost.StatusCode == HttpStatusCode.SeeOther &&
            appControls == 1 &&
            controlled?.State == AppState.PAUSED.ToString(),
            $"{appPost.StatusCode}, calls={appControls}, state={controlled?.State}");
    }
    else
    {
        // fall back to verifying the renderers directly (the listener binds at integration)
        using var doc = JsonDocument.Parse(dash.StatusJson());
        Check("/status JSON renders live counters (direct)", doc.RootElement.GetProperty("drafted").GetInt64() == 1);
        Check("HTML renderer exposes configured controls (direct)",
            doc.RootElement.GetProperty("gmailDisconnectAvailable").GetBoolean());
        Check("HTML renderer exposes configured application controls (direct)",
            doc.RootElement.GetProperty("applicationControlAvailable").GetBoolean());
        Check("HTML renderer exposes configured audit export controls (direct)",
            doc.RootElement.GetProperty("auditExportAvailable").GetBoolean());
        Check("HTML renderer exposes configured alpha package export controls (direct)",
            doc.RootElement.GetProperty("alphaPackageExportAvailable").GetBoolean());
        Check("HTML renderer exposes configured job evidence (direct)",
            doc.RootElement.GetProperty("jobsAvailable").GetBoolean());
        using var evidenceDoc = JsonDocument.Parse(await dash.EvidenceJsonAsync());
        Check("evidence renderer reports audit verification (direct)",
            evidenceDoc.RootElement.GetProperty("auditOk").GetBoolean());
    }
    await dash.DisposeAsync();
    try { if (Directory.Exists(artifactDir)) Directory.Delete(artifactDir, recursive: true); } catch (IOException) { }
}

{
    var now = DateTimeOffset.UtcNow.ToString("O");
    var rendererOnlyDashboard = new LocalDashboard(
        new EngineCounters(),
        7778,
        new LocalDashboardActions(
            ControlApplicationAsync: (_, _, _) => Task.FromResult(new DashboardControlResult(true, "ok"))),
        new LocalDashboardEvidence(_ => Task.FromResult(new DashboardEvidence(
            true,
            null,
            null,
            0,
            Array.Empty<DashboardEvidenceEvent>(),
            new[]
            {
                new ApplicationSummaryRow(101, AppState.REJECTED_BY_ENGINE.ToString(), "L1", "Email", now, now, null, 201, "Rejected sample", "Example", null, null, "Remote", "", null, null, null, null, null, null, null, null, false),
                new ApplicationSummaryRow(102, AppState.DRAFTED.ToString(), "L1", "Email", now, now, null, 202, "Drafted sample", "Example", null, null, "Remote", "", null, null, null, null, "SUCCEEDED", "draft-102", null, null, false),
            },
            new[]
            {
                new JobSummaryRow(303, "greenhouse", "draftable-303", "Draftable sample", "Example", null, "Remote", "Remote", "https://jobs.example/303", "https://apply.example/303", null, null, null, null, null, false, null, now, 0),
            }))));
    var renderedApplications = await rendererOnlyDashboard.ApplicationsHtmlAsync();
    var rejectedApplicationRow = HtmlRowContaining(renderedApplications, "REJECTED_BY_ENGINE");
    var draftedApplicationRow = HtmlRowContaining(renderedApplications, "DRAFTED");
    Check("/applications hides controls for terminal rows",
        rejectedApplicationRow.Contains("<td>-</td></tr>") &&
        !rejectedApplicationRow.Contains("action=\"/controls/application\"") &&
        draftedApplicationRow.Contains("action=\"/controls/application\""),
        renderedApplications);
    var renderedJobs = await rendererOnlyDashboard.JobsHtmlAsync();
    var draftableJobRow = HtmlRowContaining(renderedJobs, "Draftable sample");
    Check("/jobs exposes job id for selected-job drafting",
        draftableJobRow.Contains("<td class=\"n\">303</td>"),
        renderedJobs);
    await rendererOnlyDashboard.DisposeAsync();
}

// ── 4) gateway budget safety invariant ────────────────────────────────────────────────────────────
Console.WriteLine("\n[ audit export ]");
{
    var store = await SeededStoreAsync();
    await store.AppendEventAsync(new EventInput("engine", "export-test", "application", "1", "{\"secret\":\"local payload\"}"));

    var safe = await AuditExport.BuildJsonAsync(store);
    using var safeDoc = JsonDocument.Parse(safe);
    Check("audit export reports intact chain", safeDoc.RootElement.GetProperty("audit").GetProperty("ok").GetBoolean(), safe);
    Check("audit export omits payloads by default",
        !safeDoc.RootElement.GetProperty("payloadsIncluded").GetBoolean() &&
        !safe.Contains("local payload") &&
        safe.Contains("PayloadSha256"),
        safe);

    var full = await AuditExport.BuildJsonAsync(store, new AuditExportOptions(IncludePayloads: true));
    using var fullDoc = JsonDocument.Parse(full);
    Check("audit export can include payloads when explicitly requested",
        fullDoc.RootElement.GetProperty("payloadsIncluded").GetBoolean() &&
        full.Contains("local payload"),
        full);
}

Console.WriteLine("\n[ alpha package export ]");
{
    var root = Path.Combine(Path.GetTempPath(), "careerseeker-package-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "alpha.db");
        var artifacts = Path.Combine(root, "artifacts");
        var jds = Path.Combine(root, "job-descriptions");
        Directory.CreateDirectory(artifacts);
        Directory.CreateDirectory(jds);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "resume.pdf"), "%PDF test");
        await File.WriteAllTextAsync(Path.Combine(artifacts, "secret-token.dpapi"), "should not export");
        await File.WriteAllTextAsync(Path.Combine(jds, "posting.txt"), "posting body");

        var packagePath = Path.Combine(root, "package.zip");
        await using (var sqlite = SqliteSeekerStore.ForFile(dbPath))
        {
            await sqlite.InitializeAsync();
            await sqlite.AppendEventAsync(new EventInput("engine", "package-test", "application", "1", "{\"payload\":\"local\"}"));

            var result = await AlphaPackageExport.WriteAsync(
                sqlite,
                packagePath,
                new AlphaPackageOptions(dbPath, artifacts, jds));
            Check("alpha package export reports intact chain", result.AuditOk && result.EntryCount >= 5);
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var names = archive.Entries.Select(e => e.FullName).ToArray();
        Check("alpha package export writes audit database and artifact entries",
            names.Contains("manifest.json") &&
            names.Contains("audit.json") &&
            names.Any(n => n.StartsWith("database/", StringComparison.OrdinalIgnoreCase)) &&
            names.Contains("artifacts/resume.pdf") &&
            names.Contains("job-descriptions/posting.txt"),
            string.Join(", ", names));
        Check("alpha package export excludes secret-looking files",
            names.All(n =>
                !n.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                !n.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", names));

        var importRoot = Path.Combine(root, "imported");
        var imported = await AlphaPackageImport.ImportAsync(
            packagePath,
            new AlphaPackageImportOptions(
                Path.Combine(importRoot, "alpha.db"),
                Path.Combine(importRoot, "artifacts"),
                Path.Combine(importRoot, "job-descriptions")));
        Check("alpha package import restores database artifacts and job descriptions",
            imported.AuditOk &&
            File.Exists(Path.Combine(importRoot, "alpha.db")) &&
            File.Exists(Path.Combine(importRoot, "artifacts", "resume.pdf")) &&
            File.Exists(Path.Combine(importRoot, "job-descriptions", "posting.txt")));

        var overwriteRefused = false;
        try
        {
            await AlphaPackageImport.ImportAsync(
                packagePath,
                new AlphaPackageImportOptions(
                    Path.Combine(importRoot, "alpha.db"),
                    Path.Combine(importRoot, "artifacts"),
                    Path.Combine(importRoot, "job-descriptions")));
        }
        catch (IOException)
        {
            overwriteRefused = true;
        }
        Check("alpha package import preserves existing files by default", overwriteRefused);

        var unsafePackage = Path.Combine(root, "unsafe.zip");
        using (var stream = File.Create(unsafePackage))
        using (var unsafeZip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var escape = unsafeZip.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(escape.Open());
            writer.Write("bad");
        }

        var unsafeRejected = false;
        try
        {
            await AlphaPackageImport.ImportAsync(
                unsafePackage,
                new AlphaPackageImportOptions(
                    Path.Combine(root, "unsafe.db"),
                    Path.Combine(root, "unsafe-artifacts"),
                    Path.Combine(root, "unsafe-jds")));
        }
        catch (InvalidOperationException)
        {
            unsafeRejected = true;
        }
        Check("alpha package import rejects unsafe zip entries", unsafeRejected);

        var secretPackage = Path.Combine(root, "secret-entry.zip");
        using (var stream = File.Create(secretPackage))
        using (var secretZip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var secret = secretZip.CreateEntry("artifacts/token.txt");
            using var writer = new StreamWriter(secret.Open());
            writer.Write("should not import");
        }

        var secretRejected = false;
        try
        {
            await AlphaPackageImport.ImportAsync(
                secretPackage,
                new AlphaPackageImportOptions(
                    Path.Combine(root, "secret.db"),
                    Path.Combine(root, "secret-artifacts"),
                    Path.Combine(root, "secret-jds")));
        }
        catch (InvalidOperationException)
        {
            secretRejected = true;
        }
        Check("alpha package import rejects secret-looking zip entries", secretRejected);

        var noManifestPackage = Path.Combine(root, "no-manifest.zip");
        using (var stream = File.Create(noManifestPackage))
        using (var noManifestZip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var artifact = noManifestZip.CreateEntry("artifacts/resume.pdf");
            using var writer = new StreamWriter(artifact.Open());
            writer.Write("%PDF but not a CareerSeeker alpha package");
        }

        var noManifestRejected = false;
        try
        {
            await AlphaPackageImport.ImportAsync(
                noManifestPackage,
                new AlphaPackageImportOptions(
                    Path.Combine(root, "no-manifest.db"),
                    Path.Combine(root, "no-manifest-artifacts"),
                    Path.Combine(root, "no-manifest-jds")));
        }
        catch (InvalidOperationException)
        {
            noManifestRejected = true;
        }
        Check("alpha package import requires an alpha manifest", noManifestRejected);
    }
    finally
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}

Console.WriteLine("\n[ profile import ]");
{
    var root = Path.Combine(Path.GetTempPath(), "careerseeker-profile-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        var profilePath = Path.Combine(root, "profile.json");
        await File.WriteAllTextAsync(profilePath, """
        {
          "format": "careerseeker-alpha-profile-v1",
          "profile": {
            "name": "Riley Chen",
            "email": "riley@example.com",
            "headline": "Platform Engineer"
          },
          "claims": [
            {
              "kind": "Title",
              "text": "Platform Engineer",
              "confidence": "verified",
              "sourceDoc": "resume.pdf"
            },
            {
              "kind": "Skill",
              "text": "Kubernetes",
              "confidence": "stated",
              "sourceDoc": "resume.pdf"
            }
          ]
        }
        """);

        var dbPath = Path.Combine(root, "alpha.db");
        await using var sqlite = SqliteSeekerStore.ForFile(dbPath);
        await sqlite.InitializeAsync();
        var seededProfile = await SeedProfileAsync(sqlite);
        var seededClaims = await sqlite.GetClaimsAsync(seededProfile);
        Check("profile import test starts from seeded demo claims",
            seededClaims.Any(c => c.Text == "Acme"), string.Join(", ", seededClaims.Select(c => c.Text)));

        var imported = await AlphaProfileImport.ImportAsync(sqlite, profilePath, "alpha.profileId");
        var importedClaims = await sqlite.GetClaimsAsync(imported.ProfileId);
        Check("profile import replaces the profile claim oracle",
            imported.ClaimCount == 2 &&
            importedClaims.Count == 2 &&
            importedClaims.Any(c => c.Text == "Kubernetes" && c.Confidence == "stated") &&
            importedClaims.All(c => c.Text != "Acme"),
            string.Join(", ", importedClaims.Select(c => c.Text)));
        Check("profile import marks the active alpha profile",
            await sqlite.GetConfigAsync("alpha.profileId") == imported.ProfileId.ToString());

        var template = AlphaProfileImport.TemplateJson();
        using var templateDoc = JsonDocument.Parse(template);
        Check("profile template is parseable and contains editable claims",
            templateDoc.RootElement.GetProperty("claims").GetArrayLength() >= 3);
    }
    finally
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}

Console.WriteLine("\n[ startup doctor ]");
{
    var root = Path.Combine(Path.GetTempPath(), "careerseeker-doctor-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        var clientPath = Path.Combine(root, "client.json");
        await File.WriteAllTextAsync(clientPath, """
        {
          "installed": {
            "client_id": "client-123.apps.googleusercontent.com",
            "client_secret": "secret-abc"
          }
        }
        """);
        var envPath = Path.Combine(root, "env.secrets");
        await File.WriteAllTextAsync(envPath, """
        ANTHROPIC_API_KEY=fake-anthropic
        GEMINI_API_KEY=fake-gemini
        BRAVE_SEARCH_API=fake-brave
        """);

        var report = await StartupDoctor.RunAsync(new StartupDoctorOptions(
            DbPath: Path.Combine(root, "doctor.db"),
            ArtifactDirectory: Path.Combine(root, "artifacts"),
            OAuthClientPath: clientPath,
            GmailTokenVaultPath: Path.Combine(root, "missing-token.dpapi"),
            EnvFilePath: envPath,
            KeyVaultPath: Path.Combine(root, "missing-keys.dpapi")));
        Check("startup doctor passes optional Gmail/BYOK checks with usable local resources",
            report.Ok && report.Checks.Any(c => c.Name == "byok_providers" && c.Detail.Contains("anthropic")));
        Check("startup doctor reports optional Brave Search readiness",
            report.Checks.Any(c => c.Name == "brave_search" && c.Detail.Contains("BRAVE_SEARCH_API")));

        var strict = await StartupDoctor.RunAsync(new StartupDoctorOptions(
            DbPath: Path.Combine(root, "doctor-strict.db"),
            ArtifactDirectory: Path.Combine(root, "strict-artifacts"),
            OAuthClientPath: clientPath,
            GmailTokenVaultPath: Path.Combine(root, "missing-token.dpapi"),
            EnvFilePath: envPath,
            KeyVaultPath: Path.Combine(root, "missing-keys.dpapi"),
            RequireGmail: true,
            RequireByok: true));
        Check("startup doctor fails closed when required Gmail token is missing",
            !strict.Ok && strict.Checks.Any(c => c.Name == "gmail_token_vault" && !c.Ok));
    }
    finally
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}

Console.WriteLine("\n[ gateway safety ]");
{
    var meter = new BudgetMeter(0.001m);
    meter.Record(0.01m);
    Check("pinned verifier stage proceeds over cap",
        meter.Evaluate(Stage.VerifierEntailment) == ThrottleDecision.Proceed);
}

// ── 5) L1 dispatcher cannot submit ────────────────────────────────────────────────────────────────
Console.WriteLine("\n[ dispatcher safety ]");
{
    var dispatcher = MakeDispatcher(new FakeGmail());
    var threw = false;
    try
    {
        await dispatcher.SubmitAsync(
            new PipelineJob(1, "Senior Software Engineer", "Acme"),
            new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()));
    }
    catch (NotSupportedException)
    {
        threw = true;
    }
    Check("L1 SubmitAsync throws NotSupportedException", threw);
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

Dispatcher MakeDispatcher(FakeGmail g, string? artifactDirectory = null) => new(
    new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@feed.com")),
    new FakeRenderer(), g, new DispatcherConfig("Jordan Lee", "jordan@gmail.com", ArtifactDirectory: artifactDirectory));

string DashboardToken(string html)
{
    const string marker = "name=\"token\" value=\"";
    var start = html.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0) return "";
    start += marker.Length;
    var end = html.IndexOf('"', start);
    return end > start ? WebUtility.HtmlDecode(html[start..end]) : "";
}

async Task<CommandResult> RunEngineCommandAsync(params string[] engineArgs)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = Directory.GetCurrentDirectory(),
    };
    foreach (var arg in new[]
    {
        "run",
        "-c", "Release",
        "--no-build",
        "--project", "src/Engine/SeekerSvc.Engine.csproj",
        "--",
    })
    {
        psi.ArgumentList.Add(arg);
    }
    foreach (var arg in engineArgs)
        psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Engine command.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new CommandResult(process.ExitCode, (await stdout) + (await stderr));
}

sealed record CommandResult(int ExitCode, string Output);

sealed class FakeFeed : IJobFeed
{
    private readonly IReadOnlyList<JobPosting> _b;
    public FakeFeed(IReadOnlyList<JobPosting> b) => _b = b;
    public Task<IReadOnlyList<JobPosting>> DiscoverAsync(CancellationToken ct = default) => Task.FromResult(_b);
}
sealed class FakeSemantic : ISemanticScorer
{
    public Task<SemanticScores> ScoreAsync(JobPosting p, CancellationToken ct = default) => Task.FromResult(new SemanticScores(4.6, 4.2));
}
sealed class FakePostings : IPostingSource
{
    private readonly PostingDispatchInfo _i; public FakePostings(PostingDispatchInfo i) => _i = i;
    public Task<PostingDispatchInfo> GetDispatchInfoAsync(long jobId, CancellationToken ct = default) => Task.FromResult(_i);
}
sealed class FakeRenderer : IDocumentRenderer
{
    public Task<Attachment> RenderResumeAsync(PipelineJob j, TailoredApplication a, CancellationToken ct = default)
        => Task.FromResult(new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));
    public Task<Attachment?> RenderCoverAsync(PipelineJob j, TailoredApplication a, CancellationToken ct = default) => Task.FromResult<Attachment?>(null);
}
sealed class FakeGmail : IGmailDraftClient
{
    public int Drafts;
    public Task<string> CreateDraftAsync(string raw, IReadOnlyList<string> labelIds, CancellationToken ct = default) { Drafts++; return Task.FromResult("d" + Drafts); }
}
