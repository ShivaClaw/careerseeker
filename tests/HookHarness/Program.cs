using SeekerSvc.Gateway;
using SeekerSvc.Pipeline;
using SeekerSvc.Researcher;
using SeekerSvc.Tailor;
using SeekerSvc.Verifier;

int passed = 0, failed = 0;
void Check(string n, bool c, string? d = null)
{ if (c) { passed++; Console.WriteLine($"  PASS  {n}"); } else { failed++; Console.WriteLine($"  FAIL  {n}{(d is null ? "" : $"  -- {d}")}"); } }

Console.WriteLine("=== CareerSeeker Tailor hook seam ===\n");

// ── HookGuard: qualitative hooks pass; anything the Decomposer would read as a candidate claim is rejected ──
Console.WriteLine("[ HookGuard ]");
Check("qualitative hook is safe", HookGuard.IsSafe("Acme recently expanded its developer-tools platform team."));
Check("money figure rejected", !HookGuard.IsSafe("Acme raised a $40M Series B."));
Check("percentage rejected", !HookGuard.IsSafe("Acme grew revenue 200% last year."));
Check("tenure rejected", !HookGuard.IsSafe("Acme has 10 years in market."));
Check("credential cue rejected", !HookGuard.IsSafe("Acme's platform is AWS certified."));
Check("null/empty rejected", !HookGuard.IsSafe(null) && !HookGuard.IsSafe("   "));

// ── hook reaches the model prompt when safe, and is omitted when absent ──────────────────────────────
Console.WriteLine("\n[ prompt wiring ]");
{
    var cap = new CapturingProvider();
    var gw = new LlmGateway(RoutingTable.Default(), GatewayMode.Managed, new BudgetMeter(100m),
        new ILlmProvider[] { cap, new FakeProvider("google"), new FakeProvider("local", true) });
    var model = new GatewayTailorModel(gw);
    var job = new PipelineJob(1, "Senior Engineer", "Acme");
    var profile = new List<SourceClaim> { new("c1", ClaimKind.Skill, "distributed systems", Confidence.Verified) };

    await model.GenerateAsync(new TailorModelRequest(job, profile, new List<string>(), StyleCard.Default, new List<string>(),
        CompanyHook: "Acme recently expanded its developer-tools platform team."));
    Check("safe hook appears in the model prompt", cap.Last.Contains("VERIFIED COMPANY CONTEXT") && cap.Last.Contains("platform team"));

    await model.GenerateAsync(new TailorModelRequest(job, profile, new List<string>(), StyleCard.Default, new List<string>(), CompanyHook: null));
    Check("no hook section when none supplied", !cap.Last.Contains("VERIFIED COMPANY CONTEXT"));
}

// ── end-to-end via the dossier bridge: grounded qualitative hook flows; quantified hook is dropped ──────
Console.WriteLine("\n[ profile minimization ]");
{
    var cap = new CapturingProvider();
    var gw = new LlmGateway(RoutingTable.Default(), GatewayMode.Managed, new BudgetMeter(100m),
        new ILlmProvider[] { cap, new FakeProvider("google"), new FakeProvider("local", true) });
    var tailor = new Tailor(new GatewayTailorModel(gw));
    var job = new PipelineJob(1, "Platform Engineer", "Acme", DescriptionText: "Build distributed systems in Go.");
    var profile = new List<SourceClaim>
    {
        new("c1", ClaimKind.Skill, "distributed systems", Confidence.Verified),
        new("c2", ClaimKind.Other, "managed payroll operations", Confidence.Verified),
    };

    await tailor.TailorAsync(job, profile, Array.Empty<Violation>());
    Check("Tailor prompt keeps posting-relevant profile facts",
        cap.Last.Contains("distributed systems") && !cap.Last.Contains("managed payroll operations"),
        cap.Last);

    var prior = new[]
    {
        new Violation(
            new TailoredClaim(ClaimKind.Other, "handled payroll migrations"),
            ViolationKind.NoSupportingClaim,
            "Unsupported.",
            "managed payroll operations")
    };
    await tailor.TailorAsync(job, profile, prior);
    Check("Tailor rework preserves nearest supported fact even when not posting-relevant",
        cap.Last.Contains("managed payroll operations"),
        cap.Last);
}

Console.WriteLine("\n[ dossier bridge end-to-end ]");
async Task<string> RunWithDossierHook(string hookText, string docText)
{
    var docs = new List<ResearchDoc> { new("https://acme.com/news", "Acme news", docText) };
    var web = new FakeWeb(docs);
    var dmodel = new FakeDossierModel(new List<ProposedFact> { new(DossierTopic.Hook, hookText, "https://acme.com/news") });
    var researcher = new SeekerSvc.Researcher.Researcher(web, dmodel, new InMemoryDossierStore());

    var cap = new CapturingProvider();
    var gw = new LlmGateway(RoutingTable.Default(), GatewayMode.Managed, new BudgetMeter(100m),
        new ILlmProvider[] { cap, new FakeProvider("google"), new FakeProvider("local", true) });
    var tailor = new Tailor(new GatewayTailorModel(gw), hooks: new DossierHookProvider(researcher));

    var job = new PipelineJob(1, "Senior Engineer", "Acme");
    var profile = new List<SourceClaim> { new("c1", ClaimKind.Skill, "distributed systems", Confidence.Verified) };
    await tailor.TailorAsync(job, profile, Array.Empty<Violation>());
    return cap.Last;
}
{
    var qualitative = "Acme builds developer tools for distributed systems.";
    var prompt = await RunWithDossierHook(qualitative, "Acme builds developer tools for distributed systems and observability.");
    Check("grounded qualitative hook flows through the bridge into the prompt", prompt.Contains("VERIFIED COMPANY CONTEXT") && prompt.Contains("developer tools"));

    var quantified = "Acme raised a $40M Series B.";
    var prompt2 = await RunWithDossierHook(quantified, "Acme raised a $40M Series B to grow the team.");
    Check("grounded but QUANTIFIED hook is dropped by HookGuard (no false Gate block)", !prompt2.Contains("VERIFIED COMPANY CONTEXT"));
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

sealed class CapturingProvider : ILlmProvider
{
    public string Last = "";
    public string Name => "anthropic";
    public bool IsLocal => false;
    public Task<ProviderResult> CompleteAsync(ProviderCall call, CancellationToken ct = default)
    {
        Last = string.Join("\n", call.Messages.Select(m => m.Content));
        return Task.FromResult(new ProviderResult("{\"resume\":\"R\",\"cover\":\"C\",\"claims\":[],\"answers\":{}}", new LlmUsage(10, 10)));
    }
}
sealed class FakeWeb : IWebResearch
{
    private readonly IReadOnlyList<ResearchDoc> _d; public FakeWeb(IReadOnlyList<ResearchDoc> d) => _d = d;
    public Task<IReadOnlyList<ResearchDoc>> SearchAsync(string q, int max = 5, CancellationToken ct = default) => Task.FromResult(_d);
}
sealed class FakeDossierModel : IDossierModel
{
    private readonly IReadOnlyList<ProposedFact> _f; public FakeDossierModel(IReadOnlyList<ProposedFact> f) => _f = f;
    public Task<IReadOnlyList<ProposedFact>> ProposeAsync(CompanyRef c, IReadOnlyList<ResearchDoc> d, CancellationToken ct = default) => Task.FromResult(_f);
}
