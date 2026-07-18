using System.Globalization;
using System.Text.RegularExpressions;
using SeekerSvc.Verifier;

namespace SeekerSvc.Tailor;

/// <summary>
/// Turns a tailored draft into the atomic claims the Fabrication Gate verifies (spec section 5.5,
/// step 1). It normalizes the model's declared claims AND independently scans every rendered
/// applicant-facing factual proposition. Declarations are convenience metadata, never a coverage
/// boundary: a model that omits a title, employer, outcome, or any other fact still produces a Gate atom.
/// </summary>
public static class Decomposer
{
    private static readonly Regex Percent = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex Money = new(@"\$\s?(\d[\d,]*(?:\.\d+)?)\s*([kKmMbB])?", RegexOptions.Compiled);
    private static readonly Regex Duration = new(@"(\d+(?:\.\d+)?)\s*\+?\s*years?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CredentialCue = new(
        @"\b(certified|certification|licen[cs]ed?|accredited|chartered)\b|\b(AWS|GCP|Azure|PMP|CPA|CISSP|CFA|CCNA|CKA|RN|PE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AmplifiedSkill = new(
        @"\b(expert in|extensive experience (?:in|with)|mastery of|deep knowledge of|seasoned in|specialist in|advanced proficiency in)\s+([A-Za-z0-9+#./ ]{2,30})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Sentence = new(@"[^.!?\n]+[.!?]?", RegexOptions.Compiled);

    private static readonly Regex NonFactualCourtesy = new(
        @"^\s*(dear\b|sincerely\b|regards\b|thank you\b|i(?:'m| am) (?:excited|interested)|i look forward\b|i appreciate\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Decompose a draft into atomic claims: declared (normalized) + complete rendered-prose coverage.</summary>
    public static IReadOnlyList<TailoredClaim> FromDraft(TailorDraft draft)
    {
        var atoms = new List<TailoredClaim>();
        var seen = new HashSet<(ClaimKind, string)>();

        void Add(TailoredClaim c)
        {
            var key = (c.Kind, c.Text.Trim().ToLowerInvariant());
            if (seen.Add(key)) atoms.Add(c);
        }

        // 1) declared claims, with structured numbers/durations filled in from the text if absent
        foreach (var d in draft.DeclaredClaims)
            Add(Normalize(d));

        // 2) independent prose scan over everything the applicant will actually send
        var prose = draft.ResumeText + "\n" + draft.CoverText;
        foreach (Match s in Sentence.Matches(prose))
        {
            var text = s.Value.Trim();
            if (text.Length == 0) continue;
            ScanSentence(text, Add);
        }

        return atoms;
    }

    /// <summary>Fill structured Number/Unit/DurationYears from a declared claim's own text when missing.</summary>
    private static TailoredClaim Normalize(DeclaredClaim d)
    {
        var number = d.Number;
        var unit = d.Unit;
        var duration = d.DurationYears;

        if (duration is null)
        {
            var dm = Duration.Match(d.Text);
            if (dm.Success) duration = ParseNum(dm.Groups[1].Value);
        }
        if (number is null)
        {
            var pm = Percent.Match(d.Text);
            if (pm.Success) { number = ParseNum(pm.Groups[1].Value); unit = "%"; }
            else
            {
                var mm = Money.Match(d.Text);
                if (mm.Success) { number = ParseMoney(mm); unit = "$"; }
            }
        }
        return new TailoredClaim(d.Kind, d.Text, d.Text, number, unit, duration);
    }

    /// <summary>
    /// True if the text contains anything the prose scan would turn into a *candidate* claim atom — a
    /// percentage, a money figure, a tenure, a credential cue, or an amplified-skill phrase. The single
    /// source of truth for "would this trip the Gate"; <see cref="HookGuard"/> reuses it so a cover-letter
    /// hook can never smuggle in an unverifiable number or credential.
    /// </summary>
    public static bool LooksLikeCandidateClaim(string text) =>
        string.IsNullOrEmpty(text) ? false :
        Percent.IsMatch(text) || Money.IsMatch(text) || Duration.IsMatch(text)
        || CredentialCue.IsMatch(text) || AmplifiedSkill.IsMatch(text);

    /// <summary>
    /// Returns whether a rendered sentence must be Gate-checked. Courtesy and intent-only phrases are
    /// deliberately excluded; every other non-empty rendered sentence is treated as a factual proposition.
    /// This default-deny boundary avoids relying on a brittle title/employer/proper-noun matcher.
    /// </summary>
    public static bool IsFactualProposition(string text) =>
        !string.IsNullOrWhiteSpace(text) && !NonFactualCourtesy.IsMatch(text);

    private static void ScanSentence(string text, Action<TailoredClaim> add)
    {
        // Every rendered factual proposition becomes an atom, whatever vocabulary it uses.
        // The typed atoms below add exact metric/tenure/credential checks on top of this base coverage.
        if (IsFactualProposition(text))
            add(new TailoredClaim(ClaimKind.Other, text, text));

        // credentials: strict — any cue makes the sentence a Credential atom
        if (CredentialCue.IsMatch(text))
            add(new TailoredClaim(ClaimKind.Credential, text, text));

        // amplified skills: keep the amplifier in the text so the Gate's weak-skill guard can fire
        foreach (Match a in AmplifiedSkill.Matches(text))
            add(new TailoredClaim(ClaimKind.Skill, a.Value.Trim(), a.Value.Trim()));

        // tenure: "<n> years" becomes a duration-bearing experience atom
        var dm = Duration.Match(text);
        if (dm.Success)
            add(new TailoredClaim(ClaimKind.EmploymentDates, text, text, DurationYears: ParseNum(dm.Groups[1].Value)));

        // quantified metrics: a percentage or money figure becomes a Metric atom carrying the number
        var pm = Percent.Match(text);
        if (pm.Success)
            add(new TailoredClaim(ClaimKind.Metric, text, text, Number: ParseNum(pm.Groups[1].Value), Unit: "%"));
        else
        {
            var mm = Money.Match(text);
            if (mm.Success)
                add(new TailoredClaim(ClaimKind.Metric, text, text, Number: ParseMoney(mm), Unit: "$"));
        }
    }

    private static double ParseNum(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static double ParseMoney(Match m)
    {
        var value = double.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" => value * 1_000,
            "m" => value * 1_000_000,
            "b" => value * 1_000_000_000,
            _ => value,
        };
    }
}
