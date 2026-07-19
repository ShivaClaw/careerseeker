using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Dispatcher;

/// <summary>Supplies a current OAuth access token. Production refreshes against the DPAPI-vaulted token.</summary>
public interface IAccessTokenSource
{
    Task<string> GetTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Real Gmail client over the REST API, using <c>gmail.compose</c> to create drafts. That scope can also
/// authorize sends, but this class intentionally contains no send call and exposes no send method. Custom
/// labels are deferred in L1 because they require broader Gmail access. Request/response shapes follow
/// users.drafts.create. Compile-verified in the sandbox; the live HTTP path runs in the real environment,
/// which holds the OAuth token and network egress the sandbox does not.
/// </summary>
public sealed class GmailDraftClient : IGmailDraftClient
{
    private const string Base = "https://gmail.googleapis.com/gmail/v1/users/me";
    private readonly HttpClient _http;
    private readonly IAccessTokenSource _tokens;

    public GmailDraftClient(HttpClient http, IAccessTokenSource tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task PreflightDraftAccessAsync(CancellationToken ct = default)
    {
        using var req = await GmailHttp.AuthedAsync(
            _tokens,
            HttpMethod.Get,
            $"{Base}/drafts?maxResults=1",
            null,
            ct).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        GmailHttp.EnsureSuccess(resp, json);
    }

    public async Task<string> GetProfileEmailAsync(CancellationToken ct = default)
    {
        using var req = await GmailHttp.AuthedAsync(_tokens, HttpMethod.Get, $"{Base}/profile", null, ct)
            .ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        GmailHttp.EnsureSuccess(resp, json);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("emailAddress").GetString()
               ?? throw new InvalidOperationException("Gmail profile response had no emailAddress.");
    }

    public async Task<string> CreateDraftAsync(
        string rawRfc822Base64Url, IReadOnlyList<string> labelIds, CancellationToken ct = default)
    {
        var message = labelIds.Count == 0
            ? new Dictionary<string, object> { ["raw"] = rawRfc822Base64Url }
            : new Dictionary<string, object> { ["raw"] = rawRfc822Base64Url, ["labelIds"] = labelIds };
        var body = new { message };
        using var req = await GmailHttp.AuthedAsync(_tokens, HttpMethod.Post, $"{Base}/drafts", body, ct).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        GmailHttp.EnsureSuccess(resp, json);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("Gmail draft response had no id.");
    }
}

static class GmailHttp
{
    public static async Task<HttpRequestMessage> AuthedAsync(
        IAccessTokenSource tokens,
        HttpMethod method,
        string url,
        object? body,
        CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokens.GetTokenAsync(ct).ConfigureAwait(false));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    public static void EnsureSuccess(HttpResponseMessage resp, string body)
    {
        if (resp.IsSuccessStatusCode) return;
        var compact = string.IsNullOrWhiteSpace(body) ? "" : " Body: " + body.Replace("\r", "").Replace("\n", " ");
        throw new HttpRequestException(
            $"Gmail API {(int)resp.StatusCode} {resp.ReasonPhrase}.{compact}",
            null,
            resp.StatusCode);
    }
}
