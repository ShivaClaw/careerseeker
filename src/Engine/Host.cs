using System.Net;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Engine;

/// <summary>
/// Runs a tick immediately, then every interval, until stopped. BCL-only (<see cref="PeriodicTimer"/>),
/// so it's verifiable offline; the production host swaps in Quartz for cron schedules and misfire policy
/// without changing the cycle. A throwing tick is swallowed so one bad cycle never kills the loop.
/// </summary>
public sealed class PeriodicScheduler : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _tick;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PeriodicScheduler(Func<CancellationToken, Task> tick, TimeSpan interval)
    {
        _tick = tick;
        _interval = interval;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            await SafeTick(ct).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await SafeTick(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* stopping */ }
    }

    private async Task SafeTick(CancellationToken ct)
    {
        try { await _tick(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { /* a cycle's own counters record the error; the loop survives */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch { } }
        _cts?.Dispose();
    }
}

/// <summary>
/// The free local dashboard (spec §4: "nobody is ever blind"). A tiny <see cref="HttpListener"/> on
/// localhost serving the live counters as HTML at <c>/</c> and JSON at <c>/status</c> — no framework, no
/// auth surface, loopback only. The paid Android app is the same data remoted; this proves the engine is
/// never a black box even on the free tier.
/// </summary>
public sealed class LocalDashboard : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly EngineCounters _counters;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LocalDashboard(EngineCounters counters, int port = 7777)
    {
        _counters = counters;
        _port = port;
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public string StatusJson()
    {
        var c = _counters;
        return JsonSerializer.Serialize(new
        {
            status = "running",
            cycles = c.Cycles,
            lastCycleUtc = c.LastCycleUtc,
            discovered = c.Discovered,
            acted = c.Acted,
            drafted = c.Drafted,
            blocked = c.Blocked,
            rejected = c.Rejected,
            errors = c.Errors,
        });
    }

    private string Html()
    {
        var c = _counters;
        return $@"<!doctype html><html><head><meta charset=""utf-8""><title>CareerSeeker</title>
<meta http-equiv=""refresh"" content=""5""><style>body{{font:14px system-ui;margin:2rem;max-width:40rem}}
h1{{font-size:1.1rem}}.g{{display:grid;grid-template-columns:1fr auto;gap:.3rem .8rem}}.n{{font-variant-numeric:tabular-nums;font-weight:600}}</style></head>
<body><h1>CareerSeeker — engine status</h1><div class=""g"">
<div>Cycles</div><div class=""n"">{c.Cycles}</div>
<div>Discovered</div><div class=""n"">{c.Discovered}</div>
<div>Acted</div><div class=""n"">{c.Acted}</div>
<div>Drafted</div><div class=""n"">{c.Drafted}</div>
<div>Blocked (fabrication)</div><div class=""n"">{c.Blocked}</div>
<div>Rejected (engine)</div><div class=""n"">{c.Rejected}</div>
<div>Errors</div><div class=""n"">{c.Errors}</div>
</div><p>Last cycle: {c.LastCycleUtc?.ToString("u") ?? "—"}</p></body></html>";
    }

    public void Start()
    {
        if (_loop is not null) return;
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch when (ct.IsCancellationRequested) { break; }
            catch { continue; }

            try
            {
                var isStatus = ctx.Request.Url?.AbsolutePath == "/status";
                var body = Encoding.UTF8.GetBytes(isStatus ? StatusJson() : Html());
                ctx.Response.ContentType = isStatus ? "application/json" : "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
            }
            catch { /* client went away */ }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch { } }
        _cts?.Dispose();
        try { _listener.Close(); } catch { }
    }
}

/// <summary>
/// Composition root for the engine: owns the counters, runs <see cref="EngineCycle"/> on a
/// <see cref="PeriodicScheduler"/>, and serves the <see cref="LocalDashboard"/>. In production a Windows
/// Service wraps this (start on boot, stop on shutdown); the wiring is identical to the vertical slice,
/// just driven on a timer instead of once.
/// </summary>
public sealed class EngineHost : IAsyncDisposable
{
    private readonly PeriodicScheduler _scheduler;
    private readonly LocalDashboard _dashboard;
    public EngineCounters Counters { get; }

    public EngineHost(EngineCycle cycle, EngineCounters counters, TimeSpan interval, int dashboardPort = 7777)
    {
        Counters = counters;
        _scheduler = new PeriodicScheduler(cycle.TickAsync, interval);
        _dashboard = new LocalDashboard(counters, dashboardPort);
    }

    public void Start() { _dashboard.Start(); _scheduler.Start(); }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync().ConfigureAwait(false);
        await _dashboard.DisposeAsync().ConfigureAwait(false);
    }
}
