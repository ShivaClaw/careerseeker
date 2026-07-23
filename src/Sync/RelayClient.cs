using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>One envelope pulled from the relay, still ciphertext.</summary>
public sealed record PulledEnvelope(JsonElement Raw);

/// <summary>
/// The engine's HTTPS client for the blind relay (Sync-Protocol.md §2). Push/pull only;
/// the WebSocket live feed is a P2 concern. Every call carries the bearer for the pairing,
/// and the client never sees or holds key material — it moves ciphertext the codec sealed.
/// </summary>
public sealed class RelayClient(HttpClient http, string relayBaseUrl, string pairing)
{
    private string Base(string route) => $"{relayBaseUrl.TrimEnd('/')}/v1/{pairing}/{route}";

    /// <summary>Bootstrap the channel (§5.2.1). Idempotent-ish: 201 first time, 409 after.</summary>
    public async Task<bool> CreateAsync(string bearer, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Base("create"));
        req.Headers.Add("Authorization", $"Bearer {bearer}");
        using var res = await http.SendAsync(req, ct);
        return res.StatusCode is HttpStatusCode.Created;
    }

    /// <summary>Rotate the provisional bearer to the final one (§5.2.3). One-way.</summary>
    public async Task<bool> RotateTokenAsync(string currentBearer, string newTokenSha256Hex, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Base("create"))
        {
            Content = JsonContent.Create(new { rotate_to = newTokenSha256Hex }),
        };
        req.Headers.Add("Authorization", $"Bearer {currentBearer}");
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    /// <summary>Collect the phone's pairing completion (one-shot; the relay deletes it on read).</summary>
    public async Task<string?> TakeCompletionAsync(string bearer, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Base("pair"));
        req.Headers.Add("Authorization", $"Bearer {bearer}");
        using var res = await http.SendAsync(req, ct);
        return res.StatusCode == HttpStatusCode.OK ? await res.Content.ReadAsStringAsync(ct) : null;
    }

    /// <summary>Append one sealed envelope. The body IS the envelope JSON.</summary>
    public async Task<bool> PushAsync(string bearer, string envelopeJson, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Base("push"))
        {
            Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Authorization", $"Bearer {bearer}");
        using var res = await http.SendAsync(req, ct);
        return res.StatusCode is HttpStatusCode.Created;
    }

    /// <summary>Fetch envelopes for one direction with seq &gt; since. Returns them and the latest seq.</summary>
    public async Task<(IReadOnlyList<JsonElement> Envelopes, long Latest)> PullAsync(
        string bearer, string dir, long since, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Base($"pull?dir={dir}&since={since}"));
        req.Headers.Add("Authorization", $"Bearer {bearer}");
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = doc.RootElement.GetProperty("envelopes").EnumerateArray().Select(e => e.Clone()).ToList();
        var latest = doc.RootElement.GetProperty("latest").GetInt64();
        return (list, latest);
    }

    /// <summary>Unpair: purge the Durable Object. After this the pairing no longer authorizes anyone.</summary>
    public async Task<bool> UnpairAsync(string bearer, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{relayBaseUrl.TrimEnd('/')}/v1/{pairing}");
        req.Headers.Add("Authorization", $"Bearer {bearer}");
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }
}
