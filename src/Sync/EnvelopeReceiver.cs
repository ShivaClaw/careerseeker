using System.Security.Cryptography;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>An envelope as parsed off the wire, fields still in their string forms.</summary>
public sealed record ReceivedEnvelope(
    int V, string Pairing, string Dir, long Seq, string Ts, string KeyId,
    string Nonce, string Ciphertext, string? Sig);

public sealed record ReceiveResult(SyncError? Error, string? Kind, byte[]? Plaintext)
{
    public bool Accepted => Error is null;
}

/// <summary>
/// The v1 receiving state machine — check order is part of the protocol, not an
/// implementation detail, because rejecting for the wrong reason usually means a check
/// fired earlier than intended and the real one is untested.
///
/// Order: version → key_id → structural decode → size → signature placement → replay →
/// decrypt → kind → signature requirement/verification. Cheap header checks precede
/// crypto; crypto precedes payload interpretation; the sequence number is committed only
/// after every check passes, so garbage cannot burn sequence numbers.
/// </summary>
/// <param name="activeKeyId">The pairing's current key id; anything else is <c>key_unknown</c>.</param>
/// <param name="deviceSigPub">The paired device's ECDSA public key, for state-changing p2e kinds.</param>
/// <param name="resume">Persisted highest-accepted seq per direction, restored at construction
/// (§6.1/§6.2). Without it the replay mark is empty on every process start, and since the *relay*
/// chooses what a page contains, an engine that restarts can be handed an already-applied
/// <c>entitlement</c> or <c>outcome</c> again and will accept it: the mark the pairing vault persists
/// protects nothing unless the receiver is built from it. Supplied at construction rather than through
/// a setter so trust state cannot be moved after the receiver is in use, and the tracker raises only,
/// so a wrong value here can over-refuse but never under-refuse.</param>
public sealed class EnvelopeReceiver(
    string activeKeyId, byte[]? deviceSigPub = null, IReadOnlyDictionary<string, long>? resume = null)
{
    private readonly SequenceTracker _seq = Restored(resume);

    public long HighestAccepted(string dir) => _seq.HighestAccepted(dir);

    private static SequenceTracker Restored(IReadOnlyDictionary<string, long>? resume)
    {
        var tracker = new SequenceTracker();
        if (resume is not null)
            foreach (var (dir, seq) in resume) tracker.Resume(dir, seq);
        return tracker;
    }

    public ReceiveResult Receive(ReceivedEnvelope env, Func<string, byte[]> keyForDir)
    {
        if (env.V != Protocol.Version) return Reject(SyncError.VersionUnsupported);

        // Revocation is an explicit check, not a side effect of cryptography: a superseded
        // pairing whose derived key still decrypts is exactly what the tag cannot catch.
        if (!string.Equals(env.KeyId, activeKeyId, StringComparison.Ordinal))
            return Reject(SyncError.KeyUnknown);

        if (!Base64Url.TryDecode(env.Nonce, out var nonce) || nonce.Length != Protocol.NonceBytes)
            return Reject(SyncError.DecryptFailed);
        if (!Base64Url.TryDecode(env.Ciphertext, out var ciphertext))
            return Reject(SyncError.DecryptFailed);
        if (ciphertext.Length > Protocol.MaxEnvelopeBytes)
            return Reject(SyncError.TooLarge);

        // The engine holds no signing key, so sig on an e2p envelope is always wrong,
        // and it is detectable before spending any crypto.
        if (env.Dir == "e2p" && env.Sig is not null)
            return Reject(SyncError.BadSignature);

        if (env.Seq <= _seq.HighestAccepted(env.Dir))
            return Reject(SyncError.ReplayRejected);

        var header = new EnvelopeHeader(env.V, env.Pairing, env.Dir, env.Seq, env.Ts, env.KeyId);
        var aad = header.Aad();

        byte[] plaintext;
        try
        {
            plaintext = EnvelopeCodec.Open(keyForDir(env.Dir), nonce, aad, ciphertext);
        }
        catch (CryptographicException)
        {
            return Reject(SyncError.DecryptFailed);
        }

        string? kind;
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            kind = doc.RootElement.TryGetProperty("kind", out var k) ? k.GetString() : null;
        }
        catch (JsonException)
        {
            return Reject(SyncError.UnknownKind);
        }

        // Reserved-before-signature on purpose: a reserved L2 kind is rejected as unknown
        // even if beautifully signed — the phone does not get engine control in v1.
        if (kind is null || !Protocol.ShippingKinds.Contains(kind))
            return Reject(SyncError.UnknownKind);

        if (env.Dir == "p2e" && Protocol.StateChangingKinds.Contains(kind))
        {
            if (env.Sig is null) return Reject(SyncError.BadSignature);
            if (deviceSigPub is null) return Reject(SyncError.BadSignature);
            if (!Base64Url.TryDecode(env.Sig, out var sig)) return Reject(SyncError.BadSignature);

            var input = DeviceSignature.SigInput(aad, env.Nonce, ciphertext);
            if (!DeviceSignature.Verify(deviceSigPub, input, sig))
                return Reject(SyncError.BadSignature);
        }

        _seq.Accept(env.Dir, env.Seq); // committed only after every check passed
        return new ReceiveResult(null, kind, plaintext);
    }

    private static ReceiveResult Reject(SyncError e) => new(e, null, null);
}
