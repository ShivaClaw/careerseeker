using System.Security.Cryptography;
using System.Text;

namespace SeekerSvc.Sync;

/// <summary>
/// The envelope-level device signature (Sync-Protocol.md §5.4): ECDSA P-256 / SHA-256,
/// raw 64-byte r||s encoding, over exact wire artifacts — the AAD string, the nonce, and
/// the ciphertext hash. Nothing is canonicalised, which is the entire reason the
/// signature lives at the envelope rather than inside the JSON body.
/// </summary>
public static class DeviceSignature
{
    public const int SignatureBytes = 64;

    /// <summary>The exact ASCII string that gets signed.</summary>
    public static string SigInput(string aad, string nonceB64u, ReadOnlySpan<byte> ciphertext) =>
        $"{Protocol.CommandSigPrefix}|{aad}|{nonceB64u}|{Convert.ToHexString(SHA256.HashData(ciphertext)).ToLowerInvariant()}";

    public static bool Verify(ReadOnlySpan<byte> deviceSigPubUncompressed, string sigInput, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SignatureBytes) return false;
        if (deviceSigPubUncompressed.Length != PairingCrypto.UncompressedPointBytes || deviceSigPubUncompressed[0] != 0x04) return false;

        try
        {
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = deviceSigPubUncompressed[1..33].ToArray(),
                    Y = deviceSigPubUncompressed[33..].ToArray(),
                },
            });
            // .NET's default ECDsa signature format is IEEE P1363 fixed-field r||s —
            // exactly the wire encoding §5.4 specifies. No DER anywhere.
            return ecdsa.VerifyData(Encoding.ASCII.GetBytes(sigInput), signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false; // point not on curve, malformed key — not a valid signature either way
        }
    }

    /// <summary>Fingerprint recorded in the audit chain: SHA-256 of the uncompressed point, first 16 hex chars.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> deviceSigPubUncompressed) =>
        Convert.ToHexString(SHA256.HashData(deviceSigPubUncompressed)).ToLowerInvariant()[..16];
}
