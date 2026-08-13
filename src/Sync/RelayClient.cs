using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>One envelope pulled from the relay, still ciphertext.</summary>
public sealed record PulledEnvelope(JsonElement Raw);

/// <summary>
/// What a pull answered, in the only terms the caller can act on.
///
/// <para>This exists because <see cref="RelayClient.PullAsync"/> used to have no failure channel in
/// its signature: it called <c>EnsureSuccessStatusCode</c>, <c>GetProperty</c> and <c>GetInt64</c>,
/// all three of which throw, so every relay answer that was not a well-formed 200 left by way of an
/// exception. The host contained that by catching five exception types <em>by name</em>
/// (<c>src/Engine/Program.cs</c>), which is containment rather than a fix — a sixth escaping type
/// takes the engine's tick down with it. A transport failure must arrive as data; the pump's own
/// contract says so in as many words.</para>
///
/// <para>The four cases are the ones the relay can actually produce on the pull route, derived from
/// <c>relay/src/index.ts:40-70</c> rather than assumed, and they differ in what a caller should
/// <em>do</em>: retry, stop retrying, or ask a human.</para>
/// </summary>
public abstract record RelayPullResult
{
    // Private ctor: the hierarchy is closed to the four cases nested below, so a `switch` over them
    // is exhaustive by construction and a fifth case cannot be added from outside this file.
    private RelayPullResult() { }

    /// <summary>A 200 carrying a well-formed page (§2.2). The envelopes are still sealed.</summary>
    public sealed record Ok(IReadOnlyList<JsonElement> Envelopes, long Latest) : RelayPullResult;

    /// <summary>
    /// 401/403. The bearer was refused — <em>or</em> the pairing was purged, which answers 401 on
    /// every route and is deliberately indistinguishable from a wrong token (§2.3, and PQ-S2-4:
    /// the relay never tells a caller holding a bad credential whether a pairing was ever real).
    /// Retrying with the same bearer cannot help; a wrong token does not become right.
    /// </summary>
    public sealed record Unauthorised : RelayPullResult;

    /// <summary>
    /// 404. On the pull route this does <em>not</em> mean "the pairing was purged" — a purge answers
    /// 401, above. It means the relay does not serve what was asked for, and there are exactly two
    /// ways to reach it: the pairing id fails the relay's shape check (<c>index.ts:55</c>,
    /// <c>{"error":"pairing_unknown"}</c>), or the route is absent (<c>index.ts:66</c>).
    ///
    /// <para>The first is reachable for the engine and is <em>not</em> reachable for the phone, which
    /// is why the phone's mapping cannot simply be copied here: the phone refuses a malformed pairing
    /// id at construction, and this client does not — see <see cref="RelayClient"/>'s own note. So
    /// this is a configuration fault (a corrupt or hand-edited vault, or a client/relay version skew),
    /// terminal in the sense that retrying the same request forever cannot fix it.</para>
    /// </summary>
    public sealed record Misconfigured(string Detail) : RelayPullResult;

    /// <summary>
    /// The relay did not answer usefully and may on the next tick: a transport failure, a timeout, a
    /// 5xx, any other unexpected status, or a 200 whose body is not a §2.2 pull page (an HTML error
    /// page from something in front of the relay lands here). Transient by assumption — the caller
    /// should keep its cursor where it is and try again.
    /// </summary>
    public sealed record Unavailable(string Detail) : RelayPullResult;
}

