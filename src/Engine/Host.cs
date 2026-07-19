using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

/// <summary>
/// Runs a tick immediately, then every interval, until stopped. BCL-only (<see cref="PeriodicTimer"/>),
/// so it is verifiable offline; the production host can swap in Quartz for cron schedules and misfire
/// policy without changing the cycle. A throwing tick is swallowed so one bad cycle never kills the loop.
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

public sealed record DashboardControlResult(bool Performed, string Message);

public sealed record LocalDashboardActions(
    Func<CancellationToken, Task<DashboardControlResult>> DisconnectGmailAsync);

public sealed record DashboardEvidence(
    bool AuditOk,
    long? FirstBrokenSeq,
    string? Reason,
    int EventCount,
    IReadOnlyList<DashboardEvidenceEvent> RecentEvents);

public sealed record DashboardEvidenceEvent(
    long Seq,
    string Ts,
    string Actor,
    string Kind,
    string Entity,
    string EntityId);

public sealed record LocalDashboardEvidence(
    Func<CancellationToken, Task<DashboardEvidence>> LoadAsync)
{
    public static LocalDashboardEvidence FromStore(ISeekerStore store, int recentEventLimit = 12) =>
        new(async ct =>
        {
            var verification = await store.VerifyAuditAsync(ct).ConfigureAwait(false);
            var events = await store.GetEventsAsync(ct).ConfigureAwait(false);
            var recent = events
                .OrderByDescending(e => e.Seq)
                .Take(Math.Max(1, recentEventLimit))
                .OrderBy(e => e.Seq)
                .Select(e => new DashboardEvidenceEvent(e.Seq, e.Ts, e.Actor, e.Kind, e.Entity, e.EntityId))
                .ToArray();

            return new DashboardEvidence(
                verification.Ok,
                verification.FirstBrokenSeq,
                verification.Reason,
                events.Count,
                recent);
        });
}

