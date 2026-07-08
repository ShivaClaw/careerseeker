namespace SeekerSvc.Gateway;

/// <summary>
/// A deterministic, offline provider used by the test harness and as a stand-in for a local model.
/// It can be configured to fail (to exercise cross-vendor failover) and reports usage by a simple,
/// repeatable token estimate (~4 chars/token) so cost math is checkable without a network.
/// </summary>
public sealed class FakeProvider : ILlmProvider
{
    private readonly Func<ProviderCall, string> _respond;
    private readonly bool _fail;

    public string Name { get; }
    public bool IsLocal { get; }
    public int CallCount { get; private set; }

    public FakeProvider(
        string name,
        bool isLocal = false,
        bool fail = false,
        Func<ProviderCall, string>? respond = null)
    {
        Name = name;
        IsLocal = isLocal;
        _fail = fail;
        _respond = respond ?? (call => $"[{name}] ok");
    }

    public Task<ProviderResult> CompleteAsync(ProviderCall call, CancellationToken ct = default)
    {
        CallCount++;
        if (_fail)
            throw new InvalidOperationException($"{Name} is unavailable (simulated outage).");

        var text = _respond(call);
        var inTok = EstimateTokens(call.Messages.Sum(m => m.Content.Length));
        var outTok = EstimateTokens(text.Length);
        return Task.FromResult(new ProviderResult(text, new LlmUsage(inTok, outTok)));
    }

    private static int EstimateTokens(int chars) => Math.Max(1, chars / 4);
}
