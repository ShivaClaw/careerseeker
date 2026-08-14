using System.Security.Cryptography;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>
/// Seals engine→phone dashboard payloads (<see cref="SyncPayloads"/>) into v1 wire envelopes
/// and pushes them through an injected sink (Sync-Protocol.md §3–4). One publisher owns one
/// pairing's e2p direction: it holds <c>k_e2p</c> and assigns the monotonically increasing
/// <c>seq</c> the relay enforces per direction.
///
/// It is deliberately transport-agnostic and offline-testable. The sink is a delegate — in the
/// host it is backed by <see cref="RelayClient.PushAsync"/>; in the harness it is a fake that
/// records envelopes — so the sealing/sequencing logic is verified without a network. Nonce and
/// clock are injectable for the same reason; production uses a CSPRNG nonce and the wall clock.
///
/// Engine→phone envelopes carry no <c>sig</c>: the engine holds no device signing key, and the
/// receiver rejects a signature on any e2p envelope. Untrusted-text rule is upheld upstream —
/// the payload builders never carry a raw posting body.
/// </summary>
public sealed class SyncPublisher
{
    private readonly byte[] _kE2p;
    private readonly string _pairing;
    private readonly string _keyId;
    private readonly Func<string, CancellationToken, Task<bool>> _sink;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<byte[]> _nonceSource;
    private long _seq;

    public SyncPublisher(
        byte[] keyEngineToPhone,
        string pairing,
        string keyId,
        Func<string, CancellationToken, Task<bool>> sink,
        long startSeq = 0,
        Func<DateTimeOffset>? clock = null,
        Func<byte[]>? nonceSource = null)
    {
        ArgumentNullException.ThrowIfNull(keyEngineToPhone);
        if (keyEngineToPhone.Length != Protocol.KeyBytes)
            throw new ArgumentException($"k_e2p must be {Protocol.KeyBytes} bytes", nameof(keyEngineToPhone));

        _kE2p = keyEngineToPhone.ToArray();
        _pairing = pairing;
        _keyId = keyId;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _seq = startSeq;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _nonceSource = nonceSource ?? (() => RandomNumberGenerator.GetBytes(Protocol.NonceBytes));
    }

    /// <summary>The highest e2p sequence number this publisher has assigned (0 before the first push).</summary>
    public long HighestSeq => Interlocked.Read(ref _seq);

    /// <summary>
    /// Raise the counter so the next push assigns a seq above <paramref name="seq"/> — §6.1's
    /// reconciliation applied at the one moment the counter is *proved* wrong, which is a 409
    /// <c>replay_rejected</c> carrying the relay's high-water mark
    /// (<see cref="RelayPushResult.Conflict"/>).
    ///
    /// <para><b>It raises and never lowers</b>, and that asymmetry is the whole invariant. A relay
    /// that reports a <c>latest</c> below this publisher's counter is not evidence the counter ran
    /// ahead — the seqs in between may already sit in the phone's accepted history (§6.2 tracks the
    /// highest seq it accepted, and the relay's TTL purge legitimately drops rows the phone kept).
    /// Rewinding onto them would re-issue numbers the receiver refuses on sight, permanently, which
    /// is the one-sided sync death §6.1 exists to prevent. So a lower number is discarded and the
    /// call reports that it did nothing.</para>
    ///
    /// <para>Returns <c>true</c> if the counter moved. Callers use it to log "reconciled" rather
    /// than "reported", so a reader can tell the two apart.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seq"/> is outside the legal seq domain (§3.2). No shipped caller can trigger
    /// this: both numbers that reach here — a 409's <c>latest</c> and a pull page's — are range-checked
    /// inside <see cref="RelayClient"/> before they are handed over, and an unusable one arrives as
    /// null and is never passed. The guard is therefore an assertion against a future caller that
    /// skips that step, deliberately not a silent clamp: clamping would write a number this side has
    /// already judged untrustworthy straight into the counter that goes on the wire.
    /// </exception>
    public bool ReconcileTo(long seq)
    {
        if (seq < 0 || seq > Protocol.MaxSeq)
            throw new ArgumentOutOfRangeException(
                nameof(seq), seq, $"a seq must be within [0, {Protocol.MaxSeq}] (§3.2)");

        // CAS rather than a lock: PushSealedAsync assigns seqs with Interlocked.Increment and may be
        // running concurrently, so the read-compare-write has to be atomic against it. Losing the
        // race means re-reading a counter that only ever grows, so the loop terminates.
        while (true)
        {
            var current = Interlocked.Read(ref _seq);
            if (seq <= current) return false;
            if (Interlocked.CompareExchange(ref _seq, seq, current) == current) return true;
        }
    }

