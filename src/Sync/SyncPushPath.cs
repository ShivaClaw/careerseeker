namespace SeekerSvc.Sync;

/// <summary>
/// The durable half of §6.1's engine→phone high-water mark, named as the one narrow thing the push
/// path needs from the pairing store.
///
/// <para><b>Why an interface here and not in <c>src/Engine</c>.</b> The only production implementation
/// is <c>SyncPairingVault</c>, which is DPAPI-backed and Windows-only, and that is exactly why this
/// abstraction has to live on the pure side of the line: <c>src/Sync</c> is platform-free so it can be
/// verified offline and mirrored on Android, and an interface declared next to the DPAPI type would
/// drag the composition back into a place no harness can reach.</para>
///
/// <para><b>Why one method and not the vault's whole surface.</b> The push path records an outbound
/// mark and does nothing else with the store — it does not load, save or delete a pairing, and the
/// inbound direction's <c>RecordP2eSeq</c> reaches <see cref="InboundPump"/> as its own
/// <c>onAccepted</c> delegate rather than through here. A wider interface would let this path do
/// things it has no business doing and would make the fake a harness supplies bigger than the
/// behaviour under test.</para>
/// </summary>
public interface IE2pSeqStore
{
    /// <summary>
    /// Persist a new outbound high-water mark. Implementations are required to be monotonic — a
    /// value at or below the stored one is ignored rather than written — because a rewound counter
    /// makes the relay reject everything this engine sends next (§6.1).
    /// </summary>
    void RecordE2pSeq(long seq);
}

/// <summary>
/// The engine→phone push path, composed: a <see cref="SyncPublisher"/> wired to a
/// <see cref="RelaySink"/> that persists through an <see cref="IE2pSeqStore"/> and reconciles the
/// publisher's own counter (§6.1).
///
/// <para><b>The gap this closes, measured rather than suspected.</b> <see cref="RelaySink"/> made the
/// sink's <em>decision rule</em> testable, and the previous slice proved the rule is applied. But the
/// <em>wiring</em> — which delegate is attached to which collaborator — stayed in <c>Program.cs</c>'s
/// <c>BuildSyncBridge</c>, a host method that returns null without a DPAPI pairing vault and has
/// therefore never executed in any harness or on any CI runner. The consequence was measured: replacing
/// <c>persistSeq: seq =&gt; vault.RecordE2pSeq(seq)</c> with <c>persistSeq: _ =&gt; { }</c> built with
/// 0 warnings / 0 errors and left <c>SyncHarness</c> at 277 passed / 0 failed. An engine that had
/// silently stopped persisting its high-water mark would have failed no test in this repo.</para>
///
/// <para><b>What moving the wiring here does and does not buy.</b> It does not make the composition
/// executable — nothing here can construct a DPAPI vault or reach a relay. What it does is shrink the
/// unexecuted remainder from <em>five delegate bodies</em>, each of which could be quietly emptied, to
/// <em>four argument identities</em> at a single call site: that <paramref name="seqStore"/> receives
/// the vault, that <paramref name="push"/> closes over the pairing's own relay token, that
/// <paramref name="log"/> reaches the operator, and that <paramref name="startSeq"/> receives §6.1's
/// reconciled resume value. Those four remain local-gate-only claims and are recorded as such; every
/// behaviour underneath them is now assertable offline.</para>
///
/// <para><b>The mutual reference is the load-bearing part.</b> The publisher assigns the seq and the
/// sink reports on it, so each needs the other. That knot is the reason this composition could not
/// simply be inlined into a harness alongside the host's version — there would then be two copies of
/// it and the harness would be testing its own. Tying it once, here, is what makes the harness's
/// subject the shipping code.</para>
/// </summary>
public static class SyncPushPath
{
    /// <summary>
    /// Ties the publisher and its relay sink together and returns the publisher, ready to publish.
    /// </summary>
    /// <param name="keyEngineToPhone">The pairing's k_e2p.</param>
    /// <param name="pairing">The pairing id that goes on the wire.</param>
    /// <param name="keyId">The active key_id; a wrong value is a silent total outage, not a degraded mode.</param>
    /// <param name="push">Sends the sealed envelope; in production <see cref="RelayClient.PushAsync"/> bound to the pairing's relay token.</param>
    /// <param name="seqStore">Durable outbound high-water mark; in production the DPAPI pairing vault.</param>
    /// <param name="log">Operator-facing line sink.</param>
    /// <param name="startSeq">§6.1's resume value — <see cref="SyncPublisher.ResumeSeq"/> of the persisted mark and the relay's.</param>
    public static SyncPublisher Create(
        byte[] keyEngineToPhone,
        string pairing,
        string keyId,
        Func<string, CancellationToken, Task<RelayPushResult>> push,
        IE2pSeqStore seqStore,
        Action<string> log,
        long startSeq)
    {
        ArgumentNullException.ThrowIfNull(push);
        ArgumentNullException.ThrowIfNull(seqStore);
        ArgumentNullException.ThrowIfNull(log);

        // `publisherRef` exists because the publisher and its sink are mutually referential: the seq
        // is assigned before the sink runs, so reading HighestSeq from inside the sink reports the
        // seq currently in flight. Until the assignment below lands it is null, and RelaySink's
        // `pushedSeq` returns long? for exactly that window — laundering it into a 0 would persist a
        // false high-water mark on the very first push.
        SyncPublisher? publisherRef = null;
        var publisher = new SyncPublisher(
            keyEngineToPhone,
            pairing,
            keyId,
            sink: RelaySink.Create(
                push: push,
                pushedSeq: () => publisherRef?.HighestSeq,
                persistSeq: seqStore.RecordE2pSeq,
                reconcileTo: seq => publisherRef?.ReconcileTo(seq) ?? false,
                log: log),
            startSeq: startSeq);
        publisherRef = publisher;
        return publisher;
    }
}
