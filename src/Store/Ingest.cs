using SeekerSvc.Scout;

namespace SeekerSvc.Store;

/// <summary>
/// Pure mapping from a Scout <see cref="DiscoveredJob"/> to the company and job upsert inputs the
/// store persists. This is the seam between discovery and storage: Scout decides what a posting is,
/// Ingest decides how it lands in the schema. Comp/enum values are stringified to their stored form;
/// nothing is invented (a null compensation stays null).
/// </summary>
public static class Ingest
{
    public static (CompanyUpsert Company, JobUpsert Job) From(DiscoveredJob j, string? jdPath = null)
    {
        var company = new CompanyUpsert(
            AtsKind: j.Source.ToString(),
            Handle: j.BoardHandle,
            Name: j.CompanyName);

        var comp = j.Compensation;
        var job = new JobUpsert(
            Source: j.Source.ToString(),
            ExternalId: j.JobId,
            Url: j.Url,
            Title: j.Title,
            TitleCanon: j.TitleCanon,
            DedupKey: j.DedupKey,
            Remote: j.Remote.ToString(),
            SimHash: j.DescriptionSimHash,
            FirstSeen: (j.FirstPublished ?? DateTimeOffset.UtcNow).ToString("O"),
            ApplyUrl: j.ApplyUrl,
            Location: j.Location,
            CompMin: comp?.Min,
            CompMax: comp?.Max,
            CompCurrency: comp?.Currency,
            CompInterval: comp is null ? null : comp.Interval.ToString(),
            CompSource: comp is null ? null : comp.Source.ToString(),
            JdPath: jdPath,
            Injected: j.DescriptionLikelyInjected,
            InjectionSignals: j.InjectionSignals.Count == 0 ? null : string.Join(",", j.InjectionSignals));

        return (company, job);
    }
}
