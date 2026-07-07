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
/// Real Gmail client over the REST API, using only the <c>gmail.compose</c> scope: create drafts and
/// ensure labels. There is intentionally no send call here — sending requires <c>gmail.send</c>, a scope
/// an L1 install never requests (spec §8.2 incremental scopes). Request/response shapes follow
/// users.drafts.create and users.labels. Compile-verified in the sandbox; the live HTTP path runs in the
/// real environment, which holds the OAuth token and network egress the sandbox does not.
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

    public async Task<string> CreateDraftAsync(
        string rawRfc822Base64Url, IReadOnlyList<string> labelIds, CancellationToken ct = default)
    {
        var body = new { message = new { raw = rawRfc822Base64Url, labelIds } };
        using var req = await Authed(HttpMethod.Post, $"{Base}/drafts", body, ct).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("Gmail draft response had no id.");
    }

    public async Task<string> EnsureLabelAsync(string labelPath, CancellationToken ct = default)
    {
        // look for an existing label by name
        using (var listReq = await Authed(HttpMethod.Get, $"{Base}/labels", null, ct).ConfigureAwait(false))
        using (var listResp = await _http.SendAsync(listReq, ct).ConfigureAwait(false))
        {
            var json = await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            listResp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("labels", out var labels))
                foreach (var l in labels.EnumerateArray())
                    if (l.TryGetProperty("name", out var n) && n.GetString() == labelPath)
                        return l.GetProperty("id").GetString()!;
        }

        // create it
        var body = new { name = labelPath, labelListVisibility = "labelShow", messageListVisibility = "show" };
        using var createReq = await Authed(HttpMethod.Post, $"{Base}/labels", body, ct).ConfigureAwait(false);
        using var createResp = await _http.SendAsync(createReq, ct).ConfigureAwait(false);
        var cjson = await createResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        createResp.EnsureSuccessStatusCode();
        using var cdoc = JsonDocument.Parse(cjson);
        return cdoc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<HttpRequestMessage> Authed(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _tokens.GetTokenAsync(ct).ConfigureAwait(false));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }
}
