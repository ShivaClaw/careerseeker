using SeekerSvc.Dispatcher;
using SeekerSvc.Scout;
using SeekerSvc.Gateway;
using SeekerSvc.Pipeline;
using SeekerSvc.Scorer;
using SeekerSvc.Store;
using SeekerSvc.Tailor;
using SeekerSvc.Verifier;

int passed = 0, failed = 0;
void Check(string n, bool c, string? d = null)
{ if (c) { passed++; Console.WriteLine($"  PASS  {n}"); } else { failed++; Console.WriteLine($"  FAIL  {n}{(d is null ? "" : $"  -- {d}")}"); } }

// ── canned tailoring outputs (the FakeProvider returns these as the "model") ──────────────────────
const string cleanJson =
    "{\"resume\":\"Senior Software Engineer experienced in distributed systems and Go.\"," +
    "\"cover\":\"I am excited to apply. I have built reliable distributed systems in Go and would bring that experience to your team.\"," +
    "\"claims\":[],\"answers\":{}}";
const string fabricatedJson =
    "{\"resume\":\"Senior Software Engineer.\"," +
    "\"cover\":\"In my last role I personally increased company revenue by 200% in a single quarter.\"," +
    "\"claims\":[{\"kind\":\"Metric\",\"text\":\"increased revenue 200%\",\"number\":200,\"unit\":\"%\"}],\"answers\":{}}";
const string outageJson =
    "{\"resume\":\"Senior Software Engineer.\"," +
    "\"cover\":\"I drove platform leadership across the organization.\"," +
    "\"claims\":[],\"answers\":{}}";
const string metricJson =
    "{\"resume\":\"Reduced p99 latency 30%.\"," +
    "\"cover\":\"I have built reliable distributed systems in Go.\"," +
    "\"claims\":[{\"kind\":\"Metric\",\"text\":\"reduced p99 latency 30%\",\"number\":30,\"unit\":\"%\"}],\"answers\":{}}";

// ── shared builders ───────────────────────────────────────────────────────────────────────────────
LlmGateway GatewayReturning(string canned) => new(
    RoutingTable.Default(), GatewayMode.Managed, new BudgetMeter(100m),
    new ILlmProvider[] { new FakeProvider("anthropic", respond: _ => canned), new FakeProvider("google"), new FakeProvider("local", true) });

ITailor TailorReturning(string canned) => new Tailor(new GatewayTailorModel(GatewayReturning(canned)));

async Task<InMemorySeekerStore> SeededStoreAsync()
{
    var store = new InMemorySeekerStore();
    var profileId = await store.UpsertProfileAsync("{}");
    // Source-of-Truth profile: the Gate's oracle. A superset of what the clean draft will say.
    string[,] claims =
    {
        { "Title", "Senior Software Engineer" },
        { "Skill", "distributed systems" },
        { "Skill", "Go" },
        { "Skill", "reliable" },
        { "Skill", "experience" },
        { "Skill", "team" },
        { "Employer", "Acme" },
        { "Metric", "reduced p99 latency 30%" },
        { "Other", "Senior Software Engineer experienced in distributed systems and Go" },
        { "Other", "I have built reliable distributed systems in Go and would bring that experience to your team" },
    };
    for (var i = 0; i < claims.GetLength(0); i++)
        await store.AddClaimAsync(new ClaimRow($"c{i}", profileId, claims[i, 0], claims[i, 1], "Verified"));
    return store;
}

Dispatcher MakeDispatcher(FakeGmail gmail) => new(
    new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@acme.com")),
    new FakeRenderer(), gmail, new DispatcherConfig("Jordan Lee", "jordan@gmail.com"));

JobPosting HealthyPosting() => new()
{
    Title = "Senior Software Engineer",
    TitleCanon = "senior software engineer",
    Location = "Remote",
    Remote = RemoteMode.Remote,
    Compensation = new Compensation(170000m, 210000m, "USD", CompInterval.Year, CompSource.Structured),
    DescriptionText = new string('x', 50) + " We are hiring a senior engineer to build distributed systems in Go. " +
        "You will own services end to end, mentor peers, and improve reliability. Clear team, clear mission, real comp.",
    RepostCount = 0,
    FirstPublished = DateTimeOffset.UtcNow.AddDays(-3),
    DescriptionLikelyInjected = false,
    RecruiterIdentifiable = true,
    CompanyDomainVerified = true,
};

