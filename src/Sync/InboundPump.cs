using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>One page of phone→engine envelopes exactly as the relay served it (§2.1).</summary>
/// <param name="Envelopes">The page's elements, each a bare §3 envelope. Untrusted: the relay
/// controls this array completely.</param>
/// <param name="Latest">The relay's high-water mark for the direction. Also untrusted, and also
/// the bound §6.4 uses — see <see cref="InboundPump"/>.</param>
public sealed record InboundPage(IReadOnlyList<JsonElement> Envelopes, long Latest);

/// <summary>
/// What one call to <see cref="InboundPump.DrainAsync"/> did. Every field is an observation, not a
/// verdict: the pump does not decide whether a sync is healthy, it reports and the caller decides.
/// </summary>
/// <param name="PullFailed">The relay did not answer. Everything else is then empty or zero —
/// nothing was fetched, so nothing was dispatched.</param>
/// <param name="Pulled">Elements the relay handed back on this page.</param>
/// <param name="Outcomes">One entry per envelope the receiver accepted, in arrival order.</param>
/// <param name="Rejections">Parse and receive rejections (§7.2), in arrival order. A non-empty list
/// means the paired phone and this engine disagree about keys, version or framing — pulling harder
/// will not fix it.</param>
/// <param name="Cursor">The transport cursor after this call: the <c>since</c> the next pull sends.</param>
/// <param name="Latest">The relay's claimed high-water mark for this page.</param>
/// <param name="MoreAvailable"><c>Cursor &lt; Latest</c> — call again rather than waiting a tick.</param>
public sealed record InboundReport(
    bool PullFailed,
    int Pulled,
    IReadOnlyList<InboundOutcome> Outcomes,
    IReadOnlyList<SyncError> Rejections,
    long Cursor,
    long Latest,
    bool MoreAvailable);