/// <summary>
/// The engine's HTTPS client for the blind relay (Sync-Protocol.md §2). Push/pull only;
/// the WebSocket live feed is a P2 concern. Every call carries the bearer for the pairing,
/// and the client never sees or holds key material — it moves ciphertext the codec sealed.
///
/// <para>One asymmetry with the phone's client, recorded because it changes what a 404 means here:
/// the phone refuses a malformed pairing id at construction (<c>RelayClient.kt</c>'s
/// <c>require(isValidPairingId(pairing))</c>) and this one does not, though the check exists in this
/// assembly as <see cref="EnvelopeJson.IsValidPairingId"/>. So the relay's malformed-id 404
/// (<c>relay/src/index.ts:55</c>) is unreachable for the phone and reachable for the engine — see
/// <see cref="RelayPullResult.Misconfigured"/>. Adding the guard here is a constructor-throwing
/// change to a type built on the engine's startup path, so it is deliberately left to a slice that
/// can run the full local gate; this note exists so the next reader does not mistake the asymmetry
/// for an oversight.</para>
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

    /// <summary>
    /// Fetch envelopes for one direction with seq &gt; since (§2.2).
    ///
    /// <para>Never throws for a relay that answers badly, is unreachable, or answers something that is
    /// not a pull page — every one of those arrives as a <see cref="RelayPullResult"/> case. The one
    /// exception it still propagates is the caller's own cancellation, which is not a relay failure:
    /// swallowing it would turn a requested shutdown into a silent "the relay did not answer", and the
    /// loop above would keep ticking.</para>
    /// </summary>
    public async Task<RelayPullResult> PullAsync(
        string bearer, string dir, long since, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Base($"pull?dir={dir}&since={since}"));
            req.Headers.Add("Authorization", $"Bearer {bearer}");
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);

            switch (res.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return new RelayPullResult.Unauthorised();
                case HttpStatusCode.NotFound:
                    return new RelayPullResult.Misconfigured(
                        $"the relay does not serve pull for this pairing (404) -- dir={dir}");
                default:
                    return new RelayPullResult.Unavailable($"the relay answered {(int)res.StatusCode}");
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            // Each of these three was a throwing call before (GetProperty twice, GetInt64 once). They
            // are checks now rather than catches: a 200 carrying anything other than a pull page is a
            // relay that is not behaving like the relay, and that is a value the caller can act on.
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new RelayPullResult.Unavailable("the pull page is not a JSON object");
            if (!root.TryGetProperty("envelopes", out var envelopes) || envelopes.ValueKind != JsonValueKind.Array)
                return new RelayPullResult.Unavailable("the pull page carries no envelopes array");
            if (!root.TryGetProperty("latest", out var latestElement)
                || latestElement.ValueKind != JsonValueKind.Number
                || !latestElement.TryGetInt64(out var latest))
                return new RelayPullResult.Unavailable("the pull page carries no integer latest");
            // ...and being an integer is not the same as being a seq. `TryGetInt64` fixes the type
            // and the width, so it already refuses 1e19 and 1e300 (measured), but it accepts the
            // whole of Int64 -- and `latest` is not an Int64, it is `MAX(seq)` over the rows the
            // relay holds. Every one of those rows passed the relay's own seq check, so `latest`
            // inherits seq's domain: >= 0 (0 meaning "this direction holds nothing") and never
            // above Protocol.MaxSeq. A page saying otherwise is a page no conforming relay can
            // produce, which is the same class as the three checks above and gets the same answer.
            //
            // Refuse the page rather than clamp the value. Clamping would accept a page while
            // silently disagreeing with it about the one number the caller uses to bound its
            // cursor, and the asymmetry decides it: refusing keeps the cursor where it is, reports
            // PullFailed and retries next tick -- loud and recoverable -- while clamping advances
            // a cursor on a number this client has already judged untrustworthy.
            if (latest < 0 || latest > Protocol.MaxSeq)
                return new RelayPullResult.Unavailable(
                    $"the pull page's latest is outside the legal seq range (§3.2): {latest}");

            return new RelayPullResult.Ok(
                envelopes.EnumerateArray().Select(e => e.Clone()).ToList(), latest);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not a relay condition, so it is not a RelayPullResult.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient raises the same type for its own timeout, which IS a relay condition. The
            // two are separated by the token, because they are separated by nothing else.
            return new RelayPullResult.Unavailable("the pull timed out");
        }
        catch (HttpRequestException ex)
        {
            return new RelayPullResult.Unavailable($"the relay was unreachable ({ex.GetType().Name})");
        }
        catch (JsonException)
        {
            return new RelayPullResult.Unavailable("the pull page was not valid JSON");
        }
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
