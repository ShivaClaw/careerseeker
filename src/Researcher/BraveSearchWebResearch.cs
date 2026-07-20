using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeekerSvc.Researcher;

public sealed record BraveSearchOptions(
    string ApiKey,
    string Country = "us",
    string SearchLanguage = "en",
    int MaxFetchBytes = 256 * 1024,
    int MaxDocumentChars = 12_000,
    Uri? Endpoint = null)
{
    public static readonly Uri DefaultEndpoint = new("https://api.search.brave.com/res/v1/web/search");
}

/// <summary>
/// Real <see cref="IWebResearch"/> implementation backed by Brave Search. Search snippets are used only
/// to pick URLs; CareerSeeker fetches each result page and grounds dossier facts against retrieved page
/// text, preserving the Researcher's grounded-or-dropped invariant.
/// </summary>
public sealed class BraveSearchWebResearch : IWebResearch
{
    private static readonly Regex Scriptish = new(
        @"<(script|style|noscript)\b[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly BraveSearchOptions _options;

    public BraveSearchWebResearch(HttpClient http, BraveSearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("Brave Search API key is required.", nameof(options));
        _http = http;
        _options = options;
    }

    public async Task<IReadOnlyList<ResearchDoc>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return Array.Empty<ResearchDoc>();

        var results = await SearchBraveAsync(query, maxResults, ct).ConfigureAwait(false);
        var docs = new List<ResearchDoc>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (docs.Count >= maxResults) break;
            if (!TryNormalizePublicHttpUrl(result.Url, out var url)) continue;
            if (!seen.Add(url)) continue;

            var text = await TryFetchTextAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;
            docs.Add(new ResearchDoc(url, result.Title, text));
        }

        return docs;
    }

    private async Task<IReadOnlyList<SearchResult>> SearchBraveAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        var endpoint = _options.Endpoint ?? BraveSearchOptions.DefaultEndpoint;
        var uri = endpoint.ToString() + "?" + string.Join("&", new[]
        {
            Pair("q", query),
            Pair("count", Math.Clamp(maxResults, 1, 20).ToString()),
            Pair("country", _options.Country),
            Pair("search_lang", _options.SearchLanguage),
        });

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("X-Subscription-Token", _options.ApiKey);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Brave Search API failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {Compact(body)}");

        return ParseSearchResults(body);
    }

    internal static IReadOnlyList<SearchResult> ParseSearchResults(string json)
    {
        var list = new List<SearchResult>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("web", out var web) ||
            !web.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in results.EnumerateArray())
        {
            var url = Str(item, "url");
            if (string.IsNullOrWhiteSpace(url)) continue;
            list.Add(new SearchResult(url, Str(item, "title")));
        }
        return list;
    }

    private async Task<string?> TryFetchTextAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("CareerSeekerAlpha/0.1");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode || !LooksTextual(resp.Content.Headers.ContentType?.MediaType))
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var bytes = await ReadLimitedAsync(stream, _options.MaxFetchBytes, ct).ConfigureAwait(false);
            var raw = Encoding.UTF8.GetString(bytes);
            var text = ToText(raw);
            return text.Length <= _options.MaxDocumentChars ? text : text[.._options.MaxDocumentChars];
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[8192];
        while (ms.Length < maxBytes)
        {
            var remaining = (int)Math.Min(buffer.Length, maxBytes - ms.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), ct).ConfigureAwait(false);
            if (read == 0) break;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    internal static string ToText(string raw)
    {
        var text = Scriptish.Replace(raw, " ");
        text = Tags.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return Space.Replace(text, " ").Trim();
    }

    private static bool TryNormalizePublicHttpUrl(string value, out string url)
    {
        url = "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (IsLocalHost(uri.Host)) return false;

        var builder = new UriBuilder(uri) { Fragment = "" };
        url = builder.Uri.ToString();
        return true;
    }

    private static bool IsLocalHost(string host)
    {
        host = host.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var ipHost = host.Trim('[', ']');
        return IPAddress.TryParse(ipHost, out var ip) &&
               (IPAddress.IsLoopback(ip) || !IsPubliclyRoutable(ip));
    }

    private static bool IsPubliclyRoutable(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return b[0] is not 0 and not 10 and not 127 and not >= 224 &&
                   !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
                   !(b[0] == 169 && b[1] == 254) &&
                   !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                   !(b[0] == 192 && b[1] == 168) &&
                   !(b[0] == 198 && b[1] >= 18 && b[1] <= 19);
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !ip.IsIPv6LinkLocal &&
                   !ip.IsIPv6SiteLocal &&
                   !ip.IsIPv6Multicast &&
                   (b[0] & 0xfe) != 0xfc;
        }

        return false;
    }

    private static bool LooksTextual(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType) ||
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

    private static string Pair(string key, string value) =>
        $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Compact(string body) =>
        body.Length <= 300 ? body : body[..300];

    internal sealed record SearchResult(string Url, string Title);
}
