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

/// <summary>One application row as the phone's list/detail renders it. No raw posting body.</summary>
public sealed record AppSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("score")] int Score);

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
