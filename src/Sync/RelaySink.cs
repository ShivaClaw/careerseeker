namespace SeekerSvc.Sync;

/// <summary>
/// What a caller should do next about the bytes it just pushed — the one thing
/// <see cref="RelayPushResult"/> knew and no shipping code could act on.
///
/// <para><b>Why this exists.</b> <see cref="RelayPushResult"/> was introduced because a bare
/// <c>bool</c> could not tell a replay refusal from a DNS failure, and its own summary names the
/// three questions the cases answer: <em>retry these bytes, never retry these bytes, or fix the
/// counter and send different bytes</em>. But that knowledge lived only in prose on the record
/// types. <see cref="RelaySink"/> named each case distinctly <em>for the operator</em> and then
/// collapsed all of them back to <c>false</c> — so one layer above the fix, the conflation was
/// exactly as it had been. Measured rather than asserted: driven through the real
/// <see cref="SyncPushPath"/> composition for five engine cycles, a 400 <c>bad_request</c> and a
/// DNS failure produce the same push count, the same burnt seqs and the same delivered count
/// (C-DSP-1).</para>
///
/// <para><b>It is a disposition, not a status code.</b> Two results can share a disposition and
/// still need different words for the operator — a malformed envelope is a bug to fix and an
/// oversized one is a payload to split (§4.4) — so the log keeps them apart while the disposition
/// answers the narrower question a retry driver actually asks.</para>
/// </summary>
public enum PushDisposition
{
    /// <summary>201. The relay appended it. Nothing further is owed for these bytes.</summary>
    Delivered,

    /// <summary>
    /// The relay did not answer usefully and may on the next tick. These same bytes are worth
    /// sending again (<see cref="RelayPushResult.Unavailable"/>).
    /// </summary>
    RetryLater,

    /// <summary>
    /// 409. These bytes are dead — the refusal is about the <em>number</em> they carry — but the
    /// counter has been repaired from the relay's own high-water mark (§6.1), so the next envelope
    /// is genuinely different and resuming is productive rather than a loop
    /// (<see cref="RelayPushResult.Conflict"/>).
    /// </summary>
    ResendAbove,

    /// <summary>
    /// These exact bytes will never be accepted, and re-sending them is guaranteed waste: 413
    /// (too large for §3.1's cap) or 400 (this engine composed an envelope the relay would not
    /// shape-check). <b>A different payload may still succeed</b> — a smaller one certainly can, and
    /// a 400 may be specific to what this payload contained rather than to everything this engine
    /// composes. That uncertainty is why this is kept distinct from <see cref="PairingDead"/>
    /// instead of being folded into it.
    /// </summary>
    PayloadDead,

    /// <summary>
    /// Nothing this engine sends on this pairing will be accepted until a human changes something:
    /// 401/403 (the bearer was refused, or the pairing was purged) or 404 (the pairing id fails the
    /// relay's shape check, or the route is absent — a configuration fault or version skew).
    ///
    /// <para><b>This names a condition; it does not authorise a halt.</b> See
    /// <see cref="RelaySink"/>'s remarks — whether the engine should stop publishing on this
    /// disposition is a product decision that has not been taken, and a 401 that turns out to be a
    /// transient relay fault would make a self-imposed halt into the outage it was meant to
    /// prevent.</para>
    /// </summary>
    PairingDead,
}

