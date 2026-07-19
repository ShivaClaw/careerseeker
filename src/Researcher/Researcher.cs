using System.Security.Cryptography;
using System.Text;

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
                LastDroppedUngrounded = 0;
                return cached;
            }
        }

        var docs = await GatherAsync(company, ct).ConfigureAwait(false);
        LastRetrievedDocs = docs.Count;

        var proposed = await _model.ProposeAsync(company, docs, ct).ConfigureAwait(false);
        LastProposedFacts = proposed.Count;
        var grounded = GroundingFilter.Apply(proposed, docs);
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
