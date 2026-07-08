namespace SeekerSvc.Scout;

/// <summary>
/// Collapses duplicate postings that arrive from different boards/ATSs for the same role.
/// Two passes:
///   1. Exact: postings sharing a <see cref="DiscoveredJob.DedupKey"/> (company | title | locality)
///      are merged. This catches the common case of the same company listing on two ATSs.
///   2. Near-duplicate: within a single company, postings whose description SimHashes are within
///      <see cref="ScoutOptions.SimHashDuplicateMaxHamming"/> bits are merged. This catches re-lists
///      whose titles or location strings differ just enough to dodge the exact key. There is no
///      title-equality guard — catching differing-title re-lists is the whole point — and the strict
///      default distance keeps genuinely distinct roles that merely share boilerplate apart.
///
/// Merging keeps the richer record and backfills missing fields from the loser, so a structured
/// pay figure or apply URL present on only one copy survives. Input order is preserved.
/// </summary>
public static class Deduplicator
{
    public static (IReadOnlyList<DiscoveredJob> Kept, int Collapsed) Collapse(
        IReadOnlyList<DiscoveredJob> jobs, ScoutOptions options)
    {
        if (jobs.Count <= 1) return (jobs, 0);

        // ---- Pass 1: exact dedup key ----
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var pass1 = new List<DiscoveredJob>();
        foreach (var job in jobs)
        {
            if (byKey.TryGetValue(job.DedupKey, out var idx))
                pass1[idx] = Merge(pass1[idx], job);
            else
            {
                byKey[job.DedupKey] = pass1.Count;
                pass1.Add(job);
            }
        }

        // ---- Pass 2: same-company near-duplicate by SimHash ----
        var kept = new List<DiscoveredJob>();
        foreach (var job in pass1)
        {
            var company = CompanyIdentity(job);
            var merged = false;
            for (var i = 0; i < kept.Count; i++)
            {
                if (!string.Equals(CompanyIdentity(kept[i]), company, StringComparison.Ordinal))
                    continue;
                if (SimHash.Hamming(kept[i].DescriptionSimHash, job.DescriptionSimHash)
                    <= options.SimHashDuplicateMaxHamming)
                {
                    kept[i] = Merge(kept[i], job);
                    merged = true;
                    break;
                }
            }
            if (!merged) kept.Add(job);
        }

        return (kept, jobs.Count - kept.Count);
    }

    private static string CompanyIdentity(DiscoveredJob j) =>
        Canon.Company(string.IsNullOrWhiteSpace(j.CompanyName) ? j.BoardHandle : j.CompanyName);

    /// <summary>
    /// Combine two records for the same role. The "primary" (richer) record is chosen, then any
    /// field it is missing is backfilled from the other. Injection signals are unioned and the
    /// flag is sticky: if either copy looked injected, the survivor stays flagged.
    /// </summary>
    private static DiscoveredJob Merge(DiscoveredJob a, DiscoveredJob b)
    {
        var (primary, other) = Prefer(a, b) ? (a, b) : (b, a);

        var comp = primary.Compensation ?? other.Compensation;
        var applyUrl = primary.ApplyUrl ?? other.ApplyUrl;
        var location = string.IsNullOrWhiteSpace(primary.Location) ? other.Location : primary.Location;
        var published = Earlier(primary.FirstPublished, other.FirstPublished);

        var flagged = primary.DescriptionLikelyInjected || other.DescriptionLikelyInjected;
        IReadOnlyList<string> signals = primary.InjectionSignals;
        if (other.InjectionSignals.Count > 0)
        {
            var union = new List<string>(primary.InjectionSignals);
            foreach (var s in other.InjectionSignals)
                if (!union.Contains(s)) union.Add(s);
            signals = union;
        }

        return primary with
        {
            Compensation = comp,
            ApplyUrl = applyUrl,
            Location = location,
            FirstPublished = published,
            DescriptionLikelyInjected = flagged,
            InjectionSignals = signals,
        };
    }

    /// <summary>True if <paramref name="a"/> is the better record to keep as the base.</summary>
    private static bool Prefer(DiscoveredJob a, DiscoveredJob b)
    {
        var aComp = a.Compensation is not null;
        var bComp = b.Compensation is not null;
        if (aComp != bComp) return aComp; // a record with pay wins

        var aLen = a.DescriptionText.Length;
        var bLen = b.DescriptionText.Length;
        if (aLen != bLen) return aLen > bLen; // more description wins

        // earlier posting wins; nulls sort last
        if (a.FirstPublished != b.FirstPublished)
        {
            if (a.FirstPublished is null) return false;
            if (b.FirstPublished is null) return true;
            return a.FirstPublished < b.FirstPublished;
        }
        return true; // stable: keep the first-seen
    }

    private static DateTimeOffset? Earlier(DateTimeOffset? x, DateTimeOffset? y)
    {
        if (x is null) return y;
        if (y is null) return x;
        return x < y ? x : y;
    }
}
