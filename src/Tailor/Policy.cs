using System.Text.RegularExpressions;

namespace SeekerSvc.Tailor;

/// <summary>
/// Resolves application/recruiter questions against the user's Approved Answer Bank. This enforces
/// spec section 2.1, rule 2: template-class questions are answered verbatim from approved wordings;
/// anything without a confident match is flagged novel and never auto-answered — it escalates.
/// </summary>
public static class AnswerBank
{
    public static AnswerResolution Resolve(string question, IReadOnlyList<ApprovedAnswer> bank)
    {
        var q = Normalize(question);

        // exact (normalized) match first
        foreach (var a in bank)
            if (Normalize(a.Question) == q)
                return new AnswerResolution(question, a.Answer, AnswerSource.ApprovedBank);

        // strong keyword-overlap match (handles paraphrase of the same standard question)
        ApprovedAnswer? best = null;
        double bestScore = 0;
        foreach (var a in bank)
        {
            var score = Overlap(q, Normalize(a.Question));
            if (score > bestScore) { bestScore = score; best = a; }
        }
        if (best is not null && bestScore >= 0.75)
            return new AnswerResolution(question, best.Answer, AnswerSource.ApprovedBank);

        // no confident match: do NOT invent an answer
        return new AnswerResolution(question, null, AnswerSource.FlaggedNovel);
    }

    /// <summary>Resolve a batch; returns the auto-answerable answers and the questions needing escalation.</summary>
    public static (IReadOnlyDictionary<string, string> Answered, IReadOnlyList<string> Flagged) ResolveAll(
        IReadOnlyList<string> questions, IReadOnlyList<ApprovedAnswer> bank)
    {
        var answered = new Dictionary<string, string>();
        var flagged = new List<string>();
        foreach (var question in questions)
        {
            var r = Resolve(question, bank);
            if (r.IsAutoAnswerable) answered[question] = r.Answer!;
            else flagged.Add(question);
        }
        return (answered, flagged);
    }

    private static string Normalize(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9 ]", " ").Trim();

    private static double Overlap(string a, string b)
    {
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ta.Count == 0) return 0;
        ta.IntersectWith(tb);
        return (double)ta.Count / Math.Max(1, Math.Min(
            a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
    }
}

/// <summary>The outcome of checking a cover letter against the style card.</summary>
public sealed record StyleReport(bool Ok, IReadOnlyList<string> BannedHits, int CoverWordCount, bool OverLength);

/// <summary>Deterministic style-card enforcement: banned phrases and the cover-letter word cap.</summary>
public static class StyleGuard
{
    public static StyleReport Check(string coverText, StyleCard card)
    {
        var low = coverText.ToLowerInvariant();
        var hits = card.BannedPhrases.Where(p => low.Contains(p.ToLowerInvariant())).ToList();
        var words = coverText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var over = words > card.MaxCoverWords;
        return new StyleReport(hits.Count == 0 && !over, hits, words, over);
    }
}