/// <summary>
/// The free local dashboard (spec section 4: nobody is ever blind). It serves live counters at
/// <c>/status</c> and the HTML dashboard at <c>/</c>. Optional control actions are protected by a
/// per-process form token and loopback/origin checks.
/// </summary>
public sealed class LocalDashboard : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly EngineCounters _counters;
    private readonly LocalDashboardActions? _actions;
    private readonly LocalDashboardEvidence? _evidence;
    private readonly string _controlToken;
    private readonly int _port;
    private string? _lastActionMessage;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LocalDashboard(
        EngineCounters counters,
        int port = 7777,
        LocalDashboardActions? actions = null,
        LocalDashboardEvidence? evidence = null)
    {
        _counters = counters;
        _actions = actions;
        _evidence = evidence;
        _controlToken = NewControlToken();
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
            gmailDisconnectAvailable = _actions is not null,
            evidenceAvailable = _evidence is not null,
        });
    }

    private string Html()
    {
        var c = _counters;
        var notice = string.IsNullOrWhiteSpace(_lastActionMessage)
            ? ""
            : $@"<p class=""notice"">{WebUtility.HtmlEncode(_lastActionMessage)}</p>";
        var controls = _actions is null
            ? ""
            : $@"<h2>Controls</h2><form method=""post"" action=""/controls/gmail/disconnect"">
<input type=""hidden"" name=""token"" value=""{WebUtility.HtmlEncode(_controlToken)}"">
<button type=""submit"">Disconnect Gmail</button>
</form>";
        var evidence = _evidence is null
            ? ""
            : @"<h2>Evidence</h2><p><a href=""/evidence"">View audit-chain status and recent events</a></p>";

        return $@"<!doctype html><html><head><meta charset=""utf-8""><title>CareerSeeker</title>
<meta http-equiv=""refresh"" content=""5""><style>body{{font:14px system-ui;margin:2rem;max-width:40rem}}
h1{{font-size:1.1rem}}h2{{font-size:1rem;margin-top:1.5rem}}.g{{display:grid;grid-template-columns:1fr auto;gap:.3rem .8rem}}.n{{font-variant-numeric:tabular-nums;font-weight:600}}button{{font:inherit;padding:.45rem .7rem}}.notice{{padding:.6rem .7rem;background:#eef7ee;border-left:3px solid #22863a}}</style></head>
<body><h1>CareerSeeker - engine status</h1>{notice}<div class=""g"">
<div>Cycles</div><div class=""n"">{c.Cycles}</div>
<div>Discovered</div><div class=""n"">{c.Discovered}</div>
<div>Acted</div><div class=""n"">{c.Acted}</div>
<div>Drafted</div><div class=""n"">{c.Drafted}</div>
<div>Blocked (fabrication)</div><div class=""n"">{c.Blocked}</div>
<div>Rejected (engine)</div><div class=""n"">{c.Rejected}</div>
<div>Errors</div><div class=""n"">{c.Errors}</div>
</div><p>Last cycle: {c.LastCycleUtc?.ToString("u") ?? "-"}</p>{evidence}{controls}</body></html>";
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
                await HandleAsync(ctx, ct).ConfigureAwait(false);
            }
            catch { /* client went away */ }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/status")
        {
            await WriteAsync(ctx, "application/json", StatusJson(), ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/")
        {
            await WriteAsync(ctx, "text/html; charset=utf-8", Html(), ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/evidence")
        {
            await HandleEvidenceAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            path == "/controls/gmail/disconnect")
        {
            await HandleGmailDisconnectAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        ctx.Response.StatusCode = path is "/controls/gmail/disconnect" or "/evidence"
            ? (int)HttpStatusCode.MethodNotAllowed
            : (int)HttpStatusCode.NotFound;
        await WriteAsync(ctx, "text/plain; charset=utf-8", "Not found.", ct).ConfigureAwait(false);
    }

    public async Task<string> EvidenceJsonAsync(CancellationToken ct = default)
    {
        if (_evidence is null)
            return JsonSerializer.Serialize(new { available = false });

        var evidence = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            available = true,
            auditOk = evidence.AuditOk,
            firstBrokenSeq = evidence.FirstBrokenSeq,
            reason = evidence.Reason,
            eventCount = evidence.EventCount,
            recentEvents = evidence.RecentEvents,
        });
    }

    private async Task HandleEvidenceAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_evidence is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No dashboard evidence source is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        await WriteAsync(ctx, "application/json", await EvidenceJsonAsync(ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    private async Task HandleGmailDisconnectAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_actions is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No Gmail disconnect action is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        if (!RequestCameFromThisDashboard(ctx) || !await HasValidControlTokenAsync(ctx).ConfigureAwait(false))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Forbidden.", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var result = await _actions.DisconnectGmailAsync(ct).ConfigureAwait(false);
            _lastActionMessage = result.Message;
            RedirectHome(ctx);
        }
        catch (Exception ex)
        {
            _lastActionMessage = "Gmail disconnect did not complete cleanly: " + ex.Message;
            RedirectHome(ctx);
        }
    }

    private async Task<bool> HasValidControlTokenAsync(HttpListenerContext ctx)
    {
        if (ctx.Request.ContentLength64 is < 0 or > 4096) return false;
        var form = await ReadBodyAsync(ctx).ConfigureAwait(false);
        return ParseForm(form).TryGetValue("token", out var token) &&
               CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(token),
                   Encoding.UTF8.GetBytes(_controlToken));
    }

    private async Task<string> ReadBodyAsync(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? pair[..idx] : pair;
            var value = idx >= 0 ? pair[(idx + 1)..] : "";
            values[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return values;
    }

    private bool RequestCameFromThisDashboard(HttpListenerContext ctx)
    {
        if (ctx.Request.RemoteEndPoint is { Address: { } address } && !IPAddress.IsLoopback(address))
            return false;

        return LocalHostName(ctx.Request.UserHostName) &&
               LocalHeader(ctx.Request.Headers["Origin"]) &&
               LocalHeader(ctx.Request.Headers["Referer"]);
    }

    private bool LocalHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Port != -1 && uri.Port != _port) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
    }

    private static bool LocalHostName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var host = value.Split(':', 2)[0];
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    private static async Task WriteAsync(
        HttpListenerContext ctx,
        string contentType,
        string text,
        CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(text);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = body.Length;
        await ctx.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    private static void RedirectHome(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = (int)HttpStatusCode.SeeOther;
        ctx.Response.RedirectLocation = "/";
    }

    private static string NewControlToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

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

    public EngineHost(
        EngineCycle cycle,
        EngineCounters counters,
        TimeSpan interval,
        int dashboardPort = 7777,
        LocalDashboardActions? dashboardActions = null,
        LocalDashboardEvidence? dashboardEvidence = null)
    {
        Counters = counters;
        _scheduler = new PeriodicScheduler(cycle.TickAsync, interval);
        _dashboard = new LocalDashboard(counters, dashboardPort, dashboardActions, dashboardEvidence);
    }

    public void Start() { _dashboard.Start(); _scheduler.Start(); }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync().ConfigureAwait(false);
        await _dashboard.DisposeAsync().ConfigureAwait(false);
    }
}
