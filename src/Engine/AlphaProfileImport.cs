using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

public sealed record AlphaProfileImportResult(
    long ProfileId,
    int ClaimCount,
    string ProfileJson,
    IReadOnlyList<string> ClaimIds);

public static class AlphaProfileImport
{
    private const string ExpectedFormat = "careerseeker-alpha-profile-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly HashSet<string> ValidConfidences = new(StringComparer.OrdinalIgnoreCase)
    {
        "verified",
        "stated",
        "weak",
    };

    private static readonly IReadOnlyDictionary<string, string> ValidKinds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Employer"] = "Employer",
            ["Title"] = "Title",
            ["EmploymentDates"] = "EmploymentDates",
            ["Metric"] = "Metric",
            ["Skill"] = "Skill",
            ["Credential"] = "Credential",
            ["Education"] = "Education",
            ["Other"] = "Other",
        };

    public static string TemplateJson() =>
        JsonSerializer.Serialize(new AlphaProfileFile(
            "careerseeker-alpha-profile-v1",
            JsonSerializer.Deserialize<JsonElement>("""
            {
              "name": "Jordan Lee",
              "email": "jordan@example.com",
              "headline": "Senior Software Engineer"
            }
            """),
            new[]
            {
                new AlphaProfileClaim(
                    Id: null,
                    Kind: "Title",
                    Text: "Senior Software Engineer",
                    Confidence: "verified",
                    SourceDoc: "resume.pdf"),
                new AlphaProfileClaim(
                    Id: null,
                    Kind: "Skill",
                    Text: "distributed systems",
                    Confidence: "verified",
                    SourceDoc: "resume.pdf"),
                new AlphaProfileClaim(
                    Id: null,
                    Kind: "Metric",
                    Text: "reduced p99 latency 30%",
                    Confidence: "verified",
                    SourceDoc: "resume.pdf"),
                new AlphaProfileClaim(
                    Id: null,
                    Kind: "Other",
                    Text: "I have built reliable distributed systems in Go",
                    Confidence: "verified",
                    SourceDoc: "resume.pdf"),
            }), JsonOptions) + Environment.NewLine;

    public static async Task<AlphaProfileImportResult> ImportAsync(
        ISeekerStore store,
        string profilePath,
        string configKey,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(profilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Profile import file not found.", fullPath);

        var json = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<AlphaProfileFile>(json, JsonOptions)
                     ?? throw new InvalidOperationException("Profile import file is empty.");
        if (!ExpectedFormat.Equals(parsed.Format, StringComparison.Ordinal))
            throw new InvalidOperationException("Profile import file has an unrecognized format.");

        var claims = parsed.Claims ?? Array.Empty<AlphaProfileClaim>();
        if (claims.Count == 0)
            throw new InvalidOperationException("Profile import must include at least one claim.");

        var preparedRows = claims.Select((claim, index) => ToRow(claim, profileId: 0, index)).ToList();
        ValidateUniqueClaimIds(preparedRows);

        var profileJson = parsed.Profile.ValueKind == JsonValueKind.Undefined ||
                          parsed.Profile.ValueKind == JsonValueKind.Null
            ? "{}"
            : parsed.Profile.GetRawText();
        var profileId = await store.UpsertProfileAsync(profileJson, ct).ConfigureAwait(false);
        var rows = preparedRows.Select(row => row with { ProfileId = profileId }).ToList();
        await store.ReplaceClaimsAsync(profileId, rows, ct).ConfigureAwait(false);
        await store.SetConfigAsync(configKey, profileId.ToString(), ct).ConfigureAwait(false);
        return new AlphaProfileImportResult(
            profileId,
            rows.Count,
            profileJson,
            rows.Select(r => r.Id).ToArray());
    }

    private static ClaimRow ToRow(AlphaProfileClaim claim, long profileId, int index)
    {
        var kind = NormalizeKind(RequireText(claim.Kind, $"claims[{index}].kind"), index);
        var text = RequireText(claim.Text, $"claims[{index}].text");
        var confidence = string.IsNullOrWhiteSpace(claim.Confidence)
            ? "verified"
            : claim.Confidence.Trim().ToLowerInvariant();
        if (!ValidConfidences.Contains(confidence))
            throw new InvalidOperationException(
                $"claims[{index}].confidence must be verified, stated, or weak.");

        var id = string.IsNullOrWhiteSpace(claim.Id)
            ? DeterministicClaimId(index, kind, text)
            : claim.Id.Trim();
        return new ClaimRow(id, profileId, kind, text, confidence, TrimToNull(claim.SourceDoc));
    }

    private static string NormalizeKind(string kind, int index) =>
        ValidKinds.TryGetValue(kind, out var canonical)
            ? canonical
            : throw new InvalidOperationException(
                $"claims[{index}].kind must be one of: {string.Join(", ", ValidKinds.Values)}.");

    private static void ValidateUniqueClaimIds(IReadOnlyList<ClaimRow> claims)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            if (!seen.Add(claim.Id))
                throw new InvalidOperationException($"Profile import contains duplicate claim id '{claim.Id}'.");
        }
    }

    private static string DeterministicClaimId(int index, string kind, string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + text));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        return $"imported-{index + 1:D3}-{hash}";
    }

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{field} is required.");
        return value.Trim();
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AlphaProfileFile(
        string? Format,
        JsonElement Profile,
        IReadOnlyList<AlphaProfileClaim>? Claims);

    private sealed record AlphaProfileClaim(
        string? Id,
        string? Kind,
        string? Text,
        string? Confidence,
        string? SourceDoc);
}
