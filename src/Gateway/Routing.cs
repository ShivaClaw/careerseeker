namespace SeekerSvc.Gateway;

/// <summary>
/// The routing table (spec §5.6). It holds two maps: stage → capability class, and class → an ordered
/// list of candidate <see cref="ModelSpec"/>s (primary first, cross-vendor failovers after). Resolution
/// applies mode and the pinned-stage rule. The table is data: swapping a model, repricing a tier, or
/// reordering vendors never touches pipeline code.
///
/// Two invariants are enforced here, not left to callers:
///  • a pinned stage always resolves to <see cref="CapabilityClass.StrongCloud"/>, ignoring mode;
///  • Local-max may prefer local for non-pinned stages but can never move a pinned stage off cloud.
/// </summary>
public sealed class RoutingTable
{
    private readonly IReadOnlyDictionary<Stage, CapabilityClass> _stageClass;
    private readonly IReadOnlyDictionary<CapabilityClass, IReadOnlyList<ModelSpec>> _candidates;

    public RoutingTable(
        IReadOnlyDictionary<Stage, CapabilityClass> stageClass,
        IReadOnlyDictionary<CapabilityClass, IReadOnlyList<ModelSpec>> candidates)
    {
        _stageClass = stageClass;
        _candidates = candidates;
    }

    /// <summary>The nominal (un-moded) class for a stage.</summary>
    public CapabilityClass NominalClass(Stage stage) => _stageClass[stage];

    /// <summary>
    /// The effective class to serve, after mode. Pinned stages ignore mode entirely. Local-max pushes
    /// non-pinned stages toward on-device (callers mark the result degraded when it drops below nominal).
    /// </summary>
    public CapabilityClass EffectiveClass(Stage stage, GatewayMode mode)
    {
        var nominal = NominalClass(stage);

        if (GatewayPolicy.IsPinned(stage))
        {
            // Hard floor. A pinned stage is strong cloud no matter the mode. If the table ever maps a
            // pinned stage below strong cloud, that is a table bug — surface it loudly.
            if (nominal != CapabilityClass.StrongCloud)
                throw new PinnedDowngradeException(stage, nominal);
            return CapabilityClass.StrongCloud;
        }

        return mode == GatewayMode.LocalMax ? CapabilityClass.OnDeviceSmall : nominal;
    }

    /// <summary>Ordered candidate models for a class (primary first, then failovers).</summary>
    public IReadOnlyList<ModelSpec> Candidates(CapabilityClass cls) =>
        _candidates.TryGetValue(cls, out var list) ? list : Array.Empty<ModelSpec>();

    /// <summary>
    /// Default table, July 2026 (pricing verified against provider docs on July 18, 2026). Every tier names a
    /// default <i>and</i> a cross-vendor failover, so a single vendor's outage, ban, or price change
    /// swaps the model for a class without touching pipeline code (spec §5.6: vendor plurality).
    /// Email identity, inference, and sync deliberately do not all terminate at one vendor.
    /// </summary>
    public static RoutingTable Default()
    {
        // Prices are USD / 1M tokens (input, output). Local = 0.
        var local      = new ModelSpec("local",     "llama-3.x-8b-instruct",  0m,    0m,    IsLocal: true);
        var flashLite  = new ModelSpec("google",    "gemini-2.5-flash-lite",  0.10m, 0.40m);
        var haiku      = new ModelSpec("anthropic", "claude-haiku-4-5",       1.00m, 5.00m);
        var flash      = new ModelSpec("google",    "gemini-2.5-flash",       0.30m, 2.50m);
        var sonnet     = new ModelSpec("anthropic", "claude-sonnet-4-6",      3.00m, 15.00m);
        var gem31pro   = new ModelSpec("google",    "gemini-3.1-pro-preview", 2.00m, 12.00m);

        var stageClass = new Dictionary<Stage, CapabilityClass>
        {
            [Stage.ClassifyDedupExtract] = CapabilityClass.OnDeviceSmall,
            [Stage.QuickScore]           = CapabilityClass.CheapCloud,
            [Stage.FullEvaluation]       = CapabilityClass.MidCloud,
            [Stage.Tailoring]            = CapabilityClass.StrongCloud,
            [Stage.VerifierEntailment]   = CapabilityClass.StrongCloud, // pinned
        };

        var candidates = new Dictionary<CapabilityClass, IReadOnlyList<ModelSpec>>
        {
            // local first; cloud fallback so on-device stages still run if no local server is present.
            [CapabilityClass.OnDeviceSmall] = new[] { local, flashLite },
            // cheapest cloud first, cross-vendor failover after.
            [CapabilityClass.CheapCloud]    = new[] { flashLite, haiku },
            [CapabilityClass.MidCloud]      = new[] { flash, haiku },
            // two strong vendors, neither alone load-bearing. The Gate draws from this list too.
            [CapabilityClass.StrongCloud]   = new[] { sonnet, gem31pro },
        };

        return new RoutingTable(stageClass, candidates);
    }
}
