using System.Globalization;
using System.Text.RegularExpressions;

namespace SeekerSvc.Verifier;

/// <summary>
/// The Fabrication Gate (spec sections 5.5, 9.3) -- a pure verification function.
///
/// It runs after the Tailor and before anything becomes READY. No autonomy level,
/// config flag, or prompt can route around it: there is deliberately no parameter
/// on Verify that forces a READY verdict. The only way to pass is to be supported
/// by the Source-of-Truth Profile.
///
/// Support for a tailored claim is established by exactly one of:
///   1. exact / synonym-normalized textual match against a source claim,
///   2. arithmetic derivation (tenure math from recorded employment dates),
///   3. semantic entailment via the pluggable matcher (which fails closed).
///
/// And WEAK-confidence support may be restated, but never quantified, never given
/// an outcome, and never described with seniority/mastery language.
/// </summary>
public static class FabricationGate
{
    // The spec's "now" is June 2026; injectable so tenure checks are deterministic.
    public const int CurrentYearDefault = 2026;

    // Phrases that turn a permissible mention of a WEAK skill into an
    // impermissible claim of mastery. Not exhaustive; production list is config-extendable.
    private static readonly string[] Amplifiers =
    {
        "expert", "expertise", "mastery", "extensive", "seasoned", "deep ",
        "advanced proficiency", "production experience", "production-grade",
        "years of experience", "highly proficient", "specialist", "deep knowledge",
    };

