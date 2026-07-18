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

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
