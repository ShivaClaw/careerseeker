namespace SeekerSvc.Gateway;

/// <summary>
/// The LLM Gateway (spec §5.6). Every inference call in the engine goes through here. It:
///   1. resolves the stage to a capability class (applying mode, honoring the pinned-stage floor),
///   2. applies the budget throttle — <i>except</i> for pinned stages, which are never consulted,
///   3. walks the class's candidate models, failing over across vendors on provider error,
///   4. prices the call exactly from the routing table and records it in the budget + accounting.
///
/// The Gateway is the only component that names a model. Callers name a <see cref="Stage"/>. That
/// indirection is what makes the pinned-Gate guarantee enforceable in one place and vendor swaps a
/// table edit. Pure orchestration: no HTTP here — that lives in the <see cref="ILlmProvider"/>s.
/// </summary>
public sealed class LlmGateway
{
    private readonly RoutingTable _routes;
    private readonly GatewayMode _mode;
    private readonly BudgetMeter _budget;
    private readonly Accounting _accounting;
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;

    public LlmGateway(
        RoutingTable routes,
        GatewayMode mode,
        BudgetMeter budget,
        IEnumerable<ILlmProvider> providers,
        Accounting? accounting = null)
    {
        _routes = routes;
        _mode = mode;
        _budget = budget;
        _accounting = accounting ?? new Accounting();
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public GatewayMode Mode => _mode;
    public BudgetMeter Budget => _budget;
    public Accounting Accounting => _accounting;

    /// <summary>Run one inference request through routing, throttle, failover, and accounting.</summary>
    public async Task<LlmResponse> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var pinned = GatewayPolicy.IsPinned(req.Stage);
        var nominal = _routes.NominalClass(req.Stage);
        var effective = _routes.EffectiveClass(req.Stage, _mode);

        // 2. Throttle — pinned stages are not consulted at all (structural exemption).
        if (!pinned)
        {
            var decision = _budget.Evaluate(req.Stage);
            if (decision.Throttled)
                throw new ThrottledException(req.Stage, decision.Reason);
        }

        // A non-pinned stage served below its nominal class (e.g. Local-max) is flagged degraded.
        var degraded = !pinned && effective < nominal;

        // 3. Candidate walk with cross-vendor failover.
        var candidates = _routes.Candidates(effective);
        Exception? last = null;
        var attempted = false;

        foreach (var spec in candidates)
        {
            if (!_providers.TryGetValue(spec.Provider, out var provider))
                continue; // provider not registered in this mode/install — try the next vendor

            // A local provider may never serve a pinned stage, belt-and-suspenders with the class floor.
            if (pinned && provider.IsLocal)
                throw new PinnedDowngradeException(req.Stage, CapabilityClass.OnDeviceSmall);

            attempted = true;
            try
            {
                var result = await provider.CompleteAsync(
                    new ProviderCall(spec.ModelId, req.Messages, req.MaxOutputTokens, req.Temperature),
                    ct).ConfigureAwait(false);

                var cost = spec.CostOf(result.Usage);
                _budget.Record(cost);                    // pinned spend recorded even if over cap
                _accounting.Record(req.Stage, spec.ModelId, result.Usage, cost);

                return new LlmResponse(result.Text, spec.Provider, spec.ModelId, req.Stage, result.Usage, cost, degraded);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex; // vendor outage / ban / transient — fail over to the next candidate
            }
        }

        throw attempted
            ? new AggregateException($"All providers for {effective} (stage {req.Stage}) failed.", last!)
            : new NoProviderException(effective, req.Stage);
    }
}