    /// <summary>
    /// §6.1's startup rule as a value: the seq an engine must resume its e2p counter above, given
    /// what its pairing store persisted and what the relay answered to
    /// <c>GET /pull?dir=e2p&amp;since=0</c>.
    ///
    /// <para>This is a pure function on purpose. The composition that supplies its two arguments
    /// lives in the host, needs a DPAPI-backed vault and a live relay, and therefore cannot be
    /// executed in a hermetic harness — but the <em>rule</em> can, and the rule is the part that is
    /// load-bearing. Keeping it here is what makes §6.1 testable rather than merely written down.</para>
    ///
    /// <para><b>A relay that did not answer falls back to the store, and does not stop publishing.</b>
    /// §6.1 names the store as the value and the relay read as "belt-and-suspenders should the store
    /// lag" — so the store is primary and the relay term only ever raises it. Refusing to publish
    /// because the relay was briefly unreachable would convert a transient outage into no sync at
    /// all, while the persisted mark is already correct in every case except the one the fallback
    /// cannot detect. <see cref="RelayPullResult.Unauthorised"/> gets the same treatment rather than
    /// a special one: if the token really is dead, the very next push says so with the same 401 on
    /// the path that can act on it, and blocking startup here would be a second, weaker copy of a
    /// check that already exists.</para>
    ///
    /// <para>A persisted value below zero is a corrupt store, not a position. It is floored at 0 and
    /// left for the relay term to repair — which is precisely the lagging store §6.1 anticipates.</para>
    /// </summary>
    public static long ResumeSeq(long persistedSeq, RelayPullResult relayAnswer)
    {
        ArgumentNullException.ThrowIfNull(relayAnswer);
        var floor = persistedSeq > 0 ? persistedSeq : 0;
        return relayAnswer is RelayPullResult.Ok ok && ok.Latest > floor ? ok.Latest : floor;
    }

    /// <summary>Full dashboard state, sent on engine start and on pairing.</summary>
    public Task<bool> PublishSnapshotAsync(
        Counters counters, IReadOnlyList<AppSummary> applications, IReadOnlyList<JobSummary> jobs,
        CancellationToken ct = default)
        => PushSealedAsync(SyncPayloads.Snapshot(counters, applications, jobs), ct);

    /// <summary>What changed since <paramref name="sinceSeq"/>, sent on a cycle / state transition.</summary>
    public Task<bool> PublishDeltaAsync(
        long sinceSeq, Counters counters,
        IReadOnlyList<AppSummary> changedApplications, IReadOnlyList<JobSummary> changedJobs,
        CancellationToken ct = default)
        => PushSealedAsync(SyncPayloads.Delta(sinceSeq, counters, changedApplications, changedJobs), ct);

    /// <summary>A cheap liveness beat carrying the counters, for the phone's "last seen".</summary>
    public Task<bool> PublishHeartbeatAsync(long cycle, Counters counters, CancellationToken ct = default)
    {
        var ts = Iso(_clock());
        return PushSealedAsync(SyncPayloads.Heartbeat(ts, cycle, counters), ts, ct);
    }

    /// <summary>Audit-chain verdict + recent event metadata for the phone's Evidence screen.</summary>
    public Task<bool> PublishEvidenceAsync(
        bool auditOk, long? firstBrokenSeq, int eventCount, IReadOnlyList<EvidenceEvent> events,
        CancellationToken ct = default)
        => PushSealedAsync(SyncPayloads.Evidence(auditOk, firstBrokenSeq, eventCount, events), ct);

    /// <summary>
    /// The engine's answer to a receipt it verified (§4.3.3) — the only payload that unlocks Pro on
    /// the phone. The envelope <c>ts</c> and the body's <c>acknowledged_at</c> are the same clock
    /// reading, which is the instant the engine recorded the grant; both are advisory (§6.3).
    ///
    /// Emit this ONLY for an accepted <see cref="EntitlementVerdict"/>. A rejection is an `error`
    /// (§7.2), never an ack — see <see cref="SyncPayloads.EntitlementAck"/>.
    /// </summary>
    public Task<bool> PublishEntitlementAckAsync(string productId, string? orderId = null, CancellationToken ct = default)
    {
        var ts = Iso(_clock());
        return PushSealedAsync(SyncPayloads.EntitlementAck(productId, ts, orderId), ts, ct);
    }

    private Task<bool> PushSealedAsync(byte[] plaintext, CancellationToken ct)
        => PushSealedAsync(plaintext, Iso(_clock()), ct);

    private async Task<bool> PushSealedAsync(byte[] plaintext, string ts, CancellationToken ct)
    {
        // Assign the next e2p seq up front. A failed push burns the seq (leaving a gap), which
        // the protocol's SequenceTracker treats as legitimate — the relay purges on a TTL — so we
        // never reuse a seq the phone might already have accepted.
        var seq = Interlocked.Increment(ref _seq);
        var envelope = SealE2p(_pairing, seq, ts, _keyId, _kE2p, plaintext, _nonceSource());
        return await _sink(envelope, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the flat v1 wire envelope JSON for one engine→phone payload: header fields bound as
    /// AAD, AES-256-GCM ciphertext, no signature. The <paramref name="nonce"/> MUST be unique for
    /// this key. Shape matches what the relay stores and the phone's receiver parses.
    /// </summary>
    public static string SealE2p(
        string pairing, long seq, string ts, string keyId, byte[] keyE2p, byte[] plaintext, byte[] nonce)
    {
        var header = new EnvelopeHeader(Protocol.Version, pairing, "e2p", seq, ts, keyId);
        var ciphertext = EnvelopeCodec.Seal(keyE2p, nonce, header.Aad(), plaintext);
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = Protocol.Version,
            ["pairing"] = pairing,
            ["dir"] = "e2p",
            ["seq"] = seq,
            ["ts"] = ts,
            ["key_id"] = keyId,
            ["nonce"] = Base64Url.Encode(nonce),
            ["ciphertext"] = Base64Url.Encode(ciphertext),
        });
    }

    /// <summary>Seconds-precision UTC Zulu, matching the vector convention (e.g. 2026-06-11T14:02:11Z).</summary>
    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
}
