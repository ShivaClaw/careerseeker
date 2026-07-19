using System.Net;
using SeekerSvc.Gateway;

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

async Task<(bool Threw, Exception? Error)> Throws(Func<Task> work)
{
    try
    {
        await work();
        return (false, null);
    }
    catch (Exception ex)
    {
        return (true, ex);
    }
}

Console.WriteLine("=== CareerSeeker Gateway pinned-Gate harness ===\n");

var routes = RoutingTable.Default();
LlmRequest Gate() => new(Stage.VerifierEntailment, new[] { LlmMessage.User("claim + profile evidence") }, MaxOutputTokens: 256);
LlmRequest Req(Stage stage) => new(stage, new[] { LlmMessage.User("job + profile") }, MaxOutputTokens: 256);
ILlmProvider[] Cloud() => new ILlmProvider[]
{
    new FakeProvider("anthropic"),
    new FakeProvider("google"),
    new FakeProvider("local", isLocal: true),
};
LlmGateway Gw(GatewayMode mode, BudgetMeter budget, IEnumerable<ILlmProvider> providers) =>
    new(routes, mode, budget, providers);

Console.WriteLine("[ membership and class floor ]");
Check("VerifierEntailment is pinned", GatewayPolicy.IsPinned(Stage.VerifierEntailment));
Check("pinned throttle priority is int.MaxValue",
    GatewayPolicy.ThrottlePriority(Stage.VerifierEntailment) == int.MaxValue);
Check("Gate nominal class is StrongCloud", routes.NominalClass(Stage.VerifierEntailment) == CapabilityClass.StrongCloud);
foreach (var mode in new[] { GatewayMode.Managed, GatewayMode.Byok, GatewayMode.LocalMax })
    Check($"Gate effective class stays StrongCloud in {mode}",
        routes.EffectiveClass(Stage.VerifierEntailment, mode) == CapabilityClass.StrongCloud);

Console.WriteLine("\n[ throttle exemption ]");
ThrottleDecision At(decimal fractionOfCap, Stage stage)
{
    var budget = new BudgetMeter(100m);
    budget.Record(fractionOfCap * 100m);
    return budget.Evaluate(stage);
}
Check("Gate not throttled at 100% of cap", !At(1.00m, Stage.VerifierEntailment).Throttled);
Check("Gate not throttled at 105% of cap", !At(1.05m, Stage.VerifierEntailment).Throttled);
Check("Gate not throttled at 500% of cap", !At(5.00m, Stage.VerifierEntailment).Throttled);
Check("contrast: Tailoring is throttled at 105% of cap", At(1.05m, Stage.Tailoring).Throttled);
Check("contrast: QuickScore is throttled at 105% of cap", At(1.05m, Stage.QuickScore).Throttled);

Console.WriteLine("\n[ end-to-end over-cap behavior ]");
{
    var budget = new BudgetMeter(10m);
    budget.Record(50m);
    var gw = Gw(GatewayMode.LocalMax, budget, Cloud());
    var resp = await gw.CompleteAsync(Gate());
    Check("Gate still completes at 500% of cap", resp.ModelId.Length > 0);
    Check("Gate served by cloud provider", resp.Provider is "anthropic" or "google");
    Check("Gate response is not degraded", !resp.WasDegraded);
    Check("Gate over-cap spend is still recorded", budget.SpentUsd > budget.CapUsd);

    var (threw, ex) = await Throws(() => gw.CompleteAsync(Req(Stage.QuickScore)));
    Check("contrast: QuickScore throws ThrottledException at same budget", threw && ex is ThrottledException);
}

Console.WriteLine("\n[ fail-closed downgrade cases ]");
{
    var gw = Gw(GatewayMode.LocalMax, new BudgetMeter(100m),
        new ILlmProvider[] { new FakeProvider("local", isLocal: true) });
    var (threw, ex) = await Throws(() => gw.CompleteAsync(Gate()));
    Check("Gate fails closed with no strong provider", threw && ex is NoProviderException);
}
{
    var gw = Gw(GatewayMode.Managed, new BudgetMeter(100m),
        new ILlmProvider[] { new FakeProvider("anthropic", isLocal: true) });
    var (threw, ex) = await Throws(() => gw.CompleteAsync(Gate()));
    Check("Gate refuses a local-backed strong slot", threw && ex is PinnedDowngradeException);
}
{
    var gw = Gw(GatewayMode.LocalMax, new BudgetMeter(100m), Cloud());
    var resp = await gw.CompleteAsync(Gate());
    Check("LocalMax still serves Gate by cloud", resp.Provider is "anthropic" or "google");
}
{
    var gw = Gw(GatewayMode.Managed, new BudgetMeter(100m), new ILlmProvider[]
    {
        new FakeProvider("anthropic", fail: true),
        new FakeProvider("google"),
        new FakeProvider("local", isLocal: true),
    });
    var resp = await gw.CompleteAsync(Gate());
    Check("Gate fails over to second strong vendor", resp.Provider == "google" && !resp.WasDegraded);
}
{
    var gw = Gw(GatewayMode.Managed, new BudgetMeter(100m), new ILlmProvider[]
    {
        new FakeProvider("anthropic", fail: true),
        new FakeProvider("google", fail: true),
        new FakeProvider("local", isLocal: true),
    });
    var (threw, ex) = await Throws(() => gw.CompleteAsync(Gate()));
    Check("all strong vendors down throws AggregateException", threw && ex is AggregateException);
}

