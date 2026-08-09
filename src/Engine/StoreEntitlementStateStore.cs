using System.Globalization;
using System.Text.Json;
using SeekerSvc.Store;
using SeekerSvc.Sync;

namespace SeekerSvc.Engine;

/// <summary>
/// Backs <see cref="EntitlementService"/>'s state seam with the engine's real store: the Pro flag lives
/// in the config table (so it survives restart) and every applied entitlement appends a hash-chained
/// audit event recording the product, order, and the fingerprint of the paired device that delivered it
/// — extending "nobody is ever blind" to the entitlement grant.
///
/// The <see cref="IEntitlementStateStore"/> contract is synchronous so the pure grace logic in
/// EntitlementService stays sync; <see cref="ISeekerStore"/> is async. This adapter runs only inside the
/// console host and the harness, where there is no synchronization context, so blocking at the boundary
/// cannot deadlock, and the inbound entitlement path is low-frequency (one report per phone launch).
/// </summary>
public sealed class StoreEntitlementStateStore(ISeekerStore store) : IEntitlementStateStore
{
    public const string EnabledKey = "entitlement.pro.enabled";
    public const string LastVerifiedKey = "entitlement.pro.last_verified_at";

    public EntitlementState? Load()
    {
        var enabledRaw = store.GetConfigAsync(EnabledKey).GetAwaiter().GetResult();
        if (enabledRaw is null) return null; // never applied

        var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase);
        var lastRaw = store.GetConfigAsync(LastVerifiedKey).GetAwaiter().GetResult();
        var lastVerified = lastRaw is not null
            && DateTimeOffset.TryParse(lastRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue; // a present flag with an unparseable timestamp reads as long-lapsed, never active
        return new EntitlementState(enabled, lastVerified);
    }

    public void Save(EntitlementState state)
    {
        store.SetConfigAsync(EnabledKey, state.Enabled ? "true" : "false").GetAwaiter().GetResult();
        store.SetConfigAsync(LastVerifiedKey, state.LastVerifiedAt.ToString("O", CultureInfo.InvariantCulture))
            .GetAwaiter().GetResult();
    }

    public void AuditApplied(string productId, string orderId, string deviceFingerprint, bool acknowledged)
    {
        var payload = JsonSerializer.Serialize(new
        {
            product_id = productId,
            device_fingerprint = deviceFingerprint,
            acknowledged,
        });
        store.AppendEventAsync(new EventInput(
            Actor: "phone", Kind: "entitlement_applied", Entity: "pro_entitlement",
            EntityId: orderId, PayloadJson: payload)).GetAwaiter().GetResult();
    }
}
