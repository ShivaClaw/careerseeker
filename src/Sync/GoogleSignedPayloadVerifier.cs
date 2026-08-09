using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>Why an entitlement payload was rejected. Each reason is distinct and tested (P4 §2.3).</summary>
public enum EntitlementReject
{
    None,
    /// <summary>The RSA signature does not verify over the exact original_json bytes (or is unparseable).</summary>
    SignatureInvalid,
    /// <summary>original_json is not a JSON object, or a required field is missing / the wrong type.</summary>
    Malformed,
    /// <summary>packageName is not the configured applicationId.</summary>
    WrongPackage,
    /// <summary>productId is not in the configured Pro product set.</summary>
    WrongProduct,
    /// <summary>purchaseState is not the PURCHASED value (0 in the raw JSON).</summary>
    NotPurchased,
}

/// <summary>
/// The outcome of verifying one Google-signed purchase payload. On acceptance it carries the
/// fields the engine audits (product, order, acknowledged); on rejection it carries the reason.
/// </summary>
public sealed record EntitlementVerdict(
    bool Accepted, EntitlementReject Reason, string? ProductId, string? OrderId, bool Acknowledged)
{
    public static EntitlementVerdict Reject(EntitlementReject reason) => new(false, reason, null, null, false);
}

/// <summary>
/// The verification strategy seam (spec §6.3). One shipping strategy, <see cref="GoogleSignedPayloadVerifier"/>,
/// verifies Google Play's own signature offline (gate P0-WORKER option C). A future
/// <c>DeveloperApiVerifier</c> — the TLS Play Developer API path, no SHA-1 — is a named, unbuilt seam:
/// swapping it is a configuration change, not a rewrite, because the envelope shape and the flag it
/// feeds do not change.
/// </summary>
public interface IEntitlementVerifier
{
    EntitlementVerdict Verify(string originalJson, string signatureStandardB64);
}

/// <summary>
/// Verifies the {original_json, signature} body of an `entitlement` envelope (Sync-Protocol §4.3.2)
/// against Google Play's published license public key. The phone is a courier for a Google-signed
/// assertion; this class is the verifier, so a rooted phone cannot forge Pro.
///
/// Pure and dependency-free: no store, no clock, no network. The RSA public key, the expected
/// applicationId, and the Pro product-id set are all constructor arguments — the production Play
/// "License Key" only exists once the Play app is created and slots in then (P4 has no account yet).
///
/// Order of checks matches Sync-Protocol §4.3.2 / Entitlement-Architecture "How C works": RSA
/// signature over the exact original_json bytes first, then packageName, productId, purchaseState.
/// SHA-1 is Google's fixed IAB format, not a choice here (assessed in Entitlement-Architecture
/// §"weakness 1"); the native .NET verify carries no dependency.
/// </summary>
public sealed class GoogleSignedPayloadVerifier : IEntitlementVerifier
{
    /// <summary>purchaseState in the RAW original_json: 0 == PURCHASED. NOTE this is the JSON encoding,
    /// which differs from the Java <c>Purchase.getPurchaseState()</c> API (that remaps PURCHASED to 1).
    /// The engine verifies the raw JSON string, so it reads the JSON value. Confirm against a real
    /// purchase on account day.</summary>
    public const int PurchasedStateInJson = 0;

    private readonly byte[] _publicKeySpki;
    private readonly string _expectedPackageName;
    private readonly IReadOnlySet<string> _productIds;
    private readonly int _purchasedState;

