using System.Collections.Concurrent;
namespace SeekerSvc.Gateway;

/// <summary>A rolled-up tally for one bucket (a stage or a model).</summary>
public sealed record CostLine(long Calls, long InputTokens, long OutputTokens, decimal CostUsd)
{
    public static readonly CostLine Empty = new(0, 0, 0, 0m);
    public CostLine Add(LlmUsage u, decimal cost) =>
        new(Calls + 1, InputTokens + u.InputTokens, OutputTokens + u.OutputTokens, CostUsd + cost);
}

/// <summary>
/// Per-stage and per-model spend, surfaced in analytics ("this week: $1.84 / 312 calls", spec §5.6).
/// Pure bookkeeping — no I/O. The Store persists snapshots of this for the dashboard.
/// </summary>
public sealed class Accounting
{
    private readonly ConcurrentDictionary<Stage, CostLine> _byStage = new();
    private readonly ConcurrentDictionary<string, CostLine> _byModel = new();
    private CostLine _total = CostLine.Empty;

    public void Record(Stage stage, string modelId, LlmUsage usage, decimal cost)
    {
        _byStage.AddOrUpdate(stage, _ => CostLine.Empty.Add(usage, cost), (_, s) => s.Add(usage, cost));
        _byModel.AddOrUpdate(modelId, _ => CostLine.Empty.Add(usage, cost), (_, m) => m.Add(usage, cost));
        _total = _total.Add(usage, cost);
    }

    public CostLine Total => _total;
    public IReadOnlyDictionary<Stage, CostLine> ByStage => _byStage;
    public IReadOnlyDictionary<string, CostLine> ByModel => _byModel;

    /// <summary>One-line analytics summary.</summary>
    public string Summary() => $"${_total.CostUsd:0.00} / {_total.Calls} calls / {_total.InputTokens + _total.OutputTokens} tokens";
}
