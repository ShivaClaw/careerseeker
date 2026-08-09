using SeekerSvc.Dispatcher;
using SeekerSvc.Sync;

// Both SeekerSvc.Dispatcher and SeekerSvc.Sync define a Base64Url. Bind explicitly to the protocol's
// encoder: these bytes go on the wire, and the two implementations must not be assumed interchangeable.
using Base64Url = SeekerSvc.Sync.Base64Url;

namespace SeekerSvc.Engine;

/// <summary>
/// Everything the engine must remember about a paired phone to keep publishing across restarts.
///
/// The two sequence numbers are not bookkeeping — they are correctness (Sync-Protocol.md §6.1, both
/// directions). An engine that resumed <c>e2p</c> at 1 would have every envelope it sent rejected as a
/// replay, including the recovery snapshot, so the phone would silently stop updating. An engine that
/// resumed <c>p2e</c> at 0 would re-accept an entitlement or outcome it has already applied.
/// </summary>
public sealed record SyncPairing(
    string Pairing,
    string Suite,
    string RelayUrl,
    byte[] KeyEngineToPhone,
    byte[] KeyPhoneToEngine,
    byte[] DeviceSigPub,
    string RelayToken,
    string KeyId,
    long LastE2pSeq,
    long LastP2eSeq);

/// <summary>
/// DPAPI-backed store for the pairing above, scoped to the current Windows user like the OAuth and BYOK
/// vaults. Keys never leave this machine and are never logged: <see cref="Describe"/> exists so the
/// dashboard and console can say something true about the pairing without printing key material.
///
/// This lives in <c>src/Engine</c> rather than <c>src/Sync</c> on purpose. <c>src/Sync</c> is pure and
/// platform-free so it can be verified offline and mirrored on Android; DPAPI is Windows-only
/// persistence and belongs on the composition side of that line.
/// </summary>
public sealed class SyncPairingVault
{
    private readonly DpapiSecretVault _vault;

    public SyncPairingVault(string path) => _vault = new DpapiSecretVault(path);

    public bool Exists => _vault.Exists;

    public SyncPairing? Load()
    {
        if (!_vault.Exists) return null;
        var v = _vault.Load();

        if (!v.TryGetValue("pairing", out var pairing) ||
            !v.TryGetValue("suite", out var suite) ||
            !v.TryGetValue("relay_url", out var relayUrl) ||
            !v.TryGetValue("k_e2p", out var kE2p) ||
            !v.TryGetValue("k_p2e", out var kP2e) ||
            !v.TryGetValue("device_sig_pub", out var sigPub) ||
            !v.TryGetValue("relay_token", out var token))
        {
            return null; // a partially written vault is treated as no pairing, never as a usable one
        }

        if (!Base64Url.TryDecode(kE2p, out var kE2pBytes)) return null;
        if (!Base64Url.TryDecode(kP2e, out var kP2eBytes)) return null;
        if (!Base64Url.TryDecode(sigPub, out var sigPubBytes)) return null;

        // key_id is part of what the two sides agreed at pairing: the receiver rejects any envelope
        // whose key_id is not the active one, so a wrong value here is a silent total outage, not a
        // degraded mode. Older vaults that predate the field fall back to the pairing's default.
        var keyId = v.TryGetValue("key_id", out var k) && !string.IsNullOrWhiteSpace(k) ? k : DefaultKeyId;

        return new SyncPairing(
            pairing, suite, relayUrl, kE2pBytes, kP2eBytes, sigPubBytes, token, keyId,
            LongOrZero(v, "last_e2p_seq"),
            LongOrZero(v, "last_p2e_seq"));
    }

    public void Save(SyncPairing p) => _vault.Save(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pairing"] = p.Pairing,
        ["suite"] = p.Suite,
        ["relay_url"] = p.RelayUrl,
        ["k_e2p"] = Base64Url.Encode(p.KeyEngineToPhone),
        ["k_p2e"] = Base64Url.Encode(p.KeyPhoneToEngine),
        ["device_sig_pub"] = Base64Url.Encode(p.DeviceSigPub),
        ["relay_token"] = p.RelayToken,
        ["key_id"] = p.KeyId,
        ["last_e2p_seq"] = p.LastE2pSeq.ToString(),
        ["last_p2e_seq"] = p.LastP2eSeq.ToString(),
    });

    /// <summary>The key_id a fresh pairing starts on. Rotation replaces it; the vault carries it.</summary>
    public const string DefaultKeyId = "k1";

    /// <summary>
    /// Persist a new outbound high-water mark. Monotonic by construction: a lower value is ignored
    /// rather than written, so a late or out-of-order caller cannot rewind the counter and cause the
    /// relay to reject everything the engine sends next.
    /// </summary>
    public void RecordE2pSeq(long seq)
    {
        var current = Load();
        if (current is null || seq <= current.LastE2pSeq) return;
        Save(current with { LastE2pSeq = seq });
    }

    /// <summary>Same monotonic rule for the inbound direction (§6.1 applies both ways).</summary>
    public void RecordP2eSeq(long seq)
    {
        var current = Load();
        if (current is null || seq <= current.LastP2eSeq) return;
        Save(current with { LastP2eSeq = seq });
    }

    public void Delete() => _vault.Delete();

    /// <summary>
    /// A human-readable line that contains no key material — safe for the console and the dashboard.
    /// </summary>
    public static string Describe(SyncPairing p) =>
        $"pairing {p.Pairing} via {p.RelayUrl} (suite {p.Suite}, e2p seq {p.LastE2pSeq}, p2e seq {p.LastP2eSeq})";

    private static long LongOrZero(IReadOnlyDictionary<string, string> v, string key) =>
        v.TryGetValue(key, out var raw) && long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 0;
}