    /// <param name="publicKeySpkiBase64">Play Console "License Key for This Application": a base64
    /// X.509 SubjectPublicKeyInfo (STANDARD base64). Validated eagerly so a bad key fails at wiring.</param>
    /// <param name="expectedPackageName">The permanent applicationId (gate P4-APPID: app.careerseeker.dashboard).</param>
    /// <param name="productIds">The Pro product-id set (P-MONEY: pro_unlock, INAPP).</param>
    /// <param name="purchasedState">The raw-JSON purchaseState meaning PURCHASED; default 0.</param>
    public GoogleSignedPayloadVerifier(
        string publicKeySpkiBase64, string expectedPackageName, IReadOnlySet<string> productIds,
        int purchasedState = PurchasedStateInJson)
    {
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64))
            throw new ArgumentException("Play license public key is required.", nameof(publicKeySpkiBase64));
        try { _publicKeySpki = Convert.FromBase64String(publicKeySpkiBase64); }
        catch (FormatException ex) { throw new ArgumentException("Play license public key is not valid base64.", nameof(publicKeySpkiBase64), ex); }
        // Fail fast on a malformed key: import once here so a wiring mistake is not deferred to a purchase.
        using (var probe = RSA.Create())
        {
            try { probe.ImportSubjectPublicKeyInfo(_publicKeySpki, out _); }
            catch (CryptographicException ex) { throw new ArgumentException("Play license public key is not an X.509 SubjectPublicKeyInfo.", nameof(publicKeySpkiBase64), ex); }
        }
        _expectedPackageName = expectedPackageName ?? throw new ArgumentNullException(nameof(expectedPackageName));
        _productIds = productIds is { Count: > 0 } ? productIds : throw new ArgumentException("At least one product id is required.", nameof(productIds));
        _purchasedState = purchasedState;
    }

    public EntitlementVerdict Verify(string originalJson, string signatureStandardB64)
    {
        if (originalJson is null || signatureStandardB64 is null)
            return EntitlementVerdict.Reject(EntitlementReject.Malformed);

        // 1. RSA signature over the EXACT original_json bytes. The signature is Play's standard base64
        //    (not the envelope's base64url); a malformed signature is a failed signature, not a crash.
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureStandardB64); }
        catch (FormatException) { return EntitlementVerdict.Reject(EntitlementReject.SignatureInvalid); }

        bool signatureOk;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(_publicKeySpki, out _);
            signatureOk = rsa.VerifyData(
                Encoding.UTF8.GetBytes(originalJson), signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException) { return EntitlementVerdict.Reject(EntitlementReject.SignatureInvalid); }
        if (!signatureOk) return EntitlementVerdict.Reject(EntitlementReject.SignatureInvalid);

        // 2. Parse the now-trusted JSON and read the fields.
        string? packageName, productId, orderId;
        int purchaseState;
        bool acknowledged;
        try
        {
            using var doc = JsonDocument.Parse(originalJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return EntitlementVerdict.Reject(EntitlementReject.Malformed);
            if (!TryGetString(root, "packageName", out packageName)) return EntitlementVerdict.Reject(EntitlementReject.Malformed);
            if (!TryGetString(root, "productId", out productId)) return EntitlementVerdict.Reject(EntitlementReject.Malformed);
            if (!TryGetInt(root, "purchaseState", out purchaseState)) return EntitlementVerdict.Reject(EntitlementReject.Malformed);
            orderId = TryGetString(root, "orderId", out var o) ? o : "";
            acknowledged = root.TryGetProperty("acknowledged", out var a)
                && (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False) && a.GetBoolean();
        }
        catch (JsonException) { return EntitlementVerdict.Reject(EntitlementReject.Malformed); }

        // 3. Field checks, each with a distinct reason.
        if (!string.Equals(packageName, _expectedPackageName, StringComparison.Ordinal))
            return EntitlementVerdict.Reject(EntitlementReject.WrongPackage);
        if (!_productIds.Contains(productId!))
            return EntitlementVerdict.Reject(EntitlementReject.WrongProduct);
        if (purchaseState != _purchasedState)
            return EntitlementVerdict.Reject(EntitlementReject.NotPurchased);

        return new EntitlementVerdict(true, EntitlementReject.None, productId, orderId, acknowledged);
    }

    private static bool TryGetString(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString();
        return value is not null;
    }

    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value);
    }
}
