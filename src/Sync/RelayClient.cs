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
/// What a push answered, in the only terms the caller can act on.
///
/// <para>This is the same defect <see cref="RelayPullResult"/> fixed, one method over.
/// <see cref="RelayClient.PushAsync"/> returned a bare <c>bool</c> — <c>res.StatusCode is
/// Created</c> — so a 409 <c>replay_rejected</c>, a 400, a 413, a timeout and a DNS failure were
/// all the single value <c>false</c>. Three of those are permanent for the bytes in hand and two
/// are worth retrying, and the caller could not tell them apart. <b>And the 409 carries a number
/// the protocol depends on:</b> §6.1 tells a sender to resume above
/// <c>max(persisted_seq, relay_latest)</c>, and the relay puts <c>latest</c> in the very body that
/// reports the counter is wrong — which <c>bool</c> discarded unread (PQ-S6-3).</para>
///
/// <para>The cases are the ones the push route can actually produce, derived from
/// <c>relay/src/index.ts:40-70</c> and <c>relay/src/channel.ts:138-191</c> rather than assumed, and
/// they answer three different questions: retry these bytes, never retry these bytes, or fix the
/// counter and send different bytes.</para>
/// </summary>
public abstract record RelayPushResult
{
    // Private ctor, as with RelayPullResult: the hierarchy is closed to the cases nested below, so a
    // `switch` over them is exhaustive by construction.
    private RelayPushResult() { }

    /// <summary>
    /// 201. The envelope was appended to the recipient's queue, <em>and nothing more</em> — it is
    /// not an acknowledgement that the receiver accepted, decrypted or applied anything. The relay
    /// is blind (§1) and has no opinion about the payload; §6.2's receiver rules run later and
    /// independently, so a sender MUST NOT report a delivered envelope as an applied one.
    ///
    /// <para>The 201 body (<c>{"ok":true,"seq":N}</c>) is deliberately <b>not parsed</b>. Doing so
    /// would invent a failure mode on top of a success: a relay that appended the envelope and then
    /// answered with an unreadable body has still appended it, and reporting that as a failure would
    /// make the sender retry bytes the relay already holds — which it then refuses with the 409
    /// below, turning a cosmetic problem into a real one.</para>
    /// </summary>
    public sealed record Ok : RelayPushResult;

    /// <summary>
    /// 409 <c>replay_rejected</c>: the envelope's <c>seq</c> was at or below the relay's high-water
    /// mark for its direction, so the relay refused it at the door (<c>channel.ts:171</c>).
    /// Neither a success nor a transport failure — retrying <em>these bytes</em> can never succeed,
    /// because the refusal is about the number they carry.
    /// </summary>
    /// <param name="Latest">
    /// The relay's high-water <c>seq</c> <b>for the direction the refused envelope named</b>, which
    /// is precisely the second term of §6.1's <c>max(persisted_seq, relay_latest)</c>. Not a
    /// pairing-wide position: a sender that read it as one would resume far too high and skip seqs.
    ///
    /// <para><b>Null when the body carried no usable number</b> — absent, empty, not JSON, not an
    /// object, no <c>latest</c> field, not an integer, or outside the legal seq range (§3.2). The
    /// range check is the same one <see cref="RelayClient.PullAsync"/> applies to a pull page's
    /// <c>latest</c>, and for the same reason: an integer is not a seq, and this number is one a
    /// sender would otherwise write straight into its own counter.</para>
    ///
    /// <para>Note what null does <em>not</em> do: it does not downgrade the result to
    /// <see cref="Unavailable"/>. The conflict is a fact independent of the number — the relay
    /// refused the seq, and that is true whether or not it explained itself. Reporting an
    /// unreadable 409 as "the relay did not answer" would tell the caller to retry the one thing
    /// that cannot work, which is exactly what §2.2 forbids. So the conflict survives and only the
    /// unusable number is dropped. This is the deliberate asymmetry with <c>PullAsync</c>, which
    /// refuses the whole page on a bad <c>latest</c>: there the number governs a cursor the caller
    /// is about to advance, here it is an optional aid to a decision already made.</para>
    /// </param>
    public sealed record Conflict(long? Latest) : RelayPushResult;

    /// <summary>
    /// 401/403 — the bearer was refused, or the pairing was purged, which answers 401 on every
    /// route and is deliberately indistinguishable from a wrong token (§2.3, PQ-S2-4). Retrying
    /// with the same bearer cannot help.
    /// </summary>
    public sealed record Unauthorised : RelayPushResult;

