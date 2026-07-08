namespace SeekerSvc.Researcher;

/// <summary>Which part of the dossier a fact belongs to (spec §1.1: overview, signals, fit, risks).</summary>
public enum DossierTopic
{
    /// <summary>What the company does — neutral context.</summary>
    Overview,
    /// <summary>A legitimacy / health signal (funding, news, headcount).</summary>
    Signal,
    /// <summary>Why this candidate fits — for the user's own read, not employer-facing.</summary>
    Fit,
    /// <summary>A risk / caution (layoffs, lawsuits, churn).</summary>
    Risk,
    /// <summary>A researched, company-specific line the cover letter may use (spec §5.5). Highest-risk: it
    /// goes into a document the user sends, so it must be grounded in a real source or it is never emitted.</summary>
    Hook,
}

/// <summary>
/// One fact in a dossier. Every fact carries the <see cref="SourceUrl"/> it came from; the Researcher
/// constructs a fact only after confirming that URL was actually retrieved and that the cited document
/// supports the text. A fact without grounding is dropped, never surfaced — the module's safety invariant.
/// </summary>
public sealed record DossierFact(
    DossierTopic Topic,
    string Text,
    string SourceUrl,
    string SourceTitle = "");

/// <summary>
/// The researched signals the Scorer's legitimacy axis consumes (spec §5.4). Each is nullable: null means
/// "not established", which the Scorer treats neutrally rather than as a negative. The Researcher never
/// asserts a signal it could not ground.
/// </summary>
public sealed record ResearchedSignals(bool? RecruiterIdentifiable, bool? CompanyDomainVerified)
{
    public static readonly ResearchedSignals Unknown = new(null, null);
}

/// <summary>The company to research.</summary>
public sealed record CompanyRef(string Name, string? Domain = null);

/// <summary>
/// A company dossier (spec §1.1, §5.5): overview, signals, fit, risks, and cover-letter hooks — every
/// fact grounded in a source. Content-addressed (<see cref="ContentHash"/>) and timestamped so the engine
/// can cache it and compute a "dossier delta" on refresh (spec §3.2).
/// </summary>
public sealed record Dossier(
    string Company,
    string? Domain,
    IReadOnlyList<DossierFact> Facts,
    ResearchedSignals Signals,
    DateTimeOffset BuiltUtc,
    string ContentHash)
{
    public IEnumerable<DossierFact> Hooks => Facts.Where(f => f.Topic == DossierTopic.Hook);
    public IEnumerable<DossierFact> Risks => Facts.Where(f => f.Topic == DossierTopic.Risk);
    public IEnumerable<DossierFact> Overview => Facts.Where(f => f.Topic == DossierTopic.Overview);

    /// <summary>The single best cover-letter hook, or null if research grounded none (then the Tailor omits it).</summary>
    public DossierFact? BestHook => Hooks.FirstOrDefault();
}

/// <summary>A document retrieved by web research. Its text is UNTRUSTED — scanned as data, never instructions.</summary>
public sealed record ResearchDoc(string Url, string Title, string Text);

/// <summary>
/// Web search + fetch. The real implementation calls a search API and fetches pages; the sandbox fakes it.
/// Returned text is untrusted content (prompt-injection risk, like Scout's JDs) and is treated as data only.
/// </summary>
public interface IWebResearch
{
    Task<IReadOnlyList<ResearchDoc>> SearchAsync(string query, int maxResults = 5, CancellationToken ct = default);
}

/// <summary>A fact the model proposes from the retrieved docs, with the source it claims. Not yet trusted.</summary>
public sealed record ProposedFact(DossierTopic Topic, string Text, string SourceUrl, string SourceTitle = "");

/// <summary>
/// The LLM seam: turn retrieved documents into proposed dossier facts. Analogous to the Tailor's
/// ITailorModel. The real implementation routes through the LLM Gateway's FullEvaluation stage; the model
/// only ever proposes — the Researcher's grounding filter decides what survives.
/// </summary>
public interface IDossierModel
{
    Task<IReadOnlyList<ProposedFact>> ProposeAsync(CompanyRef company, IReadOnlyList<ResearchDoc> docs, CancellationToken ct = default);
}

/// <summary>Cache of built dossiers (spec §6 schema: dossier_path/dossier_at). In-memory here; disk content-addressed in production.</summary>
public interface IDossierStore
{
    Task<Dossier?> GetAsync(string companyKey, CancellationToken ct = default);
    Task PutAsync(string companyKey, Dossier dossier, CancellationToken ct = default);
}
