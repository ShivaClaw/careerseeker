namespace SeekerSvc.Sync;

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
/// </summary>
public static class RelaySink
{
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

        return async (envelopeJson, ct) =>
        {
            var result = await push(envelopeJson, ct).ConfigureAwait(false);

            // The sink's contract is still bool, so this collapses back to one — but it collapses
            // ONCE, here, after each case has been named. Before RelayPushResult the collapse
            // happened inside the client and the operator's log could not tell a replay refusal
            // from a DNS failure.
            switch (result)
            {
                case RelayPushResult.Ok:
                    // Persisted AFTER the relay's 201, which is what makes the store able to lag the
                    // relay by one on a crash in between — the very case §6.1's reconciliation and
                    // SyncPublisher.ResumeSeq exist to repair.
                    if (pushedSeq() is { } accepted) persistSeq(accepted);
                    return true;

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
                        log(reconcileTo(latest)
                            ? $"Sync: the relay refused seq {refused} as a replay; reconciled the e2p counter up to its high-water mark {latest} (§6.1). The next envelope resumes above it."
                            : $"Sync: the relay refused seq {refused} as a replay but reported a high-water mark of {latest}, at or below this engine's counter. NOT applied -- rewinding would re-issue seqs the phone may already have accepted (§6.2).");
                    }
                    else
                    {
                        log("Sync: the relay refused the envelope as a replay and reported no usable high-water mark. Nothing to reconcile (§6.1).");
                    }
                    return false;

                case RelayPushResult.TooLarge:
                    log("Sync: the relay refused the envelope as too large (§3.1); it will not be retried.");
                    return false;

                case RelayPushResult.Rejected rejected:
                    // This side composed something the relay would not shape-check. A bug here.
                    log($"Sync: the relay rejected the envelope's shape -- {rejected.Detail}. This is an engine defect, not a relay outage.");
                    return false;

                case RelayPushResult.Unauthorised:
                    log("Sync: the relay refused the pairing's token (401/403). The pairing may have been purged.");
                    return false;

                case RelayPushResult.Misconfigured misconfigured:
                    log($"Sync: {misconfigured.Detail}. Check the paired relay URL and pairing id.");
                    return false;

                case RelayPushResult.Unavailable unavailable:
                    log($"Sync: the push did not reach the relay -- {unavailable.Detail}.");
                    return false;

                default:
                    // Unreachable: RelayPushResult is closed by a private constructor. Kept as the
                    // compiler's exhaustiveness answer, not as a case that can occur.
                    return false;
            }
        };
    }
}
