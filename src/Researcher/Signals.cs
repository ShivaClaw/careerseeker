using System.Text.RegularExpressions;

namespace SeekerSvc.Researcher;

/// <summary>
/// Derives the Scorer's researched signals (spec §5.4) deterministically from retrieved documents — no
/// model judgment, so the legitimacy axis can't be moved by a hallucination. Both signals are positive-
/// only: established → true; not established → null (unknown, scored neutral). The Researcher never
/// asserts a negative it cannot prove, so research can only ever raise confidence, never manufacture doubt.
/// </summary>
public static class Signals
{
    private static readonly Regex RecruiterCue = new(
        @"\b(recruiter|recruiting|talent acquisition|talent partner|hiring manager|people team|careers?@)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ResearchedSignals Derive(CompanyRef company, IReadOnlyList<ResearchDoc> docs)
    {
        bool? domainVerified = null;
        if (!string.IsNullOrWhiteSpace(company.Domain))
        {
            var host = company.Domain.Trim().ToLowerInvariant();
            // verified if the company's own domain shows up as a retrieved source host
            var found = docs.Any(d => UrlHost(d.Url).EndsWith(host, StringComparison.OrdinalIgnoreCase));
            domainVerified = found ? true : null;
        }

        bool? recruiterIdentifiable = docs.Any(d => RecruiterCue.IsMatch(d.Text) || RecruiterCue.IsMatch(d.Title))
            ? true : null;

        return new ResearchedSignals(recruiterIdentifiable, domainVerified);
    }

    private static string UrlHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
    }
}
