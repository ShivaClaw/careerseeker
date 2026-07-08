namespace SeekerSvc.Gateway;

/// <summary>One chat message. Role is "system" | "user" | "assistant".</summary>
public sealed record LlmMessage(string Role, string Content)
{
    public static LlmMessage System(string c) => new("system", c);
    public static LlmMessage User(string c) => new("user", c);
    public static LlmMessage Assistant(string c) => new("assistant", c);
}

/// <summary>
/// A stage-tagged inference request. The <see cref="Stage"/> drives routing; the Gateway — not the
/// caller — decides which provider and model serve it. Callers never name a model, which is what keeps
/// the routing table the single source of truth and the pinned-stage guarantee enforceable.
/// </summary>
public sealed record LlmRequest(
    Stage Stage,
    IReadOnlyList<LlmMessage> Messages,
    int MaxOutputTokens = 1024,
    double? Temperature = null,
    string? PurposeTag = null);

/// <summary>Token counts for one completion.</summary>
public sealed record LlmUsage(int InputTokens, int OutputTokens)
{
    public int Total => InputTokens + OutputTokens;
    public static readonly LlmUsage Zero = new(0, 0);
}

/// <summary>What the Gateway returns: the text plus full provenance for the audit log and cost meter.</summary>
public sealed record LlmResponse(
    string Text,
    string Provider,
    string ModelId,
    Stage Stage,
    LlmUsage Usage,
    decimal CostUsd,
    bool Degraded = false)
{
    /// <summary>True when a non-pinned stage was served below its nominal class (e.g. Local-max). Never true for the Gate.</summary>
    public bool WasDegraded => Degraded;
}

/// <summary>
/// One routable model: which provider serves it and what it costs. Pricing lives here so the meter is
/// exact and so swapping a model is a one-line table edit (spec §5.6: "the class is the contract, the
/// model is swappable"). Prices are USD per 1,000,000 tokens.
/// </summary>
public sealed record ModelSpec(
    string Provider,
    string ModelId,
    decimal InputPerMTok,
    decimal OutputPerMTok,
    bool IsLocal = false)
{
    /// <summary>Exact cost of a completion under this model's pricing.</summary>
    public decimal CostOf(LlmUsage u) =>
        (u.InputTokens / 1_000_000m) * InputPerMTok + (u.OutputTokens / 1_000_000m) * OutputPerMTok;
}

/// <summary>A concrete call to a single provider/model. The provider knows nothing about routing or cost.</summary>
public sealed record ProviderCall(
    string ModelId,
    IReadOnlyList<LlmMessage> Messages,
    int MaxOutputTokens,
    double? Temperature);

/// <summary>What a provider returns: rendered text and the usage it reports (or that we estimate).</summary>
public sealed record ProviderResult(string Text, LlmUsage Usage);

/// <summary>
/// A vendor adapter. One per provider (Anthropic, Google, a local llama.cpp server, …). The provider
/// executes a call against one model and reports usage. It does not route, price, throttle, or fail
/// over — those belong to the Gateway, so vendor plurality is a table edit, not a code change.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Stable id matching <see cref="ModelSpec.Provider"/> (e.g. "anthropic", "google", "local").</summary>
    string Name { get; }

    /// <summary>On-device provider (zero marginal cost; permitted to serve the local class). Never the Gate.</summary>
    bool IsLocal { get; }

    Task<ProviderResult> CompleteAsync(ProviderCall call, CancellationToken ct = default);
}

/// <summary>Raised when a non-pinned stage is deferred because the budget throttle is engaged.</summary>
public sealed class ThrottledException : Exception
{
    public Stage Stage { get; }
    public ThrottledException(Stage stage, string reason)
        : base($"Stage {stage} throttled: {reason}") => Stage = stage;
}

/// <summary>Raised when no registered provider can serve a stage's class (after failover is exhausted).</summary>
public sealed class NoProviderException : Exception
{
    public NoProviderException(CapabilityClass cls, Stage stage)
        : base($"No registered provider can serve {cls} for stage {stage}.") { }
}

/// <summary>
/// Raised if the routing layer is ever asked to serve a pinned stage below strong cloud. This should be
/// unreachable — it exists so a future table edit that violated the invariant fails loud, not silent.
/// </summary>
public sealed class PinnedDowngradeException : Exception
{
    public PinnedDowngradeException(Stage stage, CapabilityClass attempted)
        : base($"Refused to route pinned stage {stage} at {attempted}; pinned stages require strong cloud.") { }
}
