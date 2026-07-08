using System.Text;
using System.Text.Json;
using SeekerSvc.Gateway;
using SeekerSvc.Pipeline;
using SeekerSvc.Verifier;

namespace SeekerSvc.Tailor;

/// <summary>
/// Implements the Tailor's <see cref="ITailorModel"/> generation port (Drafting.cs) on top of the LLM
/// Gateway, routing generation through <see cref="Stage.Tailoring"/> — the strong-cloud tier. This is
/// the seam that connects the Gateway to the rest of the engine: the Tailor depends only on
/// <see cref="ITailorModel"/>, so dropping this in gives it real inference without any Tailor change.
///
/// Two safety alignments matter here, though neither is the real enforcement (the Fabrication Gate is):
///  • The prompt supplies the profile claims as the ONLY admissible facts and explicitly forbids
///    inventing numbers, credentials, or outcomes — the opposite of job-hunter's "push for numbers"
///    bug. The Gate still verifies every atom; this just stops the model fighting it.
///  • The returned <see cref="DeclaredClaim"/>s are the model's self-report and are NOT trusted to be
///    complete — the Decomposer independently re-scans the prose. So a model that under-declares cannot
///    smuggle a claim past the Gate; declaration is a convenience, not a trust boundary.
///
/// On unparseable output it throws, so the Pipeline treats it as a generation failure (rework/escalate)
/// rather than shipping an unsplit blob.
/// </summary>
public sealed class GatewayTailorModel : ITailorModel
{
    private readonly LlmGateway _gateway;

    public GatewayTailorModel(LlmGateway gateway) => _gateway = gateway;

    public async Task<TailorDraft> GenerateAsync(TailorModelRequest request, CancellationToken ct = default)
    {
        var messages = new[]
        {
            LlmMessage.System(BuildSystemPrompt(request)),
            LlmMessage.User(BuildUserPrompt(request)),
        };

        var llmReq = new LlmRequest(
            Stage.Tailoring,
            messages,
            MaxOutputTokens: 2048,
            Temperature: 0.4,
            PurposeTag: $"tailor:job={request.Job.JobId}");

        var resp = await _gateway.CompleteAsync(llmReq, ct).ConfigureAwait(false);
        return ParseDraft(resp.Text);
    }

    private static string BuildSystemPrompt(TailorModelRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You tailor a resume and a cover letter for one job using ONLY the candidate facts provided.");
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- Use only the supplied profile facts. Do NOT invent employers, titles, dates, metrics, numbers, or credentials.");
        sb.AppendLine("- Do not quantify a fact the profile does not quantify. Do not upgrade tentative facts into firm ones.");
        sb.AppendLine("- A downstream verifier rejects any claim not supported by the profile, so unsupported claims only waste a pass.");
        sb.AppendLine($"- Cover letter <= {r.Style.MaxCoverWords} words. Never use these phrases: {string.Join("; ", r.Style.BannedPhrases)}.");
        sb.AppendLine("- Answer application questions ONLY if given an approved answer; otherwise leave them out (they escalate).");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a JSON object, no prose, no markdown fences, of the form:");
        sb.AppendLine("{\"resume\":\"...\",\"cover\":\"...\",\"claims\":[{\"kind\":\"Metric\",\"text\":\"...\",\"number\":30,\"unit\":\"%\",\"durationYears\":null}],\"answers\":{\"question\":\"answer\"}}");
        sb.AppendLine("kind is one of: Employer,Title,EmploymentDates,Metric,Skill,Credential,Education,Other.");
        return sb.ToString();
    }

    private static string BuildUserPrompt(TailorModelRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"JOB: {r.Job.Title} at {r.Job.Company}.");
        sb.AppendLine();
        sb.AppendLine("CANDIDATE FACTS (the only admissible facts):");
        foreach (var c in r.Profile)
            sb.AppendLine($"- [{c.Kind}] {c.Text}  (confidence: {c.Confidence})");
        if (r.Constraints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CORRECTIONS FROM A PRIOR ATTEMPT (must obey):");
            foreach (var con in r.Constraints) sb.AppendLine($"- {con}");
        }
        if (!string.IsNullOrWhiteSpace(r.CompanyHook))
        {
            sb.AppendLine();
            sb.AppendLine("VERIFIED COMPANY CONTEXT (about the employer, already fact-checked):");
            sb.AppendLine($"- {r.CompanyHook}");
            sb.AppendLine("You may weave this in as ONE sentence of genuine interest in the company. It is a fact about");
            sb.AppendLine("the employer, not the candidate: do not attribute it to the candidate and do not add any number,");
            sb.AppendLine("percentage, or credential to it.");
        }
        if (r.Questions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("APPLICATION QUESTIONS (answer only from approved answers; else omit):");
            foreach (var q in r.Questions) sb.AppendLine($"- {q}");
        }
        return sb.ToString();
    }

    /// <summary>Parse the model's JSON into a draft. Tolerates accidental code fences; throws on real failure.</summary>
    internal static TailorDraft ParseDraft(string raw)
    {
        var json = StripFences(raw).Trim();
        using var doc = JsonDocument.Parse(json); // throws -> Pipeline treats as generation failure
        var root = doc.RootElement;

        var resume = GetString(root, "resume");
        var cover = GetString(root, "cover");

        var claims = new List<DeclaredClaim>();
        if (root.TryGetProperty("claims", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                var kind = Enum.TryParse<ClaimKind>(GetString(c, "kind"), ignoreCase: true, out var k) ? k : ClaimKind.Other;
                claims.Add(new DeclaredClaim(
                    kind,
                    GetString(c, "text"),
                    GetNullableDouble(c, "number"),
                    c.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null,
                    GetNullableDouble(c, "durationYears")));
            }
        }

        var answers = new Dictionary<string, string>();
        if (root.TryGetProperty("answers", out var ans) && ans.ValueKind == JsonValueKind.Object)
            foreach (var p in ans.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) answers[p.Name] = p.Value.GetString()!;

        return new TailorDraft(resume, cover, claims, answers);
    }

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```")) return s;
        var first = s.IndexOf('\n');
        if (first >= 0) s = s[(first + 1)..];
        var fence = s.LastIndexOf("```", StringComparison.Ordinal);
        return fence >= 0 ? s[..fence] : s;
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double? GetNullableDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
