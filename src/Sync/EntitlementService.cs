namespace SeekerSvc.Sync;

/// <summary>
/// The persisted Pro entitlement flag. <see cref="Enabled"/> is set once a valid entitlement is
/// applied; <see cref="LastVerifiedAt"/> is refreshed on every re-report from the phone. Effective
/// entitlement is <c>Enabled</c> AND within the grace window of <c>LastVerifiedAt</c> — that is how a
/// refund propagates: Play stops returning the purchase as owned, the phone stops re-reporting, and
/// the grace window lapses. Persisted by the engine (config table) so it survives restart.
/// </summary>
public sealed record EntitlementState(bool Enabled, DateTimeOffset LastVerifiedAt);

/// <summary>
/// The state/audit seam <see cref="EntitlementService"/> depends on, so <c>src/Sync</c> stays free of
/// a store dependency. The engine backs it with the config table and the hash-chained audit log
/// (wired in P4 §2.4); tests use an in-memory fake.
/// </summary>
public interface IEntitlementStateStore
{
    /// <summary>The persisted flag, or null if Pro was never applied.</summary>
    EntitlementState? Load();

    /// <summary>Persist the flag (idempotent overwrite).</summary>
    void Save(EntitlementState state);

    /// <summary>
    /// Append the audit event for a successfully applied entitlement: the product and order it
    /// verified, and the fingerprint of the paired device that delivered it — so the trail can prove
    /// which device asked, not merely that Pro turned on.
    /// </summary>
    void AuditApplied(string productId, string orderId, string deviceFingerprint, bool acknowledged);
}

/// <summary>
/// Applies verified Pro entitlements and answers "is Pro active right now?". Verification is delegated
/// to an <see cref="IEntitlementVerifier"/> strategy (option C ships <see cref="GoogleSignedPayloadVerifier"/>);
/// this class owns the flag lifecycle and the offline revocation grace window.
///
/// The grace logic is pure and driven by an injected clock, so it is fully testable without a phone or
/// an account: apply refreshes the timestamp, and <see cref="IsEntitled"/> lapses to false once the
/// last verification is older than <see cref="GraceWindow"/>. Acceptable for a one-time $2.99 unlock;
/// a subscription (Cloud) must NOT reuse this path (Entitlement-Architecture §"weakness 2").
/// </summary>
public sealed class EntitlementService
{
    /// <summary>Offline revocation latency: Pro stays active for this long after the last successful
    /// re-report, matching the spec's 30-day desktop grace window.</summary>
    public static readonly TimeSpan GraceWindow = TimeSpan.FromDays(30);

    private readonly IEntitlementVerifier _verifier;
    private readonly IEntitlementStateStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public EntitlementService(IEntitlementVerifier verifier, IEntitlementStateStore store, Func<DateTimeOffset> clock)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Verify an inbound entitlement report from the device identified by <paramref name="deviceFingerprint"/>
    /// and, on success, enable Pro and refresh the grace clock. A rejected payload changes no state and
    /// writes no audit event — its distinct reason is returned for the caller to reply/record. Returning
    /// the verdict (rather than a bare bool) keeps every rejection reason observable and tested.
    /// </summary>
    public EntitlementVerdict Apply(string originalJson, string signatureStandardB64, string deviceFingerprint)
    {
        var verdict = _verifier.Verify(originalJson, signatureStandardB64);
        if (!verdict.Accepted) return verdict;

        _store.Save(new EntitlementState(Enabled: true, LastVerifiedAt: _clock()));
        _store.AuditApplied(verdict.ProductId!, verdict.OrderId ?? "", deviceFingerprint, verdict.Acknowledged);
        return verdict;
    }

    /// <summary>
    /// Is Pro active right now? True only if the flag is enabled AND the last verification is within the
    /// grace window. Pure read — a lapsed grace window is reported as not-entitled without mutating the
    /// stored flag, so a later re-report re-activates it cleanly.
    /// </summary>
    public bool IsEntitled()
    {
        var state = _store.Load();
        return state is { Enabled: true } && (_clock() - state.LastVerifiedAt) <= GraceWindow;
    }
}
