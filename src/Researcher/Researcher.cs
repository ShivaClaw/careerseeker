using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SeekerSvc.Researcher;

/// <summary>Tunables for research.</summary>
public sealed record ResearcherOptions(
    TimeSpan DossierTtl,
    int MaxDocsPerQuery = 5)
{
    public static ResearcherOptions Default { get; } = new(TimeSpan.FromDays(14));
}

/// <summary>
/// Builds and caches a company dossier (spec §5.4 "Researcher — company dossier builder (web search,
/// cached)"). Flow: serve a fresh cached dossier if present; otherwise search the web, let the dossier
/// model propose facts, keep only the <see cref="GroundingFilter"/>-approved ones, derive the Scorer's
/// researched signals deterministically, and cache the result. The model never decides what is true — it
/// proposes, and grounded retrieval decides.
/// </summary>
public sealed class Researcher
{
    private readonly IWebResearch _web;
    private readonly IDossierModel _model;
    private readonly IDossierStore _cache;
    private readonly ResearcherOptions _opt;
    private readonly Func<DateTimeOffset> _clock;

    public Researcher(
        IWebResearch web, IDossierModel model, IDossierStore cache,
        ResearcherOptions? options = null, Func<DateTimeOffset>? clock = null)
    {
        _web = web;
        _model = model;
        _cache = cache;
        _opt = options ?? ResearcherOptions.Default;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Number of proposed facts the last build dropped for lack of grounding (observability).</summary>
    public int LastDroppedUngrounded { get; private set; }
    /// <summary>Number of retrieved source documents used by the last uncached build.</summary>
    public int LastRetrievedDocs { get; private set; }
    /// <summary>Number of model-proposed facts before grounding in the last uncached build.</summary>
    public int LastProposedFacts { get; private set; }
    /// <summary>Number of deterministic source-text fallback facts used when the model proposes nothing.</summary>
    public int LastFallbackFacts { get; private set; }

    public async Task<Dossier> BuildAsync(CompanyRef company, bool forceRefresh = false, CancellationToken ct = default)
    {
        var key = Key(company);

        if (!forceRefresh)
        {
            var cached = await _cache.GetAsync(key, ct).ConfigureAwait(false);
            if (cached is not null && _clock() - cached.BuiltUtc < _opt.DossierTtl)
            {
                LastRetrievedDocs = 0;
                LastProposedFacts = 0;
                LastFallbackFacts = 0;
                LastDroppedUngrounded = 0;
                return cached;
            }
        }

        var docs = await GatherAsync(company, ct).ConfigureAwait(false);
        LastRetrievedDocs = docs.Count;

        var proposed = await _model.ProposeAsync(company, docs, ct).ConfigureAwait(false);
        LastProposedFacts = proposed.Count;
        var factsToGround = proposed;
        if (factsToGround.Count == 0)
            factsToGround = FallbackProposals(company, docs);
        LastFallbackFacts = proposed.Count == 0 ? factsToGround.Count : 0;

        var grounded = GroundingFilter.Apply(factsToGround, docs);
        LastDroppedUngrounded = grounded.Dropped;

        var signals = Signals.Derive(company, docs);
        var dossier = new Dossier(
            company.Name, company.Domain, grounded.Grounded, signals,
            _clock(), HashOf(grounded.Grounded));

        await _cache.PutAsync(key, dossier, ct).ConfigureAwait(false);
        return dossier;
    }

    private async Task<IReadOnlyList<ResearchDoc>> GatherAsync(CompanyRef company, CancellationToken ct)
    {
        var queries = new[]
        {
            $"{company.Name} company overview",
            $"{company.Name} funding news hiring",
            $"{company.Name} careers team recruiter",
        };

        var docs = new List<ResearchDoc>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in queries)
        {
            foreach (var d in await _web.SearchAsync(q, _opt.MaxDocsPerQuery, ct).ConfigureAwait(false))
                if (seen.Add(d.Url)) docs.Add(d);
        }
        return docs;
    }

    private static string Key(CompanyRef c) =>
        (c.Domain ?? c.Name).Trim().ToLowerInvariant();

    private static IReadOnlyList<ProposedFact> FallbackProposals(CompanyRef company, IReadOnlyList<ResearchDoc> docs)
    {
        var terms = new[] { company.Name, company.Domain ?? "" }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
        if (terms.Length == 0) return Array.Empty<ProposedFact>();

        var proposed = new List<ProposedFact>();
        foreach (var doc in docs)
        {
            foreach (var snippet in Snippets(doc.Text))
            {
                if (!MentionsAny(snippet, terms) || LooksBoilerplate(snippet)) continue;
                proposed.Add(new ProposedFact(DossierTopic.Overview, snippet, doc.Url, doc.Title));
                break;
            }
            if (proposed.Count >= 3) break;
        }

        return proposed;
    }

    private static IEnumerable<string> Snippets(string text)
    {
        foreach (var raw in Regex.Split(text, @"(?<=[.!?])\s+|\s+[|]\s+"))
        {
            var snippet = Regex.Replace(raw, @"\s+", " ").Trim();
            if (snippet.Length is >= 40 and <= 220 && WordCount(snippet) >= 6)
                yield return snippet;
        }
    }

    private static bool MentionsAny(string text, IReadOnlyList<string> terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool LooksBoilerplate(string text) =>
        text.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("privacy policy", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("subscribe", StringComparison.OrdinalIgnoreCase);

    private static int WordCount(string text) =>
        Regex.Matches(text, @"[A-Za-z][A-Za-z0-9'\-]{2,}").Count;

    private static string HashOf(IReadOnlyList<DossierFact> facts)
    {
        var sb = new StringBuilder();
        foreach (var f in facts.OrderBy(f => f.SourceUrl, StringComparer.Ordinal).ThenBy(f => f.Text, StringComparer.Ordinal))
            sb.Append(f.Topic).Append('\u241F').Append(f.Text).Append('\u241F').Append(f.SourceUrl).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}

/// <summary>Default in-memory dossier cache. Production persists to disk content-addressed (spec §6).</summary>
public sealed class InMemoryDossierStore : IDossierStore
{
    private readonly Dictionary<string, Dossier> _byKey = new();
    public Task<Dossier?> GetAsync(string companyKey, CancellationToken ct = default) =>
        Task.FromResult(_byKey.TryGetValue(companyKey, out var d) ? d : null);
    public Task PutAsync(string companyKey, Dossier dossier, CancellationToken ct = default)
    { _byKey[companyKey] = dossier; return Task.CompletedTask; }
}