JobPosting ScamPosting() => new()
{
    Title = "URGENT WORK FROM HOME",
    TitleCanon = "data entry",
    Location = null,
    Remote = RemoteMode.Remote,
    Compensation = null,                       // no comp transparency
    DescriptionText = "Earn $$$ fast. Wire transfer required. Send SSN to start immediately. No experience.",
    RepostCount = 9,                           // reposted endlessly
    FirstPublished = DateTimeOffset.UtcNow.AddDays(-200),
    DescriptionLikelyInjected = true,          // tripped injection heuristics
    RecruiterIdentifiable = false,
    CompanyDomainVerified = false,
};

var prefs = new UserPreferences
{
    Comp = new CompTarget(150000m, 180000m, 220000m),
    Remote = RemoteStance.Any,
    Seniority = SeniorityBand.Senior,
};

Dispatch DecideFor(JobPosting p, SemanticScores sem) => Scorer.Score(p, prefs, sem).Dispatch;

async Task<long> StoreJobAsync(InMemorySeekerStore store, JobPosting p)
{
    var companyId = await store.UpsertCompanyAsync(new CompanyUpsert("greenhouse", "acme", "Acme"));
    var jr = await store.UpsertJobAsync(companyId, new JobUpsert(
        Source: "greenhouse", ExternalId: "g-1", Url: "https://boards.greenhouse.io/acme/jobs/1",
        Title: p.Title, TitleCanon: p.TitleCanon, DedupKey: "acme|" + p.TitleCanon, Remote: p.Remote.ToString(),
        SimHash: 0L, FirstSeen: DateTimeOffset.UtcNow.ToString("o"), ApplyUrl: "mailto:jobs@acme.com",
        Location: p.Location, CompMin: p.Compensation?.Min, CompMax: p.Compensation?.Max));
    return jr.JobId;
}

Console.WriteLine("=== CareerSeeker L1 vertical slice (Scout→Store→Scorer→Pipeline→Tailor→Gate→Dispatcher) ===\n");