/// <summary>
/// The engine→phone push sink: the rule that turns one <see cref="RelayPushResult"/> into the
/// effects it implies — persist the high-water mark, reconcile the counter (§6.1), name the case
/// for the operator — and collapses it back to the <c>bool</c>
/// <see cref="SyncPublisher"/>'s sink contract expects.
///
/// <para><b>Why this is a factory over delegates rather than a closure in the host.</b> This logic
/// used to live inline in <c>Program.cs</c>'s <c>BuildSyncBridge</c>, where it was unreachable from
/// any harness: the enclosing method returns null without a pairing, and the pairing comes from a
/// DPAPI-backed vault that exists only on the owner's Windows machine. The consequence was measured
/// rather than assumed — deleting the <see cref="SyncPublisher.ReconcileTo"/> call site failed
/// <em>no test in this repo</em>, so the one line that applies §6.1's reconciliation was held in
/// place by nothing.</para>
///
/// <para><b>And why the effects are injected rather than returned as a decision record.</b> A pure
/// <c>Decide(result) -> what should happen</c> function would be simpler to test and would answer
/// the wrong question. The gap here is not "does the engine know what a 409 means" — that rule was
/// already tested through <see cref="SyncPublisher.ReconcileTo"/> directly — it is "does the sink
/// actually <em>call</em> it". Only an observable call site can answer that, so the collaborators
/// arrive as delegates a fake can stand in for, and the assertion is that the call happened with
/// the relay's number.</para>
///
/// <para>The composition that supplies those delegates in production — vault, relay client, and the
/// publisher's own counter — still lives in the host and still cannot be executed here. What moves
/// into reach is the part that decides.</para>
///
/// <para><b>What this sink deliberately does NOT do: halt.</b> <see cref="PushDisposition.PairingDead"/>
/// and <see cref="PushDisposition.PayloadDead"/> name conditions under which every subsequent push is
/// guaranteed to fail, and it is tempting to stop publishing on one. That decision was considered and
/// <em>not</em> taken here, for two reasons worth an auditor's attention. First, the retry is
/// <em>ratified</em>: <c>EngineSyncBridge.PublishAsync</c> leaves its snapshot flag unset on failure
/// precisely so the next cycle re-sends the snapshot, an audit finding of 2026-07-24 whose purpose is
/// to stop a fresh phone merging a delta into demo fixture rows. Suppressing that retry from down here
/// would silently revert a decision taken above. Second, permanence is an <em>assumption</em> about the
/// relay's answer, not a fact about the world: a 401 raised by a relay deploy blip is transient, and an
/// engine that halted on the first one would convert a minute of relay trouble into a sync outage
/// lasting until someone noticed. So the disposition is computed, named and surfaced, and the policy
/// question — halt, back off, or keep trying — is left to the layer that owns it. <b>This is a
/// deliberate decision, not an oversight.</b></para>
///
/// <para><b>What it does instead.</b> It stops lying and it stops repeating itself. The old
/// <c>TooLarge</c> line asserted the envelope "will not be retried"; nothing in this repo prevented
/// that retry, and the reproduction measured four of them in five cycles (C-DSP-2). And because every
/// failing cycle emitted a byte-identical line, an operator's log filled with one sentence and the
/// transitions that actually carry information — it started, it stopped — were buried in it (C-DSP-4).
/// A line identical to the one before it is now counted rather than repeated, and the return to
/// <see cref="PushDisposition.Delivered"/> is announced with that count, so nothing is hidden and the
/// two events an operator needs are the two that appear.</para>
/// </summary>
public static class RelaySink
{
    /// <summary>
    /// What the caller should do next about the bytes just pushed. Pure, total, and public because it
    /// is the part worth reusing: a future retry driver, a "needs attention" surface on the pairing
    /// page, or a backoff policy all need this mapping and none of them should re-derive it from the
    /// HTTP statuses a second time.
    ///
    /// <para>The unreachable arm answers <see cref="PushDisposition.RetryLater"/> rather than a
    /// permanent disposition. <see cref="RelayPushResult"/>'s hierarchy is closed by a private
    /// constructor so no such value exists today; if one were ever added, treating an
    /// <em>unrecognised</em> answer as permanent would invent a permanence nobody established, and the
    /// safe direction for an unknown is to try again rather than to give up.</para>
    /// </summary>
    public static PushDisposition Classify(RelayPushResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result switch
        {
            RelayPushResult.Ok => PushDisposition.Delivered,
            RelayPushResult.Conflict => PushDisposition.ResendAbove,
            RelayPushResult.TooLarge => PushDisposition.PayloadDead,
            RelayPushResult.Rejected => PushDisposition.PayloadDead,
            RelayPushResult.Unauthorised => PushDisposition.PairingDead,
            RelayPushResult.Misconfigured => PushDisposition.PairingDead,
            RelayPushResult.Unavailable => PushDisposition.RetryLater,
            _ => PushDisposition.RetryLater,
        };
    }

