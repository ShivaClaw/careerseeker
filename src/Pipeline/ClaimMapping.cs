using System.Globalization;
using System.Text.RegularExpressions;
using SeekerSvc.Store;
using SeekerSvc.Verifier;

namespace SeekerSvc.Pipeline;

/// <summary>
/// Maps the Store's persisted <see cref="ClaimRow"/> onto the Verifier's <see cref="SourceClaim"/>,
/// the Fabrication Gate's oracle. The Store persists claims as strings; the Gate wants typed enums and
/// structured metric values when they can be derived safely from text.
/// </summary>
internal static class ClaimMapping
{
    private static readonly Regex Percent = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex Money = new(@"\$\s?(\d[\d,]*(?:\.\d+)?)\s*([kKmMbB])?", RegexOptions.Compiled);

    public static IReadOnlyList<SourceClaim> ToSourceClaims(IReadOnlyList<ClaimRow> rows)
    {
        var list = new List<SourceClaim>(rows.Count);
        foreach (var r in rows)
        {
            var kind = ParseKind(r.Kind);
            var (number, unit) = kind == ClaimKind.Metric ? ParseMetric(r.Text) : (null, null);
            list.Add(new SourceClaim(
                Id: r.Id,
                Kind: kind,
                Text: r.Text,
                Confidence: ParseConfidence(r.Confidence),
                SourceDoc: r.SourceDoc ?? "",
                Number: number,
                Unit: unit));
        }
        return list;
    }

    private static ClaimKind ParseKind(string s) =>
        Enum.TryParse<ClaimKind>(s, ignoreCase: true, out var k) ? k : ClaimKind.Other;

    private static Confidence ParseConfidence(string s) =>
        Enum.TryParse<Confidence>(s, ignoreCase: true, out var c) ? c : Confidence.Stated;

    private static (double? Number, string? Unit) ParseMetric(string text)
    {
        var percent = Percent.Match(text);
        if (percent.Success)
            return (double.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture), "%");

        var money = Money.Match(text);
        if (!money.Success)
            return (null, null);

        var value = double.Parse(money.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        value *= money.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" => 1_000,
            "m" => 1_000_000,
            "b" => 1_000_000_000,
            _ => 1,
        };
        return (value, "$");
    }
}