{
    var undeclared = Decomposer.FromDraft(new TailorDraft(
        "I served as CTO at Google.", "", Array.Empty<DeclaredClaim>(), new Dictionary<string, string>()));
    Check("undeclared title/employer prose becomes a Gate atom",
        undeclared.Any(c => c.Kind == ClaimKind.Other && c.Text.Contains("CTO at Google")));
    var verdict = await FabricationGate.VerifyAsync(Array.Empty<SourceClaim>(), undeclared, new DefaultSemanticMatcher());
    Check("undeclared title/employer prose is blocked without profile support", !verdict.Passed);
}
{
    var courtesyOnly = Decomposer.FromDraft(new TailorDraft(
        "", "I am excited to apply.", Array.Empty<DeclaredClaim>(), new Dictionary<string, string>()));
    Check("pure courtesy sentence stays outside Gate atoms", courtesyOnly.Count == 0, courtesyOnly.Count.ToString());

    var prefixed = Decomposer.FromDraft(new TailorDraft(
        "", "I'm excited to bring the perspective I gained as Google's CTO.", Array.Empty<DeclaredClaim>(), new Dictionary<string, string>()));
    Check("courtesy-prefixed factual prose still becomes a Gate atom",
        prefixed.Any(c => c.Kind == ClaimKind.Other && c.Text.Contains("Google's CTO")));
    var verdict = await FabricationGate.VerifyAsync(Array.Empty<SourceClaim>(), prefixed, new DefaultSemanticMatcher());
    Check("courtesy-prefixed unsupported prose is blocked", !verdict.Passed && verdict.Verdict == Verdict.BlockedFabrication, verdict.Verdict.ToString());
}
{
    var unavailable = await FabricationGate.VerifyAsync(
        new[] { new SourceClaim("s1", ClaimKind.Other, "led platform engineering", Confidence.Verified) },
        new[] { new TailoredClaim(ClaimKind.Other, "drove platform leadership", "drove platform leadership") },
        new UnavailableSemanticMatcher());
    Check("semantic outage defers instead of blocking fabrication",
        unavailable.Verdict == Verdict.DeferredUnavailable && unavailable.UnavailableClaims == 1,
        $"{unavailable.Verdict} unavailable={unavailable.UnavailableClaims}");
}
{
    var matcher = new CountingSemanticMatcher((source, _) => source.Contains("reliable distributed systems", StringComparison.OrdinalIgnoreCase));
    var verdict = await FabricationGate.VerifyAsync(
        new[]
        {
            new SourceClaim("s1", ClaimKind.Other, "payment processing work", Confidence.Verified),
            new SourceClaim("s2", ClaimKind.Other, "built reliable distributed systems in Go", Confidence.Verified),
            new SourceClaim("s3", ClaimKind.Other, "customer support playbooks", Confidence.Verified),
        },
        new[] { new TailoredClaim(ClaimKind.Other, "built resilient services with Go", "built resilient services with Go") },
        matcher,
        options: GateVerificationOptions.BoundedSemantic(1));
    Check("bounded Gate candidates still allow relevant semantic support", verdict.Passed, verdict.Verdict.ToString());
    Check("bounded Gate candidates avoid irrelevant semantic calls", matcher.Calls == 1, matcher.Calls.ToString());
}
{
    var matcher = new CountingSemanticMatcher((source, _) => source.Contains("reliable distributed systems", StringComparison.OrdinalIgnoreCase));
    var verdict = await FabricationGate.VerifyAsync(
        new[]
        {
            new SourceClaim("s1", ClaimKind.Other, "payment processing work", Confidence.Verified),
            new SourceClaim("s2", ClaimKind.Other, "customer support playbooks", Confidence.Verified),
            new SourceClaim("s3", ClaimKind.Other, "built reliable distributed systems in Go", Confidence.Verified),
        },
        new[] { new TailoredClaim(ClaimKind.Other, "shipped resilient services with Go", "shipped resilient services with Go") },
        matcher);
    Check("default Gate semantic candidates remain exhaustive", verdict.Passed && matcher.Calls == 3,
        $"passed={verdict.Passed} calls={matcher.Calls}");
}
{
    var smokeProfile = new[]
    {
        new SourceClaim("title", ClaimKind.Title, "Senior Software Engineer", Confidence.Verified),
        new SourceClaim("skill-dist", ClaimKind.Skill, "distributed systems", Confidence.Verified),
        new SourceClaim("skill-go", ClaimKind.Skill, "Go", Confidence.Verified),
        new SourceClaim("skill-reliable", ClaimKind.Skill, "reliable", Confidence.Verified),
        new SourceClaim("skill-experience", ClaimKind.Skill, "experience", Confidence.Verified),
        new SourceClaim("skill-team", ClaimKind.Skill, "team", Confidence.Verified),
        new SourceClaim("summary", ClaimKind.Other, "Senior Software Engineer experienced in distributed systems and Go", Confidence.Verified),
        new SourceClaim("cover", ClaimKind.Other, "I have built reliable distributed systems in Go and would bring that experience to your team", Confidence.Verified),
    };
    var smokeDraft = Decomposer.FromDraft(new TailorDraft(
        "Senior Software Engineer experienced in distributed systems and Go.",
        "I have built reliable distributed systems in Go and would bring that experience to your team.",
        Array.Empty<DeclaredClaim>(),
        new Dictionary<string, string>()));
    var verdict = await FabricationGate.VerifyAsync(
        smokeProfile,
        smokeDraft,
        new DefaultSemanticMatcher(),
        options: GateVerificationOptions.BoundedSemantic(3));
    Check("bounded Gate accepts exact alpha smoke draft", verdict.Passed,
        string.Join(" | ", verdict.Violations.Select(v => v.Claim.Kind + ":" + v.Claim.Text + " nearest=" + v.NearestSource)));
}
{
    var matcher = new CountingSemanticMatcher((_, _) => true);
    var verdict = await FabricationGate.VerifyAsync(
        new[] { new SourceClaim("s1", ClaimKind.Other, "payment processing work", Confidence.Verified) },
        new[] { new TailoredClaim(ClaimKind.Other, "managed Kubernetes clusters", "managed Kubernetes clusters") },
        matcher,
        options: GateVerificationOptions.BoundedSemantic(2));
    Check("bounded Gate blocks no-overlap unsupported claims without semantic calls",
        !verdict.Passed && matcher.Calls == 0,
        $"passed={verdict.Passed} calls={matcher.Calls}");
}