Console.WriteLine("\n[ BYOK key source ]");
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "CareerSeeker-GatewayHarness-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var envFile = Path.Combine(tempRoot, "env.secrets");
        await File.WriteAllTextAsync(envFile, """
        # local alpha keys
        ANTHROPIC_API_KEY=anthropic-test-key
        GEMINI_API_KEY=gemini-test-key
        IGNORED_KEY=not-a-provider
        """);

        var keys = EnvironmentApiKeySource.Load(envFile);
        Check("BYOK env file maps Anthropic key",
            keys.HasKey("anthropic") && keys.GetKey("anthropic") == "anthropic-test-key");
        Check("BYOK env file maps Gemini key to google provider",
            keys.HasKey("google") && keys.GetKey("google") == "gemini-test-key");
        Check("BYOK key source reports provider ids only",
            keys.ProvidersPresent().SequenceEqual(new[] { "anthropic", "google" }));
        var missing = false;
        try { _ = keys.GetKey("openai"); }
        catch (InvalidOperationException ex)
        {
            missing = ex.Message.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal)
                      && ex.Message.Contains("GEMINI_API_KEY", StringComparison.Ordinal);
        }
        Check("BYOK missing provider error names expected variables", missing);
    }
    finally
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}

Console.WriteLine("\n[ BYOK provider HTTP shapes ]");
{
    var handler = new CapturingHandler(HttpStatusCode.OK,
        """{"content":[{"type":"text","text":"anthropic-ok"}],"usage":{"input_tokens":12,"output_tokens":3}}""");
    var provider = new AnthropicProvider(new HttpClient(handler),
        new StaticKeySource(new Dictionary<string, string> { ["anthropic"] = "anthropic-test-key" }));
    var result = await provider.CompleteAsync(new ProviderCall(
        "claude-sonnet-4-6",
        new[] { LlmMessage.System("system rules"), LlmMessage.User("candidate facts") },
        128,
        0.2));

    Check("Anthropic provider sends Messages API request",
        handler.LastMethod == HttpMethod.Post
        && handler.LastRequestUri == "https://api.anthropic.com/v1/messages"
        && handler.LastHeaders.TryGetValue("x-api-key", out var key)
        && key == "anthropic-test-key"
        && handler.LastHeaders.ContainsKey("anthropic-version")
        && handler.LastBody.Contains("\"model\":\"claude-sonnet-4-6\"", StringComparison.Ordinal)
        && handler.LastBody.Contains("\"system\":\"system rules\"", StringComparison.Ordinal));
    Check("Anthropic provider parses text and usage",
        result.Text == "anthropic-ok" && result.Usage == new LlmUsage(12, 3));
}
{
    var handler = new CapturingHandler(HttpStatusCode.OK,
        """{"candidates":[{"content":{"parts":[{"text":"google-ok"}]}}],"usageMetadata":{"promptTokenCount":7,"candidatesTokenCount":2}}""");
    var provider = new GoogleProvider(new HttpClient(handler),
        new StaticKeySource(new Dictionary<string, string> { ["google"] = "gemini-test-key" }));
    var result = await provider.CompleteAsync(new ProviderCall(
        "gemini-3.1-pro-preview",
        new[] { LlmMessage.System("system rules"), LlmMessage.User("candidate facts") },
        64,
        0));

    Check("Google provider sends generateContent request",
        handler.LastMethod == HttpMethod.Post
        && handler.LastRequestUri == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent"
        && handler.LastHeaders.TryGetValue("x-goog-api-key", out var key)
        && key == "gemini-test-key"
        && handler.LastBody.Contains("\"systemInstruction\"", StringComparison.Ordinal)
        && handler.LastBody.Contains("\"generationConfig\"", StringComparison.Ordinal));
    Check("Google provider parses text and usage",
        result.Text == "google-ok" && result.Usage == new LlmUsage(7, 2));
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

sealed class CapturingHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public HttpMethod? LastMethod { get; private set; }
    public string? LastRequestUri { get; private set; }
    public Dictionary<string, string> LastHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string LastBody { get; private set; } = "";

    public CapturingHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastMethod = request.Method;
        LastRequestUri = request.RequestUri?.GetLeftPart(UriPartial.Path);
        foreach (var header in request.Headers)
            LastHeaders[header.Key] = string.Join(",", header.Value);
        LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) };
    }
}
