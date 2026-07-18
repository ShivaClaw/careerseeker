namespace SeekerSvc.Gateway;

/// <summary>The throttle verdict for one stage at the current budget pressure.</summary>
public sealed record ThrottleDecision(bool Throttled, string Reason)
{
    public static readonly ThrottleDecision Proceed = new(false, "ok");
}

/// <summary>
/// Tracks spend against a monthly cap and decides which stages to throttle as the cap is approached
/// (spec §5.6). Throttle is breadth-first: discovery and scoring are cut before dossier freshness, and
/// the Gate is never cut. Crossing the cap throttles every non-pinned stage but the Gate still runs —
/// you do not ship a half-verified application to save a few cents, so a pinned stage records its spend
/// even when that pushes the meter past the cap.
/// </summary>
public sealed class BudgetMeter
{
    private readonly object _sync = new();
    private readonly decimal _capUsd;
    private readonly decimal _bandWarn;   // start cutting breadth
    private readonly decimal _bandTight;  // also cut depth/dossier
    private decimal _spent;

    public BudgetMeter(decimal monthlyCapUsd, decimal warnFraction = 0.80m, decimal tightFraction = 0.95m)
    {
        _capUsd = monthlyCapUsd <= 0 ? decimal.MaxValue : monthlyCapUsd;
        _bandWarn = warnFraction;
        _bandTight = tightFraction;
    }

    public decimal CapUsd => _capUsd;
    public decimal SpentUsd { get { lock (_sync) return _spent; } }
    public decimal RemainingUsd { get { lock (_sync) return Math.Max(0m, _capUsd - _spent); } }
    public decimal Fraction
    {
        get
        {
            lock (_sync)
                return _capUsd == decimal.MaxValue ? 0m : _spent / _capUsd;
        }
    }

    /// <summary>
    /// The current throttle ceiling: stages whose <see cref="GatewayPolicy.ThrottlePriority"/> is at or
    /// below this are throttled. Rises with budget pressure. -1 means "throttle nothing".
    /// </summary>
    public int ThrottleCeiling()
    {
        var f = Fraction;
        return ThrottleCeiling(f);
    }

    private int ThrottleCeiling(decimal f)
    {
        if (f < _bandWarn) return -1;        // healthy: nothing throttled
        if (f < _bandTight) return 1;        // warn: cut discovery (0) + scoring breadth (1)
        if (f < 1.0m) return 2;              // tight: also cut dossier/full-eval depth (2)
        return 3;                            // over cap: cut all non-pinned (incl. new tailoring)
    }

    /// <summary>
    /// Decide whether a stage may proceed. Pinned stages always proceed (structural exemption); they are
    /// not even consulted against the ceiling.
    /// </summary>
    public ThrottleDecision Evaluate(Stage stage)
    {
        if (GatewayPolicy.IsPinned(stage))
            return ThrottleDecision.Proceed;

        var fraction = Fraction;
        var ceiling = ThrottleCeiling(fraction);
        if (GatewayPolicy.ThrottlePriority(stage) <= ceiling)
            return new ThrottleDecision(true,
                $"budget at {fraction:P0} of cap; throttling {stage} (breadth-first, Gate exempt)");

        return ThrottleDecision.Proceed;
    }

    /// <summary>Record spend. Always applied, including for pinned stages that ran past the cap.</summary>
    public void Record(decimal costUsd)
    {
        lock (_sync)
            _spent += costUsd;
    }

    /// <summary>Reset for a new billing period.</summary>
    public void ResetPeriod()
    {
        lock (_sync)
            _spent = 0m;
    }
}
