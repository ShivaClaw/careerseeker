using SeekerSvc.Dispatcher;
using SeekerSvc.Gateway;
using SeekerSvc.Pipeline;
using SeekerSvc.Tailor;
using SeekerSvc.Verifier;

var envFile = Arg("--secrets") ?? Path.Combine("secrets", "env.secrets");
var keyVaultPath = Arg("--key-vault") ?? Path.Combine(".appdata", "secrets", "byok-keys.dpapi");

int passed = 0, failed = 0;
void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $"  -- {detail}")}");
    }
}

Console.WriteLine("=== CareerSeeker BYOK live provider smoke ===\n");

var keys = LoadKeys();
var providersPresent = keys.ProvidersPresent();
Check("BYOK providers available", providersPresent.Contains("anthropic") && providersPresent.Contains("google"),
    "providers: " + string.Join(", ", providersPresent));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

if (keys.HasKey("anthropic"))
{
    try
    {
        var provider = new AnthropicProvider(http, keys);
        var result = await provider.CompleteAsync(new ProviderCall(
            "claude-sonnet-4-6",
            new[] { LlmMessage.User("Return exactly: ok") },
            MaxOutputTokens: 16,
            Temperature: 0));
        Check("Anthropic live completion returned text and usage",
            result.Text.Trim().Length > 0 && result.Usage.Total > 0,
            $"usage={result.Usage.Total}");
    }
    catch (Exception ex)
    {
        Check("Anthropic live completion returned text and usage", false, ex.GetType().Name + ": " + ex.Message);
    }
}

if (keys.HasKey("google"))
{
    try
    {
        var provider = new GoogleProvider(http, keys);
        var result = await provider.CompleteAsync(new ProviderCall(
            "gemini-3.1-pro-preview",
            new[] { LlmMessage.User("Say ok in plain text.") },
            MaxOutputTokens: 128,
            Temperature: 0));
        Check("Gemini live completion returned text and usage",
            result.Text.Trim().Length > 0 && result.Usage.Total > 0,
            $"usage={result.Usage.Total}");
    }
    catch (Exception ex)
    {
        Check("Gemini live completion returned text and usage", false, ex.GetType().Name + ": " + ex.Message);
    }
}

try
{
    var gateway = new LlmGateway(
        RoutingTable.Default(),
        GatewayMode.Byok,
        new BudgetMeter(1000m),
        new ILlmProvider[] { new AnthropicProvider(http, keys), new GoogleProvider(http, keys) });

    using var tailorTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var tailor = new GatewayTailorModel(gateway);
    var profile = new[]
    {
        new SourceClaim("title", ClaimKind.Title, "Senior Software Engineer", Confidence.Verified),
        new SourceClaim("skill-go", ClaimKind.Skill, "Go", Confidence.Verified),
        new SourceClaim("skill-dist", ClaimKind.Skill, "distributed systems", Confidence.Verified),
        new SourceClaim("skill-reliable", ClaimKind.Skill, "reliable", Confidence.Verified),
        new SourceClaim("skill-team", ClaimKind.Skill, "team", Confidence.Verified),
        new SourceClaim("summary", ClaimKind.Other, "Senior Software Engineer experienced in Go and distributed systems", Confidence.Verified),
        new SourceClaim("cover", ClaimKind.Other, "I have built reliable distributed systems in Go and would bring that experience to your team", Confidence.Verified),
    };
    var draft = await tailor.GenerateAsync(new TailorModelRequest(
        new PipelineJob(1, "Senior Software Engineer", "Acme"),
        profile,
        Array.Empty<string>(),
        StyleCard.Default,
        Array.Empty<string>()),
        tailorTimeout.Token);
    Check("Gateway Tailor live call returned parseable draft",
        !string.IsNullOrWhiteSpace(draft.ResumeText)
        && !string.IsNullOrWhiteSpace(draft.CoverText)
        && draft.CoverText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= StyleCard.Default.MaxCoverWords);

    var matcher = new GatewaySemanticMatcher(gateway);
    var result = await matcher.EntailsAsync(
        "The candidate has built reliable distributed systems in Go.",
        "Built reliable distributed systems in Go.");
    Check("Gateway Gate live entailment is supported",
        result.Entailed && !result.Unavailable,
        $"entailed={result.Entailed} unavailable={result.Unavailable} detail={result.Detail}");
    Check("Gateway live accounting recorded Gate spend",
        gateway.Accounting.ByStage.ContainsKey(Stage.VerifierEntailment)
        && gateway.Budget.SpentUsd > 0);

    var tailored = Decomposer.FromDraft(draft);
    var gate = await FabricationGate.VerifyAsync(
        profile,
        tailored,
        matcher,
        options: GateVerificationOptions.BoundedSemantic(3));
    Check("Gateway Tailor live draft passes bounded Gate",
        gate.Passed,
        string.Join(" | ", gate.Violations.Take(3).Select(v => v.Claim.Text)));
}
catch (Exception ex)
{
    Check("Gateway Tailor live call returned parseable draft", false, ex.GetType().Name + ": " + ex.Message);
    Check("Gateway Gate live entailment is supported", false, ex.GetType().Name + ": " + ex.Message);
    Check("Gateway live accounting recorded Gate spend", false);
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

EnvironmentApiKeySource LoadKeys()
{
    var vault = new DpapiSecretVault(keyVaultPath);
    var vaulted = vault.Load();
    return vaulted.Count > 0 ? new EnvironmentApiKeySource(vaulted) : EnvironmentApiKeySource.Load(envFile);
}

string? Arg(string name)
{
    for (var i = 0; i + 1 < args.Length; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}
