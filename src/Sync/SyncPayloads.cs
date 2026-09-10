using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeekerSvc.Sync;

/// <summary>
/// The engine→phone payload builders for P2's read-only dashboard (Sync-Protocol.md §4.3
/// kinds `snapshot`, `delta`, `heartbeat`). Pure: given already-projected dashboard data,
/// they produce the plaintext JSON the caller then seals with `k_e2p`. No engine types, no
/// SQLite, no network — so this stays unit-testable and the host owns wiring.
///
/// Untrusted-text rule (CLAUDE.md): job descriptions and recruiter text are display-only
/// strings. These builders carry only the short, structured fields the dashboard renders
/// (state, company, title, score, counters) — never a raw posting body — so nothing
/// interpolable rides to the phone in P2. Document text is a separate `doc` kind (P3).
/// </summary>
public static class SyncPayloads
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Snapshot(Counters counters, IReadOnlyList<AppSummary> applications, IReadOnlyList<JobSummary> jobs)
        => Encode("snapshot", new { counters, applications, jobs });

    public static byte[] Delta(long sinceSeq, Counters counters, IReadOnlyList<AppSummary> changedApplications, IReadOnlyList<JobSummary> changedJobs)
        => Encode("delta", new { since_seq = sinceSeq, counters, applications = changedApplications, jobs = changedJobs });

    public static byte[] Heartbeat(string tsUtc, long cycle, Counters counters)
        => Encode("heartbeat", new { ts = tsUtc, cycle, counters });

    /// <summary>
    /// Audit-chain metadata for the phone's Evidence screen (Sync-Protocol.md §4.3 kind
    /// `evidence`): the engine's own verification verdict plus recent audit-event metadata —
    /// seq/ts/actor/kind/entity only. It deliberately carries NO event payload bodies: the audit
    /// events reference engine-internal entities, and the raw bodies stay on the desktop.
    /// </summary>
    public static byte[] Evidence(bool auditOk, long? firstBrokenSeq, int eventCount, IReadOnlyList<EvidenceEvent> events)
        => Encode("evidence", new { audit_ok = auditOk, first_broken_seq = firstBrokenSeq, event_count = eventCount, events });

    /// <summary>
    /// The engine's acknowledgement of a verified Pro grant (Sync-Protocol.md §4.3.3, gate PQ-A6-1).
    /// This is the ONLY payload that may unlock Pro on the phone: §4.3.2 makes the phone a courier for
    /// the Play receipt, and the phone never rules on its own entitlement — a device that could
    /// self-certify would be a device with an incentive to.
    ///
    /// <paramref name="acknowledgedAt"/> is the engine's clock and is advisory only (§6.3): a receiver
    /// MUST NOT expire or re-lock an entitlement on the strength of it. <paramref name="orderId"/> is
    /// Play correspondence data, not authorisation — null omits the field entirely rather than writing
    /// a null, which is exactly what the `entitlement-ack-no-order-id` vector pins. An ack without it
    /// is complete and must be honoured identically.
    ///
    /// <b>There is no negative form.</b> A receipt the engine rejects produces an `error` (§7.2)
    /// naming the reason — never an ack carrying a failure flag. Callers MUST NOT build one for a
    /// rejected verdict: a kind whose meaning depends on reading a field inside the body is the parser
    /// hazard §4.2 exists to avoid, and here it would sit on the one path that turns a paid feature on.
    /// </summary>
    public static byte[] EntitlementAck(string productId, string acknowledgedAt, string? orderId = null)
        => Encode("entitlement_ack", new { product_id = productId, acknowledged_at = acknowledgedAt, order_id = orderId });

    private static byte[] Encode(string kind, object body)
        => JsonSerializer.SerializeToUtf8Bytes(new { kind, body }, Options);
}

/// <summary>Dashboard tallies mirrored to the phone. Matches EngineCore's live counters.</summary>
public sealed record Counters(
    [property: JsonPropertyName("discovered")] long Discovered,
    [property: JsonPropertyName("acted")] long Acted,
    [property: JsonPropertyName("drafted")] long Drafted,
    [property: JsonPropertyName("blocked")] long Blocked,
    [property: JsonPropertyName("rejected")] long Rejected,
    [property: JsonPropertyName("errors")] long Errors,
    [property: JsonPropertyName("cycles")] long Cycles);

/// <summary>
/// One application row as the phone's list/detail renders it. No raw posting body. <c>Outcome</c> is the
/// nullable Pro outcome-tracking state (P4 §2.5): absent for non-Pro data or an unset outcome — the
/// null-ignoring serializer omits the field, so absent ⇒ null on the phone, never a malformed value.
/// </summary>
public sealed record AppSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("outcome")] string? Outcome = null);

/// <summary>One discovered job as the phone's Jobs screen renders it. Flags are display-only.</summary>
public sealed record JobSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("repost")] bool Repost,
    [property: JsonPropertyName("injection_flag")] bool InjectionFlag);

/// <summary>
/// One audit-chain event as the phone's Evidence screen renders it — metadata only. `actor`,
/// `kind`, `entity`, and `entity_id` are engine-internal structured identifiers, not untrusted
/// job text; a raw event payload body never rides here.
/// </summary>
public sealed record EvidenceEvent(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("ts")] string Ts,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("entity")] string Entity,
    [property: JsonPropertyName("entity_id")] string EntityId);