/// <summary>
/// The phone→engine transport loop: the piece that turns <see cref="InboundDispatcher"/> from a
/// library into something the running engine actually does.
///
/// Until this existed the engine could publish and could not receive. Every inbound part shipped and
/// was individually correct — <see cref="EnvelopeJson"/>, <see cref="EnvelopeReceiver"/>,
/// <see cref="InboundDispatcher"/>, <see cref="EntitlementService"/>,
/// <see cref="IEntitlementAckPublisher"/>, and the vault's <c>last_p2e_seq</c> — and not one of them
/// had a production caller. A verified purchase reached the engine's own flag and stopped there.
///
/// This is the C# counterpart of the phone's <c>core/.../SyncPump.kt</c>, and it is deliberately the
/// same shape: all the ordering decisions live here, where they can be tested without a relay, a
/// phone, or a Windows machine; the host contributes I/O and nothing else.
///
/// ## The two sequence numbers, which are not the same number
///
/// **The replay mark** (§6.2) is the highest seq the receiver has *accepted*. It is authenticated, it
/// is persisted across restarts (the pairing vault's <c>last_p2e_seq</c>), and it is what refuses a
/// replayed entitlement. **The transport cursor** is the <c>since</c> of the next pull. It advances on
/// every envelope *seen*, including ones the receiver refused, because an envelope that is re-fetched
/// forever is a page the engine pulls, refuses, and pulls again — and §6.2 forbids letting one
/// unreadable envelope stall the direction.
///
/// The cursor is seeded from the persisted replay mark rather than kept in its own vault slot: after a
/// restart, re-fetching everything above the last *accepted* seq is exactly right, because anything
/// that was seen-but-not-accepted last time deserves a second, clean attempt.
///
/// ## What moves the cursor, and why the distinction is the whole point
///
/// §6.4 (arriving with PR #33; §6.1/§6.2 in this tree already imply it) says the cursor MUST advance
/// only to a seq **recovered from the sealed bytes**, and that an element with no authenticated seq
/// MAY advance it by the number it *claims* but MUST NOT pass the page's own <c>latest</c>.
///
/// A seq is recovered from the sealed bytes only when the AEAD tag verifies — the seq is in the AAD
/// (§4.1), so the tag is what makes it a fact rather than a claim. **Parsing is not authenticating.**
/// That distinction is load-bearing and it is easy to lose: an envelope can be perfectly well-formed
/// §3 JSON, with a valid pairing id, dir, key_id, nonce and base64url ciphertext, and still be bytes
/// the relay made up. Its header seq parses. It is not authenticated by anything.
///
/// So this pump advances the cursor freely **only for an envelope the receiver accepted**. For every
/// other element — one that fails the §3 parse, and equally one that parses and then fails the tag,
/// the version check, the key check or the replay check — the advance is bounded by the page's
/// <c>latest</c>. Bounded, not refused: refusing stalls the direction permanently on one corrupt byte.
/// Bounded, not free: free is history truncation performed without decrypting anything — one crafted
/// element claiming <c>seq: 1000000</c> walks the cursor past every envelope below it, and since the
/// cursor never moves backwards those envelopes are never requested again. The two failure modes are
/// not symmetric. A stall keeps <c>latest</c> above the cursor, keeps reporting more available, and
/// resumes the moment a readable page arrives. Truncation is silent, permanent, and presents as a
/// healthy fully-caught-up sync. When the choice is between them, this stalls.
///
/// Bounding by <c>latest</c> costs an honest relay nothing: its <c>latest</c> covers every row it
/// serves, so the bound is a no-op on every conforming page.
///
/// **Corrected, and this paragraph used to say the opposite.** It claimed the bound "denies a hostile
/// relay a second, independent lever, because <c>latest</c> is already the number it must publish to
/// say there is more". That is true of an honest relay and false of the one this bound exists for:
/// <c>latest</c> and the crafted element arrive in the same response, from the same party, and
/// nothing authenticates either. Measured — one unreadable element claiming <c>seq: 1000000</c>
/// served with <c>latest: 5</c> bounds the cursor to 5; the same element served with
/// <c>latest: Int64.MaxValue</c> puts it at 1000000. **§6.4's bound is supplied by the party it
/// defends against**, so against a relay willing to inflate one number it is not a bound at all.
///
/// <see cref="RelayClient.PullAsync"/> now refuses a page whose <c>latest</c> is outside
/// <see cref="Protocol.MaxSeq"/>, which lowers the ceiling from <c>2^63-1</c> to <c>2^53-1</c> and
/// **does not close this**: 2^53-1 is still far past any real counter. What actually closes it is a
/// bound that does not come from the relay — the receiver's own persisted mark plus the page size —
/// and that is a protocol change, so it is written down as a question rather than invented here.
/// The bound is kept because it is still the tightest number available in-band, and because against
/// a relay that is honest about <c>latest</c> while replaying or corrupting an element it works.
///
/// ## What this class deliberately does not do
///
/// No transport retry or backoff — <see cref="RelayClient"/> owns that, and *when* to drain is the
/// host's lifecycle decision. No send path of any kind: the only thing that can leave as a result of
/// draining is what <see cref="InboundDispatcher"/>'s own seams emit (an <c>entitlement_ack</c> for an
/// accepted receipt, a re-published snapshot for a <c>pull_request</c>), and neither is reachable from
/// this class except through that dispatcher. Not thread-safe: drive it from one caller.
/// </summary>
public sealed class InboundPump
{
    private readonly Func<long, CancellationToken, Task<InboundPage?>> _pull;
    private readonly InboundDispatcher _dispatcher;
    private readonly string _direction;
    private readonly Action<long>? _onAccepted;
    private long _cursor;

    /// <param name="pull">Fetches one page for <c>p2e</c> with <c>seq &gt; since</c>. Returns null when
    /// the relay did not answer usefully — a transport failure must arrive as data, never as an
    /// exception, or one bad response takes the whole drain down with it.</param>
    /// <param name="dispatcher">Owns every trust decision and every routing decision. This class makes
    /// none of either.</param>
    /// <param name="direction">The direction being pulled — <c>p2e</c> for the engine. Every element
    /// whose header names a different one is refused before dispatch; see <see cref="DrainAsync"/>.</param>
    /// <param name="resumeFrom">The persisted <c>last_p2e_seq</c> (§6.1). Seeds the cursor so a restart
    /// does not re-fetch the whole retained history.</param>
    /// <param name="onAccepted">Persists the new replay mark. Called with the **authenticated** seq of
    /// an accepted envelope only — never with a claimed one, and never for a rejection.</param>
    public InboundPump(
        Func<long, CancellationToken, Task<InboundPage?>> pull,
        InboundDispatcher dispatcher,
        string direction = Protocol.PhoneToEngine,
        long resumeFrom = 0,
        Action<long>? onAccepted = null)
    {
        _pull = pull ?? throw new ArgumentNullException(nameof(pull));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _direction = string.IsNullOrEmpty(direction) ? throw new ArgumentException("A direction is required.", nameof(direction)) : direction;
        _onAccepted = onAccepted;
        _cursor = resumeFrom > 0 ? resumeFrom : 0;
    }

    /// <summary>The <c>since</c> the next <see cref="DrainAsync"/> will send.</summary>
    public long Cursor => _cursor;

