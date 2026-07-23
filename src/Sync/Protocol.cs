namespace SeekerSvc.Sync;

/// <summary>
/// Sync Protocol v1 constants and vocabulary. Normative source: docs/Sync-Protocol.md —
/// anything here that disagrees with that document is a bug in this file.
/// </summary>
public static class Protocol
{
    public const int Version = 1;

    /// <summary>Envelope hard limit; larger is rejected before any crypto work.</summary>
    public const int MaxEnvelopeBytes = 1024 * 1024;

    /// <summary>AES-256-GCM, decided at gate P0-CIPHER.</summary>
    public const int KeyBytes = 32;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;

    /// <summary>Pairing suite for v1 (gate P1-CURVE). The hybrid PQ suite is a reserved bump.</summary>
    public const string Suite = "p256-hkdf-sha256";
    public const string SuiteHybridReserved = "p256+mlkem768-hkdf-sha256";

    public const string InfoEngineToPhone = "careerseeker/v1/e2p";
    public const string InfoPhoneToEngine = "careerseeker/v1/p2e";
    public const string InfoRelayToken = "careerseeker/v1/relay-token";
    public const string InfoConfirm = "careerseeker/v1/confirm";
    public const string BootstrapSalt = "careerseeker/v1/bootstrap";
    public const string PairAadPrefix = "careerseeker/v1/pair";
    public const string CommandSigPrefix = "careerseeker/v1/cmd";

    /// <summary>Payload kinds shipping in v1 (Sync-Protocol.md §4.3).</summary>
    public static readonly IReadOnlySet<string> ShippingKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "snapshot", "delta", "doc", "evidence", "heartbeat", "conflict", "entitlement_ack",
        "doc_edit", "outcome", "entitlement", "pull_request", "error",
    };

    /// <summary>
    /// Reserved for a future L2 and rejected by a v1 receiver. `kill` in particular stays
    /// reserved: a remote stop command is a control-plane action, and the product stays L1
    /// until that has been externally audited.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedL2Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "state_change", "gate_request", "gate_resolve", "kill", "config_change", "lesson_proposal", "metric",
    };

    /// <summary>
    /// Phone-originated kinds that change engine state and therefore REQUIRE the
    /// envelope-level device signature (Sync-Protocol.md §5.4).
    /// </summary>
    public static readonly IReadOnlySet<string> StateChangingKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "doc_edit", "outcome", "entitlement",
    };
}

/// <summary>Rejection reasons, Sync-Protocol.md §7.2. Wire form is the lowercase name.</summary>
public enum SyncError
{
    VersionUnsupported,
    ReplayRejected,
    DecryptFailed,
    UnknownKind,
    KeyUnknown,
    BadSignature,
    RevConflict,
    PairingUnknown,
    TooLarge,
}

public static class SyncErrorWire
{
    public static string ToWire(this SyncError e) => e switch
    {
        SyncError.VersionUnsupported => "version_unsupported",
        SyncError.ReplayRejected => "replay_rejected",
        SyncError.DecryptFailed => "decrypt_failed",
        SyncError.UnknownKind => "unknown_kind",
        SyncError.KeyUnknown => "key_unknown",
        SyncError.BadSignature => "bad_signature",
        SyncError.RevConflict => "rev_conflict",
        SyncError.PairingUnknown => "pairing_unknown",
        SyncError.TooLarge => "too_large",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
}
