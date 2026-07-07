using SeekerSvc.Gateway;
using SeekerSvc.Researcher;
using SeekerSvc.Scorer;
using SeekerSvc.Scout;

int passed = 0, failed = 0;
void Check(string n, bool c, string? d = null)
{ if (c) { passed++; Console.WriteLine($"  PASS  {n}"); } else { failed++; Console.WriteLine($"  FAIL  {n}{(d is null ? "" : $"  -- {d}")}"); } }

// retrieved docs (fakes). One is the company's own domain; one mentions a recruiter.
var docs = new List<ResearchDoc>
{
    new("https://acme.com/about", "About Acme", "Acme builds developer tools for distributed systems and observability."),
    new("https://techcrunch.com/acme-series-b", "Acme raises Series B", "Acme announced a forty million dollar Series B to expand its platform team."),
    new("https://acme.com/careers", "Careers at Acme", "Our recruiting team is hiring engineers. Contact careers@acme.com to apply."),
};
var company = new CompanyRef("Acme", "acme.com");

Console.WriteLine("=== CareerSeeker Researcher / dossier ===\n");

// ── grounding filter (the safety invariant) ───────────────────────────────────────────────────────
Console.WriteLine("[ grounding -- the safety invariant ]");
{
    var proposed = new List<ProposedFact>
    {
        new(DossierTopic.Overview, "Acme builds developer tools for distributed systems.", "https://acme.com/about"),       // grounded
        new(DossierTopic.Hook, "Acme recently raised a Series B to expand its platform team.", "https://techcrunch.com/acme-series-b"), // grounded
        new(DossierTopic.Hook, "Acme is about to IPO at a fifty billion valuation.", "https://techcrunch.com/acme-series-b"), // hallucinated: not supported by that doc
        new(DossierTopic.Signal, "Acme partners with NASA.", "https://forbes.com/made-up"),                                  // cited URL never retrieved
        new(DossierTopic.Risk, "Layoffs loom.", ""),                                                                          // no source
    };
    var r = GroundingFilter.Apply(proposed, docs);
    Check("grounded facts kept (overview + real hook)", r.Grounded.Count == 2, r.Grounded.Count.ToString());
    Check("ungrounded facts dropped (hallucination + bad url + no source)", r.Dropped == 3, r.Dropped.ToString());
    Check("the surviving hook is the source-backed one", r.Grounded.Any(f => f.Topic == DossierTopic.Hook && f.Text.Contains("Series B")));
    Check("no fact survives without a real source url", r.Grounded.All(f => docs.Any(d => d.Url == f.SourceUrl)));
}

// ── signals derivation ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n[ researched signals ]");
{
    var s = Signals.Derive(company, docs);
    Check("domain verified (own-domain doc retrieved)", s.CompanyDomainVerified == true);
    Check("recruiter identifiable (careers contact found)", s.RecruiterIdentifiable == true);

    var s2 = Signals.Derive(new CompanyRef("Ghost", "ghost.io"), new List<ResearchDoc>
        { new("https://random.net/x", "x", "nothing useful here") });
    Check("domain null when not retrieved (unknown, not false)", s2.CompanyDomainVerified is null);
    Check("recruiter null when none found", s2.RecruiterIdentifiable is null);
}

// ── orchestrator + caching (fakes) ──────────────────────────────────────────────────────────────────
Console.WriteLine("\n[ orchestrator + cache ]");
{
    var web = new FakeWeb(docs);
    var model = new FakeModel(new List<ProposedFact>
    {
        new(DossierTopic.Overview, "Acme builds developer tools for distributed systems.", "https://acme.com/about"),
        new(DossierTopic.Hook, "Acme recently raised a Series B to expand its platform team.", "https://techcrunch.com/acme-series-b"),
        new(DossierTopic.Risk, "totally invented risk", "https://nowhere.example/none"),  // dropped
    });
    var cache = new InMemoryDossierStore();
    var researcher = new Researcher(web, model, cache, new ResearcherOptions(TimeSpan.FromDays(14)));

    var dossier = await researcher.BuildAsync(company);
    Check("dossier built with only grounded facts", dossier.Facts.Count == 2 && researcher.LastDroppedUngrounded == 1);
    Check("best hook is grounded", dossier.BestHook?.Text.Contains("Series B") == true);
    Check("signals attached", dossier.Signals.CompanyDomainVerified == true);
    Check("content hash present", dossier.ContentHash.Length == 64);

    var webCalls1 = web.Calls; var modelCalls1 = model.Calls;
    var again = await researcher.BuildAsync(company);                     // within TTL -> cached
    Check("second build served from cache (no new web/model calls)", web.Calls == webCalls1 && model.Calls == modelCalls1);
    Check("cached dossier identical (same hash)", again.ContentHash == dossier.ContentHash);

    await researcher.BuildAsync(company, forceRefresh: true);             // bypass cache
    Check("forceRefresh re-runs research", web.Calls > webCalls1 && model.Calls > modelCalls1);
}