    /// <summary>
    /// Pull one page and dispatch it. Returns as soon as the relay refuses to answer: a page that never
    /// arrived cannot be dispatched, and inventing a decision from a transport failure is how an engine
    /// ends up acting on something nobody sent.
    /// </summary>
    public async Task<InboundReport> DrainAsync(CancellationToken ct = default)
    {
        var page = await _pull(_cursor, ct).ConfigureAwait(false);
        if (page is null)
            return new InboundReport(true, 0, Array.Empty<InboundOutcome>(), Array.Empty<SyncError>(), _cursor, 0, false);

        var outcomes = new List<InboundOutcome>();
        var rejections = new List<SyncError>();

        foreach (var element in page.Envelopes)
        {
            // The same strict §3 parse the receiver's own contract assumes, run here so the header is
            // available for the cursor rules below. This is not a second, lenient parser: it is
            // EnvelopeJson, the one the phone mirrors, and the receiver still owns every trust decision
            // that follows it.
            var parsed = EnvelopeJson.Parse(RawText(element));
            var header = parsed.Envelope;

            if (header is null)
            {
                // No authenticated seq exists, so the only number available is the one the element
                // claims — read leniently, because nothing downstream trusts it, and bounded by latest.
                AdvanceBounded(ClaimedSeq(element), page.Latest);
                rejections.Add(parsed.Error ?? SyncError.DecryptFailed);
                continue;
            }

            // The direction is checked HERE rather than left to the receiver, and it is not redundant.
            // An envelope the *engine itself* sent (dir `e2p`, sealed under k_e2p, unsigned) is a
            // well-formed envelope that a hostile or merely confused relay can serve back on the p2e
            // page. Every check downstream then passes: the sig-placement rule is satisfied because an
            // e2p envelope carries no sig, the replay check consults the *e2p* counter — which the
            // resume above never seeds — and the key lookup hands over k_e2p, so the tag verifies. It
            // is accepted, its kind falls through to Ignored, and the damage is done off to the side:
            // the cursor advances unbounded and `onAccepted` writes an **e2p** seq into the persisted
            // **p2e** replay mark. Push that mark past the phone's counter and every genuine phone
            // envelope afterwards is refused as a replay — a silent, permanent, one-directional
            // outage, caused by the engine being handed back its own traffic.
            if (!string.Equals(header.Dir, _direction, StringComparison.Ordinal))
            {
                AdvanceBounded(header.Seq, page.Latest);
                rejections.Add(SyncError.DecryptFailed);
                continue;
            }

            var result = await _dispatcher.DispatchAsync(header, ct).ConfigureAwait(false);

            if (result.Outcome == InboundOutcome.ReceiveRejected)
            {
                // It parsed, so it has a header seq — but the receiver refused it, so the AEAD tag never
                // verified over that seq (or never ran at all). Same rule as an unparseable element:
                // advance, because §6.2 forbids stalling, but only as far as latest.
                AdvanceBounded(header.Seq, page.Latest);
                rejections.Add(result.ReceiveError ?? SyncError.DecryptFailed);
                continue;
            }

            // Accepted: the tag verified over the AAD, and the AAD carries this seq. It is a fact now,
            // and it is the one number here that may move the cursor without a bound.
            if (header.Seq > _cursor) _cursor = header.Seq;
            _onAccepted?.Invoke(header.Seq);
            outcomes.Add(result.Outcome);
        }

        return new InboundReport(
            PullFailed: false,
            Pulled: page.Envelopes.Count,
            Outcomes: outcomes,
            Rejections: rejections,
            Cursor: _cursor,
            Latest: page.Latest,
            MoreAvailable: _cursor < page.Latest);
    }

    private void AdvanceBounded(long claimed, long latest)
    {
        var bounded = Math.Min(claimed, latest);
        if (bounded > _cursor) _cursor = bounded;
    }

    private static string RawText(JsonElement element)
    {
        // Re-serialised rather than sliced out of the original response text, so the bytes the strict
        // parser sees are this element and nothing around it.
        try { return element.GetRawText(); }
        catch (InvalidOperationException) { return ""; }
    }

    /// <summary>
    /// The element's own top-level <c>seq</c>, read leniently: anything unusable reads as 0. Lenient is
    /// correct precisely because no trust decision consumes this — it feeds one bounded cursor advance
    /// and nothing else. A string, a fraction, or an absent field must not throw the drain away.
    /// </summary>
    private static long ClaimedSeq(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty("seq", out var seq)) return 0;
        if (seq.ValueKind != JsonValueKind.Number) return 0;
        return seq.TryGetInt64(out var value) ? value : 0;
    }
}
