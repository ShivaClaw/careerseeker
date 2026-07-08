namespace SeekerSvc.Gateway;

/// <summary>
/// The pipeline stages that consume inference (spec §5.6 routing table). Each maps to a capability
/// <see cref="CapabilityClass"/>; the mapping — not the concrete model — is the contract.
/// </summary>
public enum Stage
{
    /// <summary>classify / dedup / extract — on-device small model, ~free.</summary>
    ClassifyDedupExtract,

    /// <summary>quick score on title+snippet — cheap cloud.</summary>
    QuickScore,

    /// <summary>full evaluation + dossier delta — mid cloud, multiple calls.</summary>
    FullEvaluation,

    /// <summary>tailoring + correspondence — strong cloud (voice card + dossier hook).</summary>
    Tailoring,

    /// <summary>
    /// The Fabrication Gate's NLI entailment check (spec §5.5/§5.6). Strong cloud, <b>pinned</b>:
    /// never throttled, never downgraded by budget or mode. This is the one safety-critical judgment
    /// in the system and the "fabrication-gate escapes: zero, ever" KPI depends on it. See
    /// <see cref="GatewayPolicy.PinnedStages"/>.
    /// </summary>
    VerifierEntailment,
}

/// <summary>Capability tiers. A stage routes to a class; a class resolves to an ordered model list.</summary>
public enum CapabilityClass
{
    OnDeviceSmall,
    CheapCloud,
    MidCloud,
    StrongCloud,
}

/// <summary>
/// Inference mode chosen at onboarding (spec §5.6). Managed is the single visible default; BYOK and
/// Local-max sit behind an "Advanced" expander. Mode influences routing for <i>non-pinned</i> stages
/// only — it can never pull a pinned stage off strong cloud.
/// </summary>
public enum GatewayMode
{
    /// <summary>CareerSeeker-managed inference (metered). The default.</summary>
    Managed,

    /// <summary>User-supplied provider key; calls go direct. $0 to us.</summary>
    Byok,

    /// <summary>Everything that can run on-device does; cloud reserved for what must be cloud (incl. the Gate).</summary>
    LocalMax,
}

/// <summary>
/// Cross-cutting policy constants that are structural, not configurable. <see cref="PinnedStages"/> is
/// the Gateway's load-bearing safety invariant — the analogue of the Fabrication Gate's no-bypass and
/// the Store's hash chain. Nothing in this file is reachable from user config.
/// </summary>
public static class GatewayPolicy
{
    /// <summary>
    /// Stages that may never be throttled or downgraded. Membership is hardcoded; there is no setter,
    /// no config key, and no constructor that grows this set. Routing, budget, and mode logic all read
    /// it and structurally exempt its members.
    /// </summary>
    public static readonly IReadOnlySet<Stage> PinnedStages = new HashSet<Stage> { Stage.VerifierEntailment };

    /// <summary>True if a stage is exempt from throttle/downgrade by construction.</summary>
    public static bool IsPinned(Stage stage) => PinnedStages.Contains(stage);

    /// <summary>
    /// Throttle priority: lower throttles first as budget tightens. The spec's order is
    /// "discovery/scoring breadth first, then dossier freshness — never the Gate." Pinned stages are
    /// reported as <see cref="int.MaxValue"/> and are never throttled regardless.
    /// </summary>
    public static int ThrottlePriority(Stage stage) => stage switch
    {
        _ when IsPinned(stage)         => int.MaxValue, // never throttled
        Stage.ClassifyDedupExtract     => 0,            // discovery breadth — cut first
        Stage.QuickScore               => 1,            // scoring breadth
        Stage.FullEvaluation           => 2,            // dossier freshness/depth
        Stage.Tailoring                => 3,            // last non-pinned to be cut
        _                              => 3,
    };
}
