using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SeekerSvc.Sync;

/// <summary>Everything §5.2 derives from one pairing exchange.</summary>
public sealed record PairingKeys(
    byte[] KeyEngineToPhone,
    byte[] KeyPhoneToEngine,
    string RelayToken,
    string ConfirmCode);

/// <summary>
/// Pairing key agreement for suite <c>p256-hkdf-sha256</c> (Sync-Protocol.md §5.2).
///
/// The derivation always goes through <c>ikm = concat(shared secrets)</c>, even while
/// there is exactly one. That is deliberate and load-bearing: the post-quantum hybrid
/// suite appends the ML-KEM shared secret to the same concatenation, so migrating is a
/// suite bump instead of a breaking change for every paired device. Do not "simplify"
/// the concat away.
/// </summary>
public static class PairingCrypto
{
    /// <summary>Uncompressed P-256 point: 0x04 || X(32) || Y(32).</summary>
    public const int UncompressedPointBytes = 65;

    public static ECDiffieHellman CreateKeyPair() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] ExportUncompressedPublic(ECDiffieHellman key)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        var point = new byte[UncompressedPointBytes];
        point[0] = 0x04;
        p.Q.X!.CopyTo(point, 1);
        p.Q.Y!.CopyTo(point, 33);
        return point;
    }

    /// <summary>Raw ECDH shared secret: the 32-byte X coordinate, per §5.2.</summary>
    public static byte[] ComputeSharedSecret(ECDiffieHellman own, ReadOnlySpan<byte> peerUncompressed)
    {
        using var peer = ImportPeerPublic(peerUncompressed);
        return own.DeriveRawSecretAgreement(peer.PublicKey);
    }

    public static ECDiffieHellman ImportPeerPublic(ReadOnlySpan<byte> uncompressed)
    {
        if (uncompressed.Length != UncompressedPointBytes || uncompressed[0] != 0x04)
            throw new ArgumentException("expected a 65-byte uncompressed P-256 point", nameof(uncompressed));
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uncompressed[1..33].ToArray(), Y = uncompressed[33..].ToArray() },
        });
    }

    /// <summary>Derive every §5.2 output from the suite's shared secrets and the QR secret.</summary>
    public static PairingKeys Derive(IReadOnlyList<byte[]> sharedSecrets, byte[] oneTimeSecret)
    {
        if (sharedSecrets.Count == 0) throw new ArgumentException("at least one shared secret", nameof(sharedSecrets));

        var ikm = Concat(sharedSecrets);
        var confirmBytes = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 4, oneTimeSecret, Encoding.ASCII.GetBytes(Protocol.InfoConfirm));
        var confirm = (BinaryPrimitives.ReadUInt32BigEndian(confirmBytes) % 1_000_000u).ToString("D6");

        return new PairingKeys(
            KeyEngineToPhone: HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, Protocol.KeyBytes, oneTimeSecret, Encoding.ASCII.GetBytes(Protocol.InfoEngineToPhone)),
            KeyPhoneToEngine: HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, Protocol.KeyBytes, oneTimeSecret, Encoding.ASCII.GetBytes(Protocol.InfoPhoneToEngine)),
            RelayToken: Base64Url.Encode(HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, Protocol.KeyBytes, oneTimeSecret, Encoding.ASCII.GetBytes(Protocol.InfoRelayToken))),
            ConfirmCode: confirm);
    }

    /// <summary>
    /// Provisional relay token (§5.2.1): keyed on the one-time secret alone, so the engine
    /// can bootstrap the relay channel before the phone's key exists. Exactly as secret as
    /// the QR itself, and replaced by the ikm-derived token after completion.
    /// </summary>
    public static string ProvisionalRelayToken(byte[] oneTimeSecret) =>
        Base64Url.Encode(HKDF.DeriveKey(
            HashAlgorithmName.SHA256, oneTimeSecret, Protocol.KeyBytes,
            Encoding.ASCII.GetBytes(Protocol.BootstrapSalt), Encoding.ASCII.GetBytes(Protocol.InfoRelayToken)));

    /// <summary>AAD for the pairing completion message (§5.2.2). Binds phone_pub so a relay cannot swap keys.</summary>
    public static string CompletionAad(string pairing, string suite, string phonePubB64u) =>
        $"{Protocol.PairAadPrefix}|{pairing}|{suite}|{phonePubB64u}";

    private static byte[] Concat(IReadOnlyList<byte[]> parts)
    {
        var total = parts.Sum(p => p.Length);
        var ikm = new byte[total];
        var offset = 0;
        foreach (var p in parts) { p.CopyTo(ikm, offset); offset += p.Length; }
        return ikm;
    }
}