    /// <summary>
    /// Builds the sink. <paramref name="pushedSeq"/> reports the seq the envelope now in flight was
    /// assigned, or <c>null</c> when no publisher is attached yet — the publisher and its sink are
    /// mutually referential, so the host constructs one holding a reference the other fills in, and
    /// this signature keeps that "not yet" case explicit instead of laundering it into a 0 that
    /// would be persisted as a real high-water mark.
    /// </summary>
    /// <param name="push">Sends the sealed envelope; in production <see cref="RelayClient.PushAsync"/>.</param>
    /// <param name="pushedSeq">The seq being pushed, or null if no publisher is attached.</param>
    /// <param name="persistSeq">Records the e2p high-water mark durably (the vault).</param>
    /// <param name="reconcileTo">Raises the publisher's counter; returns whether it moved.</param>
    /// <param name="log">Operator-facing line sink.</param>
    public static Func<string, CancellationToken, Task<bool>> Create(
        Func<string, CancellationToken, Task<RelayPushResult>> push,
        Func<long?> pushedSeq,
        Action<long> persistSeq,
        Func<long, bool> reconcileTo,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(push);
        ArgumentNullException.ThrowIfNull(pushedSeq);
        ArgumentNullException.ThrowIfNull(persistSeq);
        ArgumentNullException.ThrowIfNull(reconcileTo);
        ArgumentNullException.ThrowIfNull(log);

        // Dedupe state. The publisher assigns seqs with Interlocked.Increment and may run pushes
        // concurrently, so read-modify-write of this state has to be atomic against itself; a lock
        // is enough because the only work inside it is a string compare and the log call.
        var gate = new object();
        string? lastLine = null;
        var lastDisposition = PushDisposition.Delivered;
        var suppressed = 0;

        // Emits `line`, unless it is identical to the line before it, in which case it is counted.
        // A transition back to Delivered announces the recovery AND the count, so a suppressed run
        // is never silently dropped -- the operator learns both that it stopped and how long it ran.
        void Report(PushDisposition disposition, string? line)
        {
            lock (gate)
            {
                if (disposition == PushDisposition.Delivered)
                {
                    if (lastDisposition != PushDisposition.Delivered)
                    {
                        log(suppressed > 0
                            ? $"Sync: push recovered ({lastDisposition}); the preceding line was repeated {suppressed} more time(s) and suppressed."
                            : $"Sync: push recovered ({lastDisposition}).");
                    }
                }
                else if (line is not null)
                {
                    if (line == lastLine) suppressed++;
                    else { log(line); suppressed = 0; }
                }

                lastLine = disposition == PushDisposition.Delivered ? null : line;
                lastDisposition = disposition;
            }
        }

        return async (envelopeJson, ct) =>
        {
            var result = await push(envelopeJson, ct).ConfigureAwait(false);
            var disposition = Classify(result);

            // The switch now produces the operator's WORDS; the success/failure bool is derived from
            // the disposition below rather than written out per case, so the two cannot drift apart
            // -- a case that returned true while classifying as PayloadDead used to be expressible.
            string? line;
            switch (result)
            {
                case RelayPushResult.Ok:
                    // Persisted AFTER the relay's 201, which is what makes the store able to lag the
                    // relay by one on a crash in between — the very case §6.1's reconciliation and
                    // SyncPublisher.ResumeSeq exist to repair.
                    if (pushedSeq() is { } accepted) persistSeq(accepted);
                    line = null;
                    break;

                case RelayPushResult.Conflict conflict:
                    // §6.1's reconciliation input, arriving at the exact moment the counter is proved
                    // wrong. These bytes are still dead — the refusal is about the number they carry,
                    // and re-sending them cannot change it — so this still returns false; what changes
                    // is that the NEXT push assigns a seq above the relay's mark instead of walking up
                    // one at a time into the same 409.
                    if (conflict.Latest is { } latest && pushedSeq() is { } refused)
                    {
                        // The log distinguishes reconciled from reported, because ReconcileTo refuses
                        // to move the counter DOWN and an operator needs to see which happened.
                        line = reconcileTo(latest)
                            ? $"Sync: the relay refused seq {refused} as a replay; reconciled the e2p counter up to its high-water mark {latest} (§6.1). The next envelope resumes above it."
                            : $"Sync: the relay refused seq {refused} as a replay but reported a high-water mark of {latest}, at or below this engine's counter. NOT applied -- rewinding would re-issue seqs the phone may already have accepted (§6.2).";
                    }
                    else
                    {
                        line = "Sync: the relay refused the envelope as a replay and reported no usable high-water mark. Nothing to reconcile (§6.1).";
                    }
                    break;

                case RelayPushResult.TooLarge:
                    // CORRECTED. This line used to end "it will not be retried", which was false:
                    // nothing in this repo prevents the retry, and EngineSyncBridge's ratified
                    // snapshot policy guarantees it -- four retries in five cycles, measured
                    // (C-DSP-2). Saying so is the difference between an operator who splits the
                    // payload (§4.4) and one who waits for a retry that was never coming.
                    line = "Sync: the relay refused the envelope as too large (§3.1). These bytes can never be accepted -- the payload needs splitting (§4.4), and until it is split the engine will re-attempt this on every cycle.";
                    break;

                case RelayPushResult.Rejected rejected:
                    // This side composed something the relay would not shape-check. A bug here.
                    line = $"Sync: the relay rejected the envelope's shape -- {rejected.Detail}. This is an engine defect, not a relay outage, and it will recur on every cycle until the engine is fixed.";
                    break;

                case RelayPushResult.Unauthorised:
                    line = "Sync: the relay refused the pairing's token (401/403). The pairing may have been purged. Nothing will send on this pairing until it is re-paired.";
                    break;

                case RelayPushResult.Misconfigured misconfigured:
                    line = $"Sync: {misconfigured.Detail}. Check the paired relay URL and pairing id. Nothing will send on this pairing until that is corrected.";
                    break;

                case RelayPushResult.Unavailable unavailable:
                    line = $"Sync: the push did not reach the relay -- {unavailable.Detail}.";
                    break;

                default:
                    // Unreachable: RelayPushResult is closed by a private constructor. Kept as the
                    // compiler's exhaustiveness answer, not as a case that can occur.
                    line = null;
                    break;
            }

            Report(disposition, line);

            // The sink's contract is still bool, so this collapses back to one — but it collapses
            // ONCE, here, and it is now DERIVED from the disposition rather than written per case.
            // Before RelayPushResult the collapse happened inside the client and the operator's log
            // could not tell a replay refusal from a DNS failure; before PushDisposition it happened
            // here and no CALLER could tell them apart either.
            return disposition == PushDisposition.Delivered;
        };
    }
}