    /// <summary>
    /// 404 — as on the pull route this does <em>not</em> mean "the pairing was purged" (that is the
    /// 401 above). Either the pairing id fails the relay's shape check (<c>index.ts:55</c>) or the
    /// route is absent (<c>index.ts:66</c>): a configuration fault or a client/relay version skew.
    /// </summary>
    public sealed record Misconfigured(string Detail) : RelayPushResult;

    /// <summary>
    /// 400 <c>bad_request</c> — the relay could not parse the body, or the envelope failed its
    /// header-shape check (<c>channel.ts:143-159</c>). Unlike every other case here this one
    /// indicts <em>this side</em>: a conforming engine does not compose an envelope the relay
    /// refuses to shape-check, so it is a defect in the sender, and it is permanent for these
    /// bytes. Kept distinct from <see cref="TooLarge"/> because the remedy differs — a malformed
    /// envelope is a bug to fix, an oversized one is a payload to split (§4.4).
    /// </summary>
    public sealed record Rejected(string Detail) : RelayPushResult;

    /// <summary>
    /// 413 <c>too_large</c> — the body or the ciphertext exceeded §3.1's cap, which the relay
    /// measures in base64url characters because it cannot decode (<c>channel.ts:140,164</c>).
    /// Permanent for these bytes; the payload needs chunking, not a retry.
    /// </summary>
    public sealed record TooLarge : RelayPushResult;

    /// <summary>
    /// The relay did not answer usefully and may on the next tick: a transport failure, a timeout,
    /// a 5xx, or any other unexpected status — including a 200, which is not the 201 §2.2 pins for
    /// an appended envelope. Transient by assumption; these bytes are worth sending again.
    /// </summary>
    public sealed record Unavailable(string Detail) : RelayPushResult;
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

    /// <summary>
    /// Append one sealed envelope. The body IS the envelope JSON.
    ///
    /// <para>Never throws for a relay that refuses, is unreachable, or answers something
    /// unexpected — every one of those arrives as a <see cref="RelayPushResult"/> case. As with
    /// <see cref="PullAsync"/>, the one exception still propagated is the caller's own
    /// cancellation, which is not a relay condition: laundering it turns a requested shutdown into
    /// "the relay did not answer" and the loop above keeps ticking.</para>
    /// </summary>
    public async Task<RelayPushResult> PushAsync(
        string bearer, string envelopeJson, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Base("push"))
            {
                Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", $"Bearer {bearer}");
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);

            switch (res.StatusCode)
            {
                // 201 and only 201. §2.2 pins it as the appended answer, so a 200 is a relay not
                // behaving like the relay and falls to Unavailable with every other surprise --
                // which is what the old `is HttpStatusCode.Created` did too, preserved on purpose.
                case HttpStatusCode.Created:
                    return new RelayPushResult.Ok();

                case HttpStatusCode.Conflict:
                    return new RelayPushResult.Conflict(
                        ConflictLatest(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)));

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return new RelayPushResult.Unauthorised();

                case HttpStatusCode.NotFound:
                    return new RelayPushResult.Misconfigured(
                        "the relay does not serve push for this pairing (404)");

                case HttpStatusCode.BadRequest:
                    return new RelayPushResult.Rejected(
                        "the relay refused the envelope's shape (400)");

                case HttpStatusCode.RequestEntityTooLarge:
                    return new RelayPushResult.TooLarge();

                default:
                    return new RelayPushResult.Unavailable($"the relay answered {(int)res.StatusCode}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient raises this for its own timeout, which IS a relay condition. Separated
            // from the caller's cancellation by the token, because nothing else separates them.
            return new RelayPushResult.Unavailable("the push timed out");
        }
        catch (HttpRequestException ex)
        {
            return new RelayPushResult.Unavailable($"the relay was unreachable ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// The <c>latest</c> a 409 body carries, or null when it carries none this client may use.
    ///
    /// <para>Deliberately total — it has no failure channel because a 409 with an unreadable body is
    /// still a 409 (see <see cref="RelayPushResult.Conflict"/>). Every rejection here collapses to
    /// "no reconciliation input", never to an exception and never to a different result case.</para>
    /// </summary>
    private static long? ConflictLatest(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("latest", out var element)) return null;
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var latest))
                return null;
            // Same bound, same reason as the pull page's latest: `latest` is MAX(seq) over rows the
            // relay holds, every one of which passed its seq check, so it inherits seq's domain from
            // §3.2. A sender would otherwise write an absurd-but-integral number into the counter
            // §6.1 tells it to reconcile -- and unlike the pull cursor, that number goes on the wire.
            if (latest < 0 || latest > Protocol.MaxSeq) return null;
            return latest;
        }
        catch (JsonException)
        {
            return null;
        }
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
