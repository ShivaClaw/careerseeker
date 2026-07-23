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