// ── 1) HAPPY PATH: a clean, supported application flows all the way to a Gmail draft ───────────────
Console.WriteLine("[ happy path -> DRAFTED ]");
{
    var store = await SeededStoreAsync();
    var p = HealthyPosting();
    var jobId = await StoreJobAsync(store, p);
    var job = new PipelineJob(jobId, p.Title, "Acme", "mailto:jobs@acme.com");

    var sem = new SemanticScores(CvMatch: 4.6, GrowthSignal: 4.2);
    var decision = DecideFor(p, sem);
    Check("Scorer says Act on the healthy job", decision == Dispatch.Act, decision.ToString());

    var gmail = new FakeGmail();
    var pipeline = new ApplicationPipeline(store, TailorReturning(cleanJson), MakeDispatcher(gmail),
        matcher: new DefaultSemanticMatcher(), options: new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });

    var result = await pipeline.AdmitAsync(job, AutonomyLevel.L1, decision);
    Check("Gate passed", result.Gate?.Passed == true, $"violations={result.Gate?.Violations.Count}");
    Check("final state DRAFTED", result.FinalState == AppState.DRAFTED, result.FinalState.ToString());
    Check("dispatch outcome Ok via Email channel", result.Dispatch is { Ok: true, Channel: DispatchChannel.Email });
    Check("a Gmail draft was created", gmail.Drafts == 1);
    Check("audit chain intact", (await store.VerifyAuditAsync()).Ok);
}
{
    var store = await SeededStoreAsync();
    var p = HealthyPosting();
    var jobId = await StoreJobAsync(store, p);
    var job = new PipelineJob(jobId, p.Title, "Acme", "mailto:jobs@acme.com");
    var gmail = new FakeGmail();
    var pipeline = new ApplicationPipeline(store, TailorReturning(metricJson), MakeDispatcher(gmail),
        matcher: new DefaultSemanticMatcher(), options: new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });

    var result = await pipeline.AdmitAsync(job, AutonomyLevel.L1, Dispatch.Act);
    Check("stored text metric claim supports exact quantified draft", result.FinalState == AppState.DRAFTED,
        result.Gate?.Violations.FirstOrDefault()?.Explanation ?? result.FinalState.ToString());
}

// ── 2) FABRICATION: an unsupported metric is caught; nothing is drafted ────────────────────────────
Console.WriteLine("\n[ draft failure leaves READY ]");
{
    var store = await SeededStoreAsync();
    var p = HealthyPosting();
    var jobId = await StoreJobAsync(store, p);
    var pipeline = new ApplicationPipeline(store, TailorReturning(cleanJson), new FailingDraftDispatcher(),
        new DefaultSemanticMatcher(), new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });
    try
    {
        await pipeline.AdmitAsync(new PipelineJob(jobId, p.Title, "Acme"), AutonomyLevel.L1,
            DecideFor(p, new SemanticScores(4.6, 4.2)));
    }
    catch (InvalidOperationException) { }

    var app = await store.GetApplicationAsync(1);
    Check("failed draft leaves lifecycle at READY", app?.State == AppState.READY.ToString(), app?.State);
}

Console.WriteLine("\n[ fabrication -> BLOCKED, no draft ]");
{
    var store = await SeededStoreAsync();
    var p = HealthyPosting();
    var jobId = await StoreJobAsync(store, p);
    var job = new PipelineJob(jobId, p.Title, "Acme", "mailto:jobs@acme.com");
    var decision = DecideFor(p, new SemanticScores(4.6, 4.2));

    var gmail = new FakeGmail();
    var pipeline = new ApplicationPipeline(store, TailorReturning(fabricatedJson), MakeDispatcher(gmail),
        matcher: new DefaultSemanticMatcher(), options: new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });

    var result = await pipeline.AdmitAsync(job, AutonomyLevel.L1, decision);
    Check("Gate did NOT pass on fabricated metric", result.Gate?.Passed == false);
    Check("final state BLOCKED_FABRICATION (escalated, not shipped)", result.FinalState == AppState.BLOCKED_FABRICATION, result.FinalState.ToString());
    Check("NO Gmail draft created for fabricated content", gmail.Drafts == 0);
}

