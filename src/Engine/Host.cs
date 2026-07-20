using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeekerSvc.Pipeline;
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
    Func<CancellationToken, Task<DashboardControlResult>>? DisconnectGmailAsync = null,
    Func<long, string, CancellationToken, Task<DashboardControlResult>>? ControlApplicationAsync = null,
    Func<CancellationToken, Task<DashboardControlResult>>? ExportAuditAsync = null,
    Func<CancellationToken, Task<DashboardControlResult>>? ExportAlphaPackageAsync = null);

public sealed record DashboardEvidence(
    bool AuditOk,
    long? FirstBrokenSeq,
    string? Reason,
    int EventCount,
    IReadOnlyList<DashboardEvidenceEvent> RecentEvents,
    IReadOnlyList<ApplicationSummaryRow> RecentApplications,
    IReadOnlyList<JobSummaryRow> RecentJobs);

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
    public static LocalDashboardEvidence FromStore(
        ISeekerStore store,
        int recentEventLimit = 12,
        int recentApplicationLimit = 25,
        int recentJobLimit = 25) =>
        new(async ct =>
        {
            var verification = await store.VerifyAuditAsync(ct).ConfigureAwait(false);
            var events = await store.GetEventsAsync(ct).ConfigureAwait(false);
            var applications = await store.GetRecentApplicationsAsync(recentApplicationLimit, ct).ConfigureAwait(false);
            var jobs = await store.GetRecentJobsAsync(recentJobLimit, ct).ConfigureAwait(false);
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
                recent,
                applications,
                jobs);
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
    private const string DashboardCss = """
:root{color-scheme:light;--bg:#f7f7f4;--panel:#fff;--ink:#1c211f;--muted:#65706b;--line:#d9ddd8;--accent:#0f766e;--accent-ink:#064e3b;--danger:#b42318;--warn:#9a3412}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
a{color:#075985;text-decoration:none}a:hover{text-decoration:underline}
.top{position:sticky;top:0;z-index:1;background:rgba(247,247,244,.94);border-bottom:1px solid var(--line);backdrop-filter:blur(8px)}
.top-inner{max-width:80rem;margin:0 auto;padding:1rem 1.25rem;display:flex;align-items:center;justify-content:space-between;gap:1rem;flex-wrap:wrap}
.brand{font-weight:700;letter-spacing:0}.sub{color:var(--muted);font-size:.86rem}.nav{display:flex;gap:.35rem;flex-wrap:wrap}
.nav a{color:var(--ink);padding:.4rem .55rem;border-radius:.35rem}.nav a.active{background:#e7f4f1;color:var(--accent-ink);font-weight:650}
.shell{max-width:80rem;margin:0 auto;padding:1.25rem}.hero{display:flex;align-items:flex-end;justify-content:space-between;gap:1rem;margin:.4rem 0 1rem}
h1{font-size:1.35rem;line-height:1.2;margin:0}h2{font-size:1rem;margin:1.2rem 0 .55rem}.muted{color:var(--muted)}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(9rem,1fr));gap:.75rem}.card{background:var(--panel);border:1px solid var(--line);border-radius:.45rem;padding:.85rem}.label{color:var(--muted);font-size:.78rem;text-transform:uppercase}.n{font-variant-numeric:tabular-nums}.big{font-size:1.45rem;font-weight:720}
.notice{padding:.65rem .75rem;background:#eef7ee;border-left:3px solid #22863a;margin:.75rem 0}.actions{display:flex;gap:.5rem;flex-wrap:wrap}form{display:inline}
button{font:inherit;font-weight:650;padding:.45rem .65rem;border:1px solid var(--line);border-radius:.35rem;background:#fff;color:var(--ink);cursor:pointer}button:hover{border-color:#8fa19a}.danger button{color:var(--danger)}
.links{display:flex;gap:.55rem;flex-wrap:wrap}.table-wrap{overflow:auto;background:var(--panel);border:1px solid var(--line);border-radius:.45rem}
table{border-collapse:collapse;width:100%;min-width:58rem}th,td{text-align:left;border-bottom:1px solid var(--line);padding:.5rem .6rem;vertical-align:top}th{font-size:.78rem;text-transform:uppercase;color:var(--muted);background:#fbfbf9}tr:last-child td{border-bottom:0}
.state{font-weight:700}.ok{color:#166534;font-weight:700}.bad{color:var(--danger);font-weight:700}.warn{font-weight:700;color:var(--warn)}.pill{display:inline-block;border:1px solid var(--line);border-radius:999px;padding:.15rem .45rem;background:#fff}
@media (max-width:720px){.hero{display:block}.top-inner{align-items:flex-start}.shell{padding:1rem}table{min-width:46rem}}
""";

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
            gmailDisconnectAvailable = _actions?.DisconnectGmailAsync is not null,
            applicationControlAvailable = _actions?.ControlApplicationAsync is not null,
            auditExportAvailable = _actions?.ExportAuditAsync is not null,
            alphaPackageExportAvailable = _actions?.ExportAlphaPackageAsync is not null,
            evidenceAvailable = _evidence is not null,
            applicationsAvailable = _evidence is not null,
            jobsAvailable = _evidence is not null,
        });
    }

    private string Html()
    {
        var c = _counters;
        var notice = string.IsNullOrWhiteSpace(_lastActionMessage)
            ? ""
            : $@"<p class=""notice"">{WebUtility.HtmlEncode(_lastActionMessage)}</p>";
        var controls = ControlsHtml();
        var evidence = _evidence is null
            ? ""
            : @"<h2>Evidence</h2><div class=""links""><a href=""/jobs"">Recent jobs</a><a href=""/applications"">Recent applications</a><a href=""/evidence.html"">audit-chain status</a><a href=""/evidence"">audit JSON</a></div>";

        var body = $@"<section class=""hero""><div><h1>CareerSeeker engine status</h1><div class=""muted"">Last cycle: {WebUtility.HtmlEncode(c.LastCycleUtc?.ToString("u") ?? "-")}</div></div><span class=""pill"">running</span></section>
{notice}<section class=""cards"">
{MetricCard("Cycles", c.Cycles)}
{MetricCard("Discovered", c.Discovered)}
{MetricCard("Acted", c.Acted)}
{MetricCard("Drafted", c.Drafted)}
{MetricCard("Blocked (fabrication)", c.Blocked)}
{MetricCard("Rejected (engine)", c.Rejected)}
{MetricCard("Errors", c.Errors)}
        </section>{evidence}{controls}";
        return PageHtml("CareerSeeker", "status", body);
    }

    private static string MetricCard(string label, long value) =>
        $@"<div class=""card""><div class=""label"">{WebUtility.HtmlEncode(label)}</div><div class=""big n"">{value}</div></div>";

    private static string PageHtml(string title, string active, string body)
    {
        return $@"<!doctype html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1""><title>{WebUtility.HtmlEncode(title)}</title>
<meta http-equiv=""refresh"" content=""5""><style>{DashboardCss}</style></head><body>
<header class=""top""><div class=""top-inner""><div><div class=""brand"">CareerSeeker</div><div class=""sub"">Local alpha dashboard</div></div><nav class=""nav"">{NavLink("/", "Status", active == "status")}{NavLink("/jobs", "Jobs", active == "jobs")}{NavLink("/applications", "Applications", active == "applications")}{NavLink("/evidence.html", "Evidence", active == "evidence")}</nav></div></header>
<main class=""shell"">{body}</main></body></html>";
    }

    private static string NavLink(string href, string label, bool active) =>
        $@"<a class=""{(active ? "active" : "")}"" href=""{href}"">{WebUtility.HtmlEncode(label)}</a>";

    private string ControlsHtml()
    {
        var forms = new List<string>();
        if (_actions?.ExportAuditAsync is not null)
        {
            forms.Add($@"<form method=""post"" action=""/controls/audit/export"">
<input type=""hidden"" name=""token"" value=""{WebUtility.HtmlEncode(_controlToken)}"">
<button type=""submit"">Export Audit JSON</button>
</form>");
        }

        if (_actions?.ExportAlphaPackageAsync is not null)
        {
            forms.Add($@"<form method=""post"" action=""/controls/package/export"">
<input type=""hidden"" name=""token"" value=""{WebUtility.HtmlEncode(_controlToken)}"">
<button type=""submit"">Export Alpha Package</button>
</form>");
        }

        if (_actions?.DisconnectGmailAsync is not null)
        {
            forms.Add($@"<form method=""post"" action=""/controls/gmail/disconnect"">
<input type=""hidden"" name=""token"" value=""{WebUtility.HtmlEncode(_controlToken)}"">
<button type=""submit"">Disconnect Gmail</button>
</form>");
        }

        return forms.Count == 0 ? "" : $@"<h2>Controls</h2><div class=""actions"">{string.Concat(forms)}</div>";
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

        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/evidence.html")
        {
            await HandleEvidencePageAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/applications")
        {
            await HandleApplicationsAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/jobs")
        {
            await HandleJobsAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith("/documents/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDocumentAsync(ctx, path, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            path == "/controls/gmail/disconnect")
        {
            await HandleGmailDisconnectAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            path == "/controls/audit/export")
        {
            await HandleAuditExportAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            path == "/controls/package/export")
        {
            await HandlePackageExportAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            path == "/controls/application")
        {
            await HandleApplicationControlAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        ctx.Response.StatusCode = path is "/controls/gmail/disconnect" or "/controls/audit/export" or "/controls/package/export" or "/controls/application" or "/evidence" or "/applications" or "/jobs"
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
            recentApplications = evidence.RecentApplications,
            recentJobs = evidence.RecentJobs,
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

    public async Task<string> EvidenceHtmlAsync(CancellationToken ct = default)
    {
        if (_evidence is null)
            return "<!doctype html><html><body><p>No dashboard evidence source is configured.</p></body></html>";

        var evidence = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        var auditText = evidence.AuditOk
            ? "intact"
            : $"broken at {evidence.FirstBrokenSeq?.ToString() ?? "unknown"}";
        var auditDetail = evidence.AuditOk
            ? "Hash chain verified"
            : WebUtility.HtmlEncode(evidence.Reason ?? "Audit chain verification failed");
        var auditClass = evidence.AuditOk ? "ok" : "bad";
        var eventRows = evidence.RecentEvents.Count == 0
            ? @"<tr><td colspan=""6"">No audit events yet.</td></tr>"
            : string.Concat(evidence.RecentEvents.Select(EvidenceEventRowHtml));

        var body = $@"<section class=""hero""><div><h1>Audit evidence</h1><div class=""muted"">Local metadata only; raw event payloads stay out of this page.</div></div><a href=""/"">Back to status</a></section>
<section class=""cards"">
<div class=""card""><div class=""label"">Audit chain</div><div class=""big {auditClass}"">{WebUtility.HtmlEncode(auditText)}</div><div class=""muted"">{auditDetail}</div></div>
{MetricCard("Events", evidence.EventCount)}
{MetricCard("Applications", evidence.RecentApplications.Count)}
{MetricCard("Jobs", evidence.RecentJobs.Count)}
</section>
<h2>Recent audit events</h2>
<div class=""table-wrap""><table><thead><tr><th>Seq</th><th>Time</th><th>Actor</th><th>Kind</th><th>Entity</th><th>Id</th></tr></thead>
<tbody>{eventRows}</tbody></table></div>
<h2>Evidence views</h2><div class=""links""><a href=""/applications"">Recent applications</a><a href=""/jobs"">Recent jobs</a><a href=""/evidence"">audit JSON</a></div>";
        return PageHtml("CareerSeeker Evidence", "evidence", body);
    }

    private static string EvidenceEventRowHtml(DashboardEvidenceEvent row) =>
        $@"<tr><td class=""n"">{row.Seq}</td><td class=""n"">{WebUtility.HtmlEncode(row.Ts)}</td><td>{WebUtility.HtmlEncode(row.Actor)}</td><td>{WebUtility.HtmlEncode(row.Kind)}</td><td>{WebUtility.HtmlEncode(row.Entity)}</td><td>{WebUtility.HtmlEncode(row.EntityId)}</td></tr>";

    private async Task HandleEvidencePageAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_evidence is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No dashboard evidence source is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        await WriteAsync(ctx, "text/html; charset=utf-8", await EvidenceHtmlAsync(ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    public async Task<string> ApplicationsHtmlAsync(CancellationToken ct = default)
    {
        if (_evidence is null)
            return "<!doctype html><html><body><p>No application evidence source is configured.</p></body></html>";

        var evidence = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        var rows = evidence.RecentApplications.Count == 0
            ? @"<tr><td colspan=""8"">No applications yet.</td></tr>"
            : string.Concat(evidence.RecentApplications.Select(row => ApplicationRowHtml(
                row,
                _actions?.ControlApplicationAsync is not null,
                _controlToken)));

        var body = $@"<section class=""hero""><div><h1>Recent applications</h1><div class=""muted"">{evidence.RecentApplications.Count} applications shown</div></div><a href=""/"">Back to status</a></section>
<div class=""table-wrap""><table><thead><tr><th>State</th><th>Job</th><th>Company</th><th>Score</th><th>Draft</th><th>Updated</th><th>Links</th><th>Controls</th></tr></thead>
<tbody>{rows}</tbody></table></div>";
        return PageHtml("CareerSeeker Applications", "applications", body);
    }

    private static string ApplicationRowHtml(ApplicationSummaryRow row, bool canControl, string token)
    {
        var job = WebUtility.HtmlEncode(row.JobTitle);
        var company = WebUtility.HtmlEncode(row.CompanyName ?? row.CompanyDomain ?? "-");
        var score = row.Total is null
            ? "-"
            : WebUtility.HtmlEncode($"{row.Total:0.0} total / {row.Fit:0.0} fit / {row.Legitimacy:0.0} legitimacy");
        var draft = string.IsNullOrWhiteSpace(row.DraftStatus)
            ? "-"
            : WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(row.DraftExternalRef)
                ? row.DraftStatus
                : $"{row.DraftStatus} ({row.DraftExternalRef})");
        var updated = WebUtility.HtmlEncode(row.UpdatedAt);
        var links = LinksHtml(row, token);
        var controls = canControl ? ApplicationControlsHtml(row, token) : "-";
        return $@"<tr><td class=""state"">{WebUtility.HtmlEncode(row.State)}</td><td>{job}</td><td>{company}</td><td class=""n"">{score}</td><td>{draft}</td><td class=""n"">{updated}</td><td><div class=""links"">{links}</div></td><td>{controls}</td></tr>";
    }

    private static string ApplicationControlsHtml(ApplicationSummaryRow row, string token)
    {
        var buttons = new List<string>();
        if (row.State == AppState.PAUSED.ToString())
            buttons.Add(ControlButton(row.ApplicationId, "resume", token, "Resume"));
        else if (Enum.TryParse<AppState>(row.State, out var state) && Lifecycle.IsActive(state))
            buttons.Add(ControlButton(row.ApplicationId, "pause", token, "Pause"));

        if (row.State != AppState.USER_KILLED.ToString())
            buttons.Add(ControlButton(row.ApplicationId, "kill", token, "Kill", danger: true));

        return buttons.Count == 0 ? "-" : string.Join(" ", buttons);
    }

    private static string ControlButton(long applicationId, string action, string token, string label, bool danger = false) =>
        $@"<form class=""{(danger ? "danger" : "")}"" method=""post"" action=""/controls/application""><input type=""hidden"" name=""token"" value=""{WebUtility.HtmlEncode(token)}""><input type=""hidden"" name=""applicationId"" value=""{applicationId}""><input type=""hidden"" name=""action"" value=""{WebUtility.HtmlEncode(action)}""><button type=""submit"">{WebUtility.HtmlEncode(label)}</button></form>";

    private static string LinksHtml(ApplicationSummaryRow row, string token)
    {
        var links = new List<string>();
        if (Uri.TryCreate(row.JobUrl, UriKind.Absolute, out var jobUri) && jobUri.Scheme is "http" or "https")
            links.Add($@"<a href=""{WebUtility.HtmlEncode(jobUri.ToString())}"">job</a>");
        if (Uri.TryCreate(row.ApplyUrl, UriKind.Absolute, out var applyUri) && applyUri.Scheme is "http" or "https" or "mailto")
            links.Add($@"<a href=""{WebUtility.HtmlEncode(applyUri.ToString())}"">apply</a>");
        AddFileLink(links, row.ApplicationId, row.ResumePath, "resume", token);
        AddFileLink(links, row.ApplicationId, row.CoverPath, "cover", token);
        return links.Count == 0 ? "-" : string.Join(" ", links);
    }

    private static void AddFileLink(List<string> links, long applicationId, string? path, string label, string token)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
        links.Add($@"<a href=""/documents/{applicationId}/{WebUtility.UrlEncode(label)}?token={WebUtility.UrlEncode(token)}"">{label}</a>");
    }

    private async Task HandleApplicationsAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_evidence is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No application evidence source is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        await WriteAsync(ctx, "text/html; charset=utf-8", await ApplicationsHtmlAsync(ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    public async Task<string> JobsHtmlAsync(CancellationToken ct = default)
    {
        if (_evidence is null)
            return "<!doctype html><html><body><p>No job evidence source is configured.</p></body></html>";

        var evidence = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        var rows = evidence.RecentJobs.Count == 0
            ? @"<tr><td colspan=""8"">No jobs yet.</td></tr>"
            : string.Concat(evidence.RecentJobs.Select(JobRowHtml));

        var body = $@"<section class=""hero""><div><h1>Recent jobs</h1><div class=""muted"">{evidence.RecentJobs.Count} jobs shown</div></div><a href=""/"">Back to status</a></section>
<div class=""table-wrap""><table><thead><tr><th>Job</th><th>Company</th><th>Source</th><th>Remote</th><th>Comp</th><th>Updated</th><th>Flags</th><th>Links</th></tr></thead>
<tbody>{rows}</tbody></table></div>";
        return PageHtml("CareerSeeker Jobs", "jobs", body);
    }

    private static string JobRowHtml(JobSummaryRow row)
    {
        var job = WebUtility.HtmlEncode(row.Title);
        var company = WebUtility.HtmlEncode(row.CompanyName ?? row.CompanyDomain ?? "-");
        var source = WebUtility.HtmlEncode($"{row.Source}:{row.ExternalId}");
        var remote = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(row.Location)
            ? row.Remote
            : $"{row.Remote} / {row.Location}");
        var comp = row.CompMin is null && row.CompMax is null
            ? "-"
            : WebUtility.HtmlEncode($"{row.CompCurrency ?? "?"} {row.CompMin?.ToString("0") ?? "?"}-{row.CompMax?.ToString("0") ?? "?"} {row.CompInterval ?? ""} {row.CompSource ?? ""}".Trim());
        var flags = row.Injected
            ? $@"<span class=""warn"">injection</span>{(string.IsNullOrWhiteSpace(row.InjectionSignals) ? "" : " " + WebUtility.HtmlEncode(row.InjectionSignals))}"
            : "-";
        var updated = WebUtility.HtmlEncode(row.LastVerified);
        var links = JobLinksHtml(row);
        return $@"<tr><td>{job}</td><td>{company}</td><td>{source}</td><td>{remote}</td><td class=""n"">{comp}</td><td class=""n"">{updated}</td><td>{flags}</td><td><div class=""links"">{links}</div></td></tr>";
    }

    private static string JobLinksHtml(JobSummaryRow row)
    {
        var links = new List<string>();
        if (Uri.TryCreate(row.JobUrl, UriKind.Absolute, out var jobUri) && jobUri.Scheme is "http" or "https")
            links.Add($@"<a href=""{WebUtility.HtmlEncode(jobUri.ToString())}"">job</a>");
        if (Uri.TryCreate(row.ApplyUrl, UriKind.Absolute, out var applyUri) && applyUri.Scheme is "http" or "https" or "mailto")
            links.Add($@"<a href=""{WebUtility.HtmlEncode(applyUri.ToString())}"">apply</a>");
        return links.Count == 0 ? "-" : string.Join(" ", links);
    }

    private async Task HandleJobsAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_evidence is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No job evidence source is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        await WriteAsync(ctx, "text/html; charset=utf-8", await JobsHtmlAsync(ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    private async Task HandleDocumentAsync(HttpListenerContext ctx, string path, CancellationToken ct)
    {
        if (_evidence is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No application evidence source is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 ||
            !segments[0].Equals("documents", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(segments[1], out var applicationId))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Document not found.", ct).ConfigureAwait(false);
            return;
        }

        var kind = Uri.UnescapeDataString(segments[2]);
        if (!HasValidControlToken(ctx.Request.QueryString["token"]))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Forbidden.", ct).ConfigureAwait(false);
            return;
        }

        var evidence = await _evidence.LoadAsync(ct).ConfigureAwait(false);
        var row = evidence.RecentApplications.FirstOrDefault(a => a.ApplicationId == applicationId);
        var documentPath = kind.Equals("resume", StringComparison.OrdinalIgnoreCase)
            ? row?.ResumePath
            : kind.Equals("cover", StringComparison.OrdinalIgnoreCase)
                ? row?.CoverPath
                : null;

        if (string.IsNullOrWhiteSpace(documentPath) ||
            !Path.IsPathRooted(documentPath) ||
            !File.Exists(documentPath))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Document not found.", ct).ConfigureAwait(false);
            return;
        }

        var fileName = Path.GetFileName(documentPath).Replace("\"", "", StringComparison.Ordinal);
        ctx.Response.ContentType = Path.GetExtension(documentPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";
        ctx.Response.Headers["Content-Disposition"] = $@"inline; filename=""{fileName}""";
        await using var file = File.OpenRead(documentPath);
        ctx.Response.ContentLength64 = file.Length;
        await file.CopyToAsync(ctx.Response.OutputStream, ct).ConfigureAwait(false);
    }

    private async Task HandleGmailDisconnectAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_actions?.DisconnectGmailAsync is null)
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

    private async Task HandleAuditExportAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_actions?.ExportAuditAsync is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No audit export action is configured.", ct)
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
            var result = await _actions.ExportAuditAsync(ct).ConfigureAwait(false);
            _lastActionMessage = result.Message;
            RedirectHome(ctx);
        }
        catch (Exception ex)
        {
            _lastActionMessage = "Audit export did not complete cleanly: " + ex.Message;
            RedirectHome(ctx);
        }
    }

    private async Task HandlePackageExportAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_actions?.ExportAlphaPackageAsync is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No alpha package export action is configured.", ct)
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
            var result = await _actions.ExportAlphaPackageAsync(ct).ConfigureAwait(false);
            _lastActionMessage = result.Message;
            RedirectHome(ctx);
        }
        catch (Exception ex)
        {
            _lastActionMessage = "Alpha package export did not complete cleanly: " + ex.Message;
            RedirectHome(ctx);
        }
    }

    private async Task HandleApplicationControlAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_actions?.ControlApplicationAsync is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "No application control action is configured.", ct)
                .ConfigureAwait(false);
            return;
        }

        if (!RequestCameFromThisDashboard(ctx))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Forbidden.", ct).ConfigureAwait(false);
            return;
        }

        if (!IsDashboardFormPost(ctx.Request) || ctx.Request.ContentLength64 is < 0 or > 4096)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Forbidden.", ct).ConfigureAwait(false);
            return;
        }

        var form = ParseForm(await ReadBodyAsync(ctx).ConfigureAwait(false));
        if (!HasValidControlToken(form))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Forbidden.", ct).ConfigureAwait(false);
            return;
        }

        if (!long.TryParse(form.GetValueOrDefault("applicationId"), out var applicationId) ||
            !form.TryGetValue("action", out var action))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(ctx, "text/plain; charset=utf-8", "Missing application control fields.", ct)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            var result = await _actions.ControlApplicationAsync(applicationId, action, ct).ConfigureAwait(false);
            _lastActionMessage = result.Message;
            RedirectApplications(ctx);
        }
        catch (Exception ex)
        {
            _lastActionMessage = "Application control did not complete cleanly: " + ex.Message;
            RedirectApplications(ctx);
        }
    }

    private async Task<bool> HasValidControlTokenAsync(HttpListenerContext ctx)
    {
        if (!IsDashboardFormPost(ctx.Request)) return false;
        if (ctx.Request.ContentLength64 is < 0 or > 4096) return false;
        var form = await ReadBodyAsync(ctx).ConfigureAwait(false);
        return HasValidControlToken(ParseForm(form));
    }

    private static bool IsDashboardFormPost(HttpListenerRequest request)
    {
        if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)) return false;
        var contentType = request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasValidControlToken(Dictionary<string, string> form)
    {
        return form.TryGetValue("token", out var token) && HasValidControlToken(token);
    }

    private bool HasValidControlToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(_controlToken));

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

    private static void RedirectApplications(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = (int)HttpStatusCode.SeeOther;
        ctx.Response.RedirectLocation = "/applications";
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
