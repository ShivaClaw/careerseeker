using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>One envelope pulled from the relay, still ciphertext.</summary>
public sealed record PulledEnvelope(JsonElement Raw);

/// <summary>
/// How the relay answered one push (Sync-Protocol.md §2.2). The engine needs these apart:
/// <c>Replayed</c> is the only one that carries a reconciliation value, and it is the only
/// failure that says "your counter is behind" rather than "try again later".
/// </summary>
public enum PushStatus
{
    /// <summary>201 — appended, and the seq is now the direction's high-water mark.</summary>
    Accepted,

    /// <summary>409 <c>replay_rejected</c> — the seq was at or below the relay's mark. Carries <c>latest</c>.</summary>
    Replayed,

    /// <summary>400 <c>bad_request</c> — the envelope is structurally wrong. Retrying it cannot help.</summary>
    Rejected,

    /// <summary>413 <c>too_large</c> — the ciphertext exceeds §3.1's cap. Retrying it cannot help.</summary>
    TooLarge,

    /// <summary>401/403 — the bearer is not (or is no longer) good for this pairing.</summary>
    Unauthorised,

    /// <summary>Any other status (5xx, 429): "not now", as opposed to the 4xx answers above.</summary>
    Unavailable,
}

/// <summary>
/// The result of one push. <see cref="Latest"/> is the relay's high-water seq for the direction
/// and is populated <em>only</em> for <see cref="PushStatus.Replayed"/> — §2.2 pins <c>latest</c>
/// to the 409 body and deliberately omits it from 400 and 413, so a non-null here on any other
/// status would be this client inventing a number. It is also nullable on a 409, because a body
/// that fails to parse must degrade to "no reconciliation value" rather than to zero: zero is a
/// legal seq and would read as "the relay has nothing", the precise misreading §6.1 exists to
/// prevent.
/// </summary>
public readonly record struct PushOutcome(PushStatus Status, long? Latest)
{
    /// <summary>True only for 201. Every caller that used to read the old <c>bool</c> means this.</summary>
    public bool Accepted => Status is PushStatus.Accepted;
}

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

    /// <summary>
    /// Append one sealed envelope. The body IS the envelope JSON.
    /// </summary>
    /// <remarks>
    /// Returns the relay's answer rather than a bare success flag (§2.2). Collapsing 409 into
    /// "false" made a replay indistinguishable from a timeout and discarded the <c>latest</c> the
    /// relay sends precisely so the sender can reconcile — see §6.1 and PQ-S6-3.
    /// <para>
    /// Transport failures still throw, exactly as before: this method maps HTTP answers, and
    /// <see cref="PushStatus.Unavailable"/> means "the relay answered, but not with a status v1
    /// pins". A caller that wants timeouts as data must catch them itself.
    /// </para>
    /// </remarks>
    public async Task<PushOutcome> PushAsync(string bearer, string envelopeJson, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Base("push"))
        {
            Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Authorization", $"Bearer {bearer}");
        using var res = await http.SendAsync(req, ct);
        return res.StatusCode switch
        {
            HttpStatusCode.Created => new PushOutcome(PushStatus.Accepted, null),
            HttpStatusCode.Conflict => new PushOutcome(PushStatus.Replayed, await ReadLatestAsync(res, ct).ConfigureAwait(false)),
            HttpStatusCode.BadRequest => new PushOutcome(PushStatus.Rejected, null),
            HttpStatusCode.RequestEntityTooLarge => new PushOutcome(PushStatus.TooLarge, null),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new PushOutcome(PushStatus.Unauthorised, null),
            _ => new PushOutcome(PushStatus.Unavailable, null),
        };
    }

    /// <summary>
    /// Reads <c>latest</c> out of a 409 body. Total by construction: a body that is absent,
    /// truncated, not JSON, or carries <c>latest</c> as a string or a fraction yields null. The
    /// relay is blind infrastructure and this value steers a counter, so anything unparseable is
    /// "no value" rather than a guess.
    /// </summary>
    private static async Task<long?> ReadLatestAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("latest", out var latest)
                && latest.ValueKind == JsonValueKind.Number
                && latest.TryGetInt64(out var value)
                ? value
                : null;
        }
        catch (JsonException) { return null; }
        catch (HttpRequestException) { return null; }
        // Unreachable while the ValueKind guard above stands, and kept precisely because that guard
        // is one edit away from being reordered: TryGetInt64 THROWS on a non-number element rather
        // than returning false, so without this the body {"latest":"42"} takes down the push path.
        // Mutation-checked — removing the guard turns this from a crash into a null (PQ-S6-3, M7).
        catch (InvalidOperationException) { return null; }
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