// ── 3) SCAM FLOOR: a low-legitimacy posting never reaches Tailor/Dispatcher ────────────────────────
Console.WriteLine("\n[ matcher outage -> GATE_UNAVAILABLE, no draft ]");
{
    var store = await SeededStoreAsync();
    var p = HealthyPosting();
    var jobId = await StoreJobAsync(store, p);
    var job = new PipelineJob(jobId, p.Title, "Acme", "mailto:jobs@acme.com");
    var decision = DecideFor(p, new SemanticScores(4.6, 4.2));

    var gmail = new FakeGmail();
    var pipeline = new ApplicationPipeline(store, TailorReturning(outageJson), MakeDispatcher(gmail),
        matcher: new UnavailableSemanticMatcher(), options: new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });

    var result = await pipeline.AdmitAsync(job, AutonomyLevel.L1, decision);
    Check("Gate verdict is DeferredUnavailable", result.Gate?.Verdict == Verdict.DeferredUnavailable, result.Gate?.Verdict.ToString());
    Check("final state GATE_UNAVAILABLE", result.FinalState == AppState.GATE_UNAVAILABLE, result.FinalState.ToString());
    Check("NO Gmail draft created while matcher is unavailable", gmail.Drafts == 0);
}

Console.WriteLine("\n[ scam floor -> REJECTED_BY_ENGINE ]");
{
    var store = await SeededStoreAsync();
    var p = ScamPosting();
    var jobId = await StoreJobAsync(store, p);
    var job = new PipelineJob(jobId, p.Title, "Acme", "mailto:jobs@acme.com");

    var score = Scorer.Score(p, prefs, new SemanticScores(1.0, 1.0));
    Check("Scorer refuses to Act on the scam (legitimacy floor)", score.Dispatch != Dispatch.Act, $"{score.Dispatch} legit={score.Legitimacy:0.0}");

    var gmail = new FakeGmail();
    var pipeline = new ApplicationPipeline(store, TailorReturning(cleanJson), MakeDispatcher(gmail),
        matcher: new DefaultSemanticMatcher(), options: new PipelineOptions { ProfileId = 1, Channel = DispatchChannel.Email });

    var result = await pipeline.AdmitAsync(job, AutonomyLevel.L1, score.Dispatch);
    Check("final state REJECTED_BY_ENGINE", result.FinalState == AppState.REJECTED_BY_ENGINE, result.FinalState.ToString());
    Check("never tailored, never drafted", gmail.Drafts == 0);
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

// ── fakes ───────────────────────────────────────────────────────────────────────────────────────
sealed class FailingDraftDispatcher : IDispatcher
{
    public Task<DispatchOutcome> CreateDraftAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default) =>
        throw new InvalidOperationException("simulated Gmail draft failure");

    public Task<DispatchOutcome> SubmitAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

sealed class FakePostings : IPostingSource
{
    private readonly PostingDispatchInfo _i;
    public FakePostings(PostingDispatchInfo i) => _i = i;
    public Task<PostingDispatchInfo> GetDispatchInfoAsync(long jobId, CancellationToken ct = default) => Task.FromResult(_i);
}
sealed class FakeRenderer : IDocumentRenderer
{
    public Task<Attachment> RenderResumeAsync(PipelineJob j, TailoredApplication a, CancellationToken ct = default)
        => Task.FromResult(new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));
    public Task<Attachment?> RenderCoverAsync(PipelineJob j, TailoredApplication a, CancellationToken ct = default)
        => Task.FromResult<Attachment?>(null);
}
sealed class FakeGmail : IGmailDraftClient
{
    public int Drafts;
    public Task<string> CreateDraftAsync(string raw, IReadOnlyList<string> labelIds, CancellationToken ct = default)
    { Drafts++; return Task.FromResult("draft_" + Drafts); }
}
sealed class UnavailableSemanticMatcher : ISemanticMatcher
{
    public Task<SemanticMatchResult> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default) =>
        Task.FromResult(SemanticMatchResult.Deferred("provider_unavailable"));
}

sealed class CountingSemanticMatcher : ISemanticMatcher
{
    private readonly Func<string, string, bool> _entails;

    public CountingSemanticMatcher(Func<string, string, bool> entails) => _entails = entails;

    public int Calls { get; private set; }

    public Task<SemanticMatchResult> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(_entails(sourceText, tailoredText)
            ? SemanticMatchResult.Supported()
            : SemanticMatchResult.Unsupported());
    }
}