    private static readonly Regex Num = new(@"(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private const double NumTolerance = 1e-9;  // metrics match exactly; tenure has its own +/-1yr rule

    private enum TenureStatus { Ok, Mismatch, NoDates }

    /// <summary>
    /// Verify a tailored output against the Source-of-Truth Profile. Pure and
    /// stateless. Collects ALL violations (not the first) so the human sees the
    /// full diff in one pass rather than failing one claim at a time.
    /// </summary>
    public static async Task<VerificationResult> VerifyAsync(
        IReadOnlyList<SourceClaim> source,
        IReadOnlyList<TailoredClaim> tailored,
        ISemanticMatcher? matcher = null,
        int currentYear = CurrentYearDefault,
        CancellationToken ct = default)
    {
        matcher ??= new DefaultSemanticMatcher();
        var violations = new List<Violation>();
        foreach (var tc in tailored)
        {
            var v = await CheckOneAsync(tc, source, matcher, currentYear, ct).ConfigureAwait(false);
            if (v is not null) violations.Add(v);
        }
        var verdict = violations.Count > 0 ? Verdict.BlockedFabrication : Verdict.Ready;
        return new VerificationResult(verdict, violations, tailored.Count);
    }

    // ---- per-claim dispatch ----------------------------------------------

    private static async Task<Violation?> CheckOneAsync(
        TailoredClaim tc, IReadOnlyList<SourceClaim> source,
        ISemanticMatcher matcher, int currentYear, CancellationToken ct)
    {
        if (tc.Kind == ClaimKind.Credential)
            return CheckCredential(tc, source);
        if (tc.Kind == ClaimKind.Skill)
            return await CheckSkillAsync(tc, source, matcher, currentYear, ct).ConfigureAwait(false);

        if (tc.DurationYears is not null)
        {
            var (status, derived) = DeriveTenure(tc, source, currentYear);
            if (status == TenureStatus.Ok)
                return null;  // tenure proven by arithmetic
            if (status == TenureStatus.Mismatch)
                return new Violation(
                    tc, ViolationKind.DateMismatch,
                    $"This asserts {G(tc.DurationYears.Value)} years, but the employment " +
                    $"dates in your profile add up to about {G(derived!.Value)}.",
                    $"~{G(derived.Value)} years from recorded dates");
            return await CheckTextualAsync(tc, source, matcher, ct).ConfigureAwait(false);  // NoDates
        }

        if (tc.Number is not null)
            return await CheckMetricAsync(tc, source, matcher, ct).ConfigureAwait(false);

        return await CheckTextualAsync(tc, source, matcher, ct).ConfigureAwait(false);
    }

    // ---- credentials: strict, never inferred -----------------------------

    private static Violation? CheckCredential(TailoredClaim tc, IReadOnlyList<SourceClaim> source)
    {
        var candidates = source
            .Where(s => s.Kind is ClaimKind.Credential or ClaimKind.Education)
            .ToList();
        var t = Text.ContentTokens(tc.Text);
        foreach (var s in candidates)
        {
            var cs = Text.ContentTokens(s.Text);
            if (t.Count > 0 && (t.IsSubsetOf(cs) || cs.IsSubsetOf(t)))
                return WeakCheck(tc, s);
        }
        return new Violation(
            tc, ViolationKind.CredentialNotFound,
            "No matching credential exists in your profile. Credentials are " +
            "never inferred -- they must appear in a source document.",
            candidates.Count > 0 ? Nearest(tc.Text, candidates) : null);
    }

    // ---- skills: identify the term, then govern the characterization ------

    private static async Task<Violation?> CheckSkillAsync(
        TailoredClaim tc, IReadOnlyList<SourceClaim> source,
        ISemanticMatcher matcher, int currentYear, CancellationToken ct)
    {
        // Identification: the skill term must appear in the tailored claim.
        // Search SKILL claims first so the matched confidence is the skill's own.
        var ordered = source.Where(s => s.Kind == ClaimKind.Skill)
                            .Concat(source.Where(s => s.Kind != ClaimKind.Skill));
        var tcTokens = Text.ContentTokens(tc.Text);
        SourceClaim? supporting = null;
        foreach (var s in ordered)
        {
            var st = Text.ContentTokens(s.Text);
            if (st.Count > 0 && (st.IsSubsetOf(tcTokens) || await matcher.EntailsAsync(s.Text, tc.Text, ct).ConfigureAwait(false)))
            {
                supporting = s;
                break;
            }
        }
        if (supporting is null)
            return new Violation(
                tc, ViolationKind.NoSupportingClaim,
                "This skill is not listed anywhere in your profile.",
                Nearest(tc.Text, source));

        if (supporting.Confidence == Confidence.Weak)
        {
            if (tc.Number is not null || tc.DurationYears is not null)
                return new Violation(
                    tc, ViolationKind.QuantifiedWeakClaim,
                    "This skill is low-confidence (coursework/exposure) in your " +
                    "profile, so it may be mentioned but not quantified or given " +
                    "an outcome.",
                    supporting.Text);
            if (HasAmplifier(tc.Text))
                return new Violation(
                    tc, ViolationKind.UpgradedConfidence,
                    "This skill is low-confidence in your profile, so it can't " +
                    "be described with seniority or mastery language.",
                    supporting.Text);
            return null;
        }

        // VERIFIED / STATED skill: a duration claim can't exceed total career.
        if (tc.DurationYears is not null)
        {
            var span = TotalCareerSpan(source, currentYear);
            if (span is not null && tc.DurationYears > span + 1)
                return new Violation(
                    tc, ViolationKind.DateMismatch,
                    $"{G(tc.DurationYears.Value)} years with this skill exceeds your " +
                    $"total recorded career of ~{G(span.Value)} years.",
                    $"~{G(span.Value)} years total experience");
        }
        return null;
    }

    // ---- tenure arithmetic (non-skill experience claims) -----------------

    private static (TenureStatus, double?) DeriveTenure(
        TailoredClaim tc, IReadOnlyList<SourceClaim> source, int currentYear)
    {
        var dates = source
            .Where(s => s.Kind == ClaimKind.EmploymentDates && s.YearStart.GetValueOrDefault() != 0)
            .ToList();
        if (dates.Count == 0)
            return (TenureStatus.NoDates, null);

        var tcTokens = Text.ContentTokens(tc.Text);
        SourceClaim? named = null;
        foreach (var s in dates)
        {
            if (!string.IsNullOrEmpty(s.Employer))
            {
                var emp = Text.ContentTokens(s.Employer);
                if (emp.Count > 0 && emp.IsSubsetOf(tcTokens))
                {
                    named = s;
                    break;
                }
            }
        }

        int derived;
        if (named is not null)
        {
            derived = (named.YearEnd ?? currentYear) - named.YearStart!.Value;
        }
        else
        {
            var start = dates.Min(s => s.YearStart!.Value);
            var end = dates.Max(s => s.YearEnd ?? currentYear);
            derived = end - start;
        }

        if (Math.Abs(tc.DurationYears!.Value - derived) <= 1)  // tolerate rounding
            return (TenureStatus.Ok, derived);
        return (TenureStatus.Mismatch, derived);
    }

    // ---- metrics: qualitative match AND exact number ---------------------

    private static async Task<Violation?> CheckMetricAsync(
        TailoredClaim tc, IReadOnlyList<SourceClaim> source, ISemanticMatcher matcher, CancellationToken ct)
    {
        var tcQual = StripNumbers(tc.Text);
        SourceClaim? qualitativeMatch = null;
        foreach (var s in source)
        {
            if (await SupportsTextAsync(tcQual, StripNumbers(s.Text), matcher, ct).ConfigureAwait(false))
            {
                qualitativeMatch = s;
                if (s.Number is not null)
                    break;  // prefer a source carrying a number to compare against
            }
        }
        if (qualitativeMatch is null)
            return new Violation(
                tc, ViolationKind.NoSupportingClaim,
                "This quantified achievement has no basis in your profile.",
                Nearest(tc.Text, source));

        var srcNum = qualitativeMatch.Number;
        if (srcNum is null || Math.Abs(srcNum.Value - tc.Number!.Value) > NumTolerance)
            return new Violation(
                tc, ViolationKind.NumericMismatch,
                $"The figure ({G(tc.Number!.Value)}{tc.Unit ?? ""}) does not match what " +
                "your profile supports.",
                qualitativeMatch.Text);
        return WeakCheck(tc, qualitativeMatch);
    }

    // ---- general textual support -----------------------------------------

    private static async Task<Violation?> CheckTextualAsync(
        TailoredClaim tc, IReadOnlyList<SourceClaim> source, ISemanticMatcher matcher, CancellationToken ct)
    {
        foreach (var s in source)
            if (await SupportsTextAsync(tc.Text, s.Text, matcher, ct).ConfigureAwait(false))
                return WeakCheck(tc, s);
        return new Violation(
            tc, ViolationKind.NoSupportingClaim,
            "No supporting fact for this statement exists in your profile.",
            Nearest(tc.Text, source));
    }

    // ---- WEAK-confidence guard (for non-skill paths) ---------------------

    private static Violation? WeakCheck(TailoredClaim tc, SourceClaim supporting)
    {
        if (supporting.Confidence != Confidence.Weak)
            return null;
        if (tc.Number is not null || tc.DurationYears is not null)
            return new Violation(
                tc, ViolationKind.QuantifiedWeakClaim,
                "This is supported only by a low-confidence fact, so it may be " +
                "mentioned but not quantified or given an outcome.",
                supporting.Text);
        if (HasAmplifier(tc.Text))
            return new Violation(
                tc, ViolationKind.UpgradedConfidence,
                "This is supported only by a low-confidence fact, so it can't be " +
                "described with seniority or mastery language.",
                supporting.Text);
        return null;
    }

    // ---- helpers ---------------------------------------------------------

    private static string StripNumbers(string text) => Num.Replace(text, " ");

    private static async Task<bool> SupportsTextAsync(string tailoredText, string sourceText, ISemanticMatcher matcher, CancellationToken ct)
    {
        var t = Text.ContentTokens(tailoredText);
        var s = Text.ContentTokens(sourceText);
        if (t.Count == 0) return true;
        if (t.IsSubsetOf(s)) return true;   // covers exact-equal and subset
        return await matcher.EntailsAsync(sourceText, tailoredText, ct).ConfigureAwait(false);
    }

    private static double Overlap(string a, string b)
    {
        var ta = Text.ContentTokens(a);
        if (ta.Count == 0) return 0.0;
        var tb = Text.ContentTokens(b);
        return (double)ta.Count(tb.Contains) / ta.Count;
    }

    private static string? Nearest(string tcText, IEnumerable<SourceClaim> source)
    {
        string? best = null;
        double bestScore = 0.0;
        foreach (var s in source)
        {
            var score = Overlap(tcText, s.Text);
            if (score > bestScore)
            {
                best = s.Text;
                bestScore = score;
            }
        }
        return best;
    }

    private static double? TotalCareerSpan(IReadOnlyList<SourceClaim> source, int currentYear)
    {
        var dates = source
            .Where(s => s.Kind == ClaimKind.EmploymentDates && s.YearStart.GetValueOrDefault() != 0)
            .ToList();
        if (dates.Count == 0) return null;
        var start = dates.Min(s => s.YearStart!.Value);
        var end = dates.Max(s => s.YearEnd ?? currentYear);
        return end - start;
    }

    private static bool HasAmplifier(string text)
    {
        var low = text.ToLowerInvariant();
        return Amplifiers.Any(a => low.Contains(a));
    }

    // ":g"-style formatting (no trailing zeros), invariant culture for determinism.
    private static string G(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
}
