using System.Security.Cryptography;
using System.Text;

namespace SeekerSvc.Sync;

/// <summary>The authenticated-but-not-encrypted envelope header (Sync-Protocol.md §3).</summary>
public sealed record EnvelopeHeader(int V, string Pairing, string Dir, long Seq, string Ts, string KeyId)
{
    /// <summary>
    /// Additional authenticated data, §4.1. A fixed ASCII string rather than canonical
    /// JSON, because two independent implementations must agree byte-for-byte and JSON
    /// canonicalization is a classic cross-language mismatch. Field order is normative.
    /// </summary>
    public string Aad() => $"v={V}|pairing={Pairing}|dir={Dir}|seq={Seq}|ts={Ts}|key_id={KeyId}";
}

/// <summary>
/// Seal/open for Sync Protocol v1 envelopes: AES-256-GCM, header bound in as AAD,
/// 16-byte tag appended to the ciphertext.
/// </summary>
public static class EnvelopeCodec
{
    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, string aad, ReadOnlySpan<byte> plaintext)
    {
        if (key.Length != Protocol.KeyBytes) throw new ArgumentException("key must be 32 bytes", nameof(key));
        if (nonce.Length != Protocol.NonceBytes) throw new ArgumentException("nonce must be 12 bytes", nameof(nonce));

        var output = new byte[plaintext.Length + Protocol.TagBytes];
        using var gcm = new AesGcm(key, Protocol.TagBytes);
        gcm.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length),
            output.AsSpan(plaintext.Length, Protocol.TagBytes), Encoding.ASCII.GetBytes(aad));
        return output;
    }

    /// <summary>Throws <see cref="CryptographicException"/> on any authentication failure.</summary>
    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, string aad, ReadOnlySpan<byte> sealedBytes)
    {
        if (key.Length != Protocol.KeyBytes) throw new ArgumentException("key must be 32 bytes", nameof(key));
        if (nonce.Length != Protocol.NonceBytes) throw new ArgumentException("nonce must be 12 bytes", nameof(nonce));
        if (sealedBytes.Length < Protocol.TagBytes) throw new CryptographicException("ciphertext shorter than the tag");

        var plaintext = new byte[sealedBytes.Length - Protocol.TagBytes];
        using var gcm = new AesGcm(key, Protocol.TagBytes);
        gcm.Decrypt(nonce, sealedBytes[..^Protocol.TagBytes], sealedBytes[^Protocol.TagBytes..],
            plaintext, Encoding.ASCII.GetBytes(aad));
        return plaintext;
    }
}

/// <summary>
/// Tracks the highest accepted sequence number per direction. Gaps are legitimate — the
/// relay purges on a TTL — so a gap must not stall the stream. Only regression is a replay.
/// </summary>
public sealed class SequenceTracker
{
    private readonly Dictionary<string, long> _highest = new(StringComparer.Ordinal);

    public long HighestAccepted(string dir) => _highest.GetValueOrDefault(dir, 0L);

    /// <summary>True and records the seq if acceptable; false if it is a replay.</summary>
    public bool Accept(string dir, long seq)
    {
        if (seq <= HighestAccepted(dir)) return false;
        _highest[dir] = seq;
        return true;
    }
}
