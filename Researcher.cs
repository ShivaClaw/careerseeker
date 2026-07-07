using SeekerSvc.Store;
using SeekerSvc.Verifier;

namespace SeekerSvc.Pipeline;

/// <summary>
/// Maps the Store's persisted <see cref="ClaimRow"/> onto the Verifier's <see cref="SourceClaim"/> — the
/// Fabrication Gate's oracle. The Store persists claims as strings (kind, confidence); the Gate wants the
/// typed enums. Unknown kinds map to <see cref="ClaimKind.Other"/> and unknown confidences to the
/// face-value <see cref="Confidence.Stated"/> — both safe defaults: "Other" gets generic text matching
/// rather than strict structured checks, and "Stated" neither elevates a weak claim nor discards a real
/// one.
///
/// <para>Note: <see cref="ClaimRow"/> does not yet carry the structured numeric/employer/year fields that
/// <see cref="SourceClaim"/> can hold, so those are left null and the Gate falls back to text matching for
/// those atoms. Populating them end-to-end is a Store-schema enhancement, not a correctness gap — the Gate
/// is strictly more conservative without them.</para>
/// </summary>
internal static class ClaimMapping
{
    public static IReadOnlyList<SourceClaim> ToSourceClaims(IReadOnlyList<ClaimRow> rows)
    {
        var list = new List<SourceClaim>(rows.Count);
        foreach (var r in rows)
            list.Add(new SourceClaim(
                Id: r.Id,
                Kind: ParseKind(r.Kind),
                Text: r.Text,
                Confidence: ParseConfidence(r.Confidence),
                SourceDoc: r.SourceDoc ?? ""));
        return list;
    }

    private static ClaimKind ParseKind(string s) =>
        Enum.TryParse<ClaimKind>(s, ignoreCase: true, out var k) ? k : ClaimKind.Other;

    private static Confidence ParseConfidence(string s) =>
        Enum.TryParse<Confidence>(s, ignoreCase: true, out var c) ? c : Confidence.Stated;
}
