using System.Security.Cryptography;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>The QR payload the desktop renders (Sync-Protocol.md §5.2).</summary>
public sealed record PairingInvite(string V, string Suite, string Pairing, string EnginePub, string Relay, string Secret)
{
    public string ToQrJson() => JsonSerializer.Serialize(new
    {
        v = 1, suite = Suite, pairing = Pairing, engine_pub = EnginePub, relay = Relay, secret = Secret,
    });
}

/// <summary>Result of completing a pairing: the engine now holds everything it needs to talk to the phone.</summary>
public sealed record PairedState(
    string Pairing,
    string Suite,
    byte[] KeyEngineToPhone,
    byte[] KeyPhoneToEngine,
    byte[] DeviceSigPub,
    string RelayToken,
    string ConfirmCode);

/// <summary>
/// The engine side of the pairing handshake (Sync-Protocol.md §5.2). Pure and offline —
/// no network, no disk. The caller (engine host) owns transport (RelayClient) and
/// persistence (the DPAPI vault), so this stays unit-testable and is exercised against
/// the shared pairing vectors in SyncHarness.
///
/// A pairing manager instance is single-use: it holds the ephemeral engine keypair and the
/// one-time secret, and burns the secret on the first valid completion.
/// </summary>
public sealed class PairingManager : IDisposable
{
    private readonly ECDiffieHellman _engineKey;
    private readonly byte[] _oneTimeSecret;
    private readonly DateTimeOffset _expiresAt;
    private bool _secretBurned;

    public string Pairing { get; }
    public string RelayUrl { get; }
    public string Suite => Protocol.Suite;

    public PairingManager(string relayUrl, TimeSpan? ttl = null, ECDiffieHellman? engineKey = null, byte[]? oneTimeSecret = null, string? pairingId = null)
    {
        RelayUrl = relayUrl;
        _engineKey = engineKey ?? PairingCrypto.CreateKeyPair();
        _oneTimeSecret = oneTimeSecret ?? RandomNumberGenerator.GetBytes(32);
        _expiresAt = DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromSeconds(60));
        Pairing = pairingId ?? "p_" + Base64Url.Encode(RandomNumberGenerator.GetBytes(12))[..16];
    }

    /// <summary>The QR the desktop shows. Engine public key is not secret; the secret is single-use.</summary>
    public PairingInvite CreateInvite() => new(
        V: "1", Suite: Suite, Pairing: Pairing,
        EnginePub: Base64Url.Encode(PairingCrypto.ExportUncompressedPublic(_engineKey)),
        Relay: RelayUrl,
        Secret: Base64Url.Encode(_oneTimeSecret));

    /// <summary>Provisional relay token to bootstrap the channel before the phone's key exists (§5.2.1).</summary>
    public string ProvisionalRelayToken() => PairingCrypto.ProvisionalRelayToken(_oneTimeSecret);

    /// <summary>
    /// Complete pairing from the phone's <c>POST /pair</c> body. Verifies the sealed
    /// completion, extracts the device signing key, and burns the one-time secret. Returns
    /// null (with a reason) on any failure — a losing race, an expired secret, a bad key,
    /// or a completion that does not decrypt.
    /// </summary>
    public PairedState? CompletePairing(string completionJson, out string? error)
    {
        error = null;
        if (_secretBurned) { error = "secret already used"; return null; }
        if (DateTimeOffset.UtcNow > _expiresAt) { error = "pairing window expired"; return null; }

        JsonElement root;
        try { root = JsonDocument.Parse(completionJson).RootElement; }
        catch (JsonException) { error = "malformed completion"; return null; }

        if (root.GetProperty("suite").GetString() != Suite) { error = "suite mismatch"; return null; }
        var phonePubB64u = root.GetProperty("phone_pub").GetString()!;
        if (!Base64Url.TryDecode(phonePubB64u, out var phonePub)) { error = "bad phone_pub"; return null; }
        if (!Base64Url.TryDecode(root.GetProperty("nonce").GetString(), out var nonce)) { error = "bad nonce"; return null; }
        if (!Base64Url.TryDecode(root.GetProperty("ciphertext").GetString(), out var ciphertext)) { error = "bad ciphertext"; return null; }

        byte[] sharedSecret;
        try { sharedSecret = PairingCrypto.ComputeSharedSecret(_engineKey, phonePub); }
        catch (Exception) { error = "phone_pub not on curve"; return null; }

        var keys = PairingCrypto.Derive(new[] { sharedSecret }, _oneTimeSecret);
        var aad = PairingCrypto.CompletionAad(Pairing, Suite, phonePubB64u);

        byte[] payload;
        try { payload = EnvelopeCodec.Open(keys.KeyPhoneToEngine, nonce, aad, ciphertext); }
        catch (CryptographicException) { error = "completion did not decrypt (key swap or corruption)"; return null; }

        var payloadRoot = JsonDocument.Parse(payload).RootElement;
        var deviceSigPubB64u = payloadRoot.GetProperty("device_sig_pub").GetString()!;
        if (!Base64Url.TryDecode(deviceSigPubB64u, out var deviceSigPub)
            || deviceSigPub.Length != PairingCrypto.UncompressedPointBytes || deviceSigPub[0] != 0x04)
        {
            error = "bad device signing key";
            return null;
        }

        _secretBurned = true; // single-use: a second completion, even valid, is refused
        return new PairedState(Pairing, Suite, keys.KeyEngineToPhone, keys.KeyPhoneToEngine, deviceSigPub, keys.RelayToken, keys.ConfirmCode);
    }

    public void Dispose() => _engineKey.Dispose();
}