// ── bridge: GatewayDossierModel over the LLM Gateway (Stage.FullEvaluation) ─────────────────────────
Console.WriteLine("\n[ gateway bridge ]");
{
    Check("Parse handles JSON array", GatewayDossierModel.Parse("[{\"topic\":\"Hook\",\"text\":\"t\",\"sourceUrl\":\"u\"}]").Count == 1);
    Check("Parse strips fences", GatewayDossierModel.Parse("```json\n[{\"topic\":\"Overview\",\"text\":\"t\",\"sourceUrl\":\"u\"}]\n```").Count == 1);
    Check("Parse tolerates junk (no throw, empty)", GatewayDossierModel.Parse("not json").Count == 0);

    const string canned = "[{\"topic\":\"Hook\",\"text\":\"Acme recently raised a Series B to expand its platform team.\",\"sourceUrl\":\"https://techcrunch.com/acme-series-b\",\"sourceTitle\":\"Acme raises Series B\"}]";
    var gw = new LlmGateway(RoutingTable.Default(), GatewayMode.Managed, new BudgetMeter(100m),
        new ILlmProvider[] { new FakeProvider("anthropic"), new FakeProvider("google", respond: _ => canned), new FakeProvider("local", true) });
    var researcher = new Researcher(new FakeWeb(docs), new GatewayDossierModel(gw), new InMemoryDossierStore());
    var dossier = await researcher.BuildAsync(company);
    Check("end-to-end via Gateway: grounded hook in dossier", dossier.Hooks.Any(h => h.Text.Contains("Series B")));
    Check("FullEvaluation stage billed (mid cloud)", gw.Accounting.ByStage.ContainsKey(Stage.FullEvaluation));
}

// ── dossier -> Scorer seam: signals raise legitimacy ────────────────────────────────────────────────
Console.WriteLine("\n[ dossier -> scorer seam ]");
{
    JobPosting Base() => new()
    {
        Title = "Senior Engineer", TitleCanon = "senior engineer", Remote = RemoteMode.Remote,
        Compensation = new Compensation(170000m, 210000m, "USD", CompInterval.Year, CompSource.Structured),
        DescriptionText = new string('x', 60) + " Build distributed systems, own services, mentor peers.",
        RepostCount = 0, FirstPublished = DateTimeOffset.UtcNow.AddDays(-3),
    };
    var prefs = new UserPreferences { Comp = new CompTarget(150000m, 180000m, 220000m), Remote = RemoteStance.Any, Seniority = SeniorityBand.Senior };
    var sem = new SemanticScores(4.5, 4.0);

    var s = Signals.Derive(company, docs); // both true
    var withResearch = Base() with { RecruiterIdentifiable = s.RecruiterIdentifiable, CompanyDomainVerified = s.CompanyDomainVerified };
    var unknown = Base();

    var legitWith = Scorer.Score(withResearch, prefs, sem).Legitimacy;
    var legitWithout = Scorer.Score(unknown, prefs, sem).Legitimacy;
    Check("grounded signals raise legitimacy vs unknown", legitWith > legitWithout, $"{legitWith:0.000} vs {legitWithout:0.000}");
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

sealed class FakeWeb : IWebResearch
{
    private readonly IReadOnlyList<ResearchDoc> _docs; public int Calls;
    public FakeWeb(IReadOnlyList<ResearchDoc> docs) => _docs = docs;
    public Task<IReadOnlyList<ResearchDoc>> SearchAsync(string query, int max = 5, CancellationToken ct = default)
    { Calls++; return Task.FromResult(_docs); }
}
sealed class FakeModel : IDossierModel
{
    private readonly IReadOnlyList<ProposedFact> _facts; public int Calls;
    public FakeModel(IReadOnlyList<ProposedFact> facts) => _facts = facts;
    public Task<IReadOnlyList<ProposedFact>> ProposeAsync(CompanyRef c, IReadOnlyList<ResearchDoc> d, CancellationToken ct = default)
    { Calls++; return Task.FromResult(_facts); }
}
