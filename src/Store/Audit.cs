using System.Security.Cryptography;
using System.Text;

namespace SeekerSvc.Store;

/// <summary>The fields needed to append one audit event; seq/ts/prev_hash/hash are assigned at write time.</summary>
public sealed record EventInput(string Actor, string Kind, string Entity, string EntityId, string PayloadJson = "");

/// <summary>One stored audit row, including its chain links.</summary>
public sealed record EventRow(
    long Seq,
    string Ts,
    string Actor,
    string Kind,
    string Entity,
    string EntityId,
    string PayloadJson,
    string PrevHash,
    string Hash);

/// <summary>Result of verifying the audit chain.</summary>
public sealed record AuditVerification(bool Ok, long? FirstBrokenSeq, string? Reason)
{
    public static readonly AuditVerification Valid = new(true, null, null);
}

/// <summary>
/// The tamper-evident audit log (spec sections 7.1 and 8.4). Each event's hash is computed over its
/// own fields AND the previous event's hash, forming a chain. Editing a payload changes that row's
/// hash; deleting or reordering a row breaks the prev-hash linkage. Either way
/// <see cref="VerifyChain"/> reports the first broken sequence, so "show me everything you've ever
/// done in my name" is backed by a record that cannot be silently altered.
///
/// Pure and dependency-free: the same function runs over rows from SQLite, the in-memory store, or a
/// relay replica, and produces identical hashes.
/// </summary>
public static class Audit
{
    /// <summary>prev_hash of the very first event.</summary>
    public const string Genesis = "GENESIS";

    /// <summary>
    /// Canonical hash of an event. Fields are length-prefixed before hashing so no field value can
    /// be shifted into another (e.g. a payload cannot impersonate a different entity_id).
    /// </summary>
    public static string ComputeHash(
        long seq, string ts, string actor, string kind,
        string entity, string entityId, string payloadJson, string prevHash)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(seq);
            WriteField(w, ts);
            WriteField(w, actor);
            WriteField(w, kind);
            WriteField(w, entity);
            WriteField(w, entityId);
            WriteField(w, payloadJson);
            WriteField(w, prevHash);
        }
        ms.Position = 0;
        var digest = SHA256.HashData(ms);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Build the next event row given the previous hash, the new sequence, timestamp, and input.</summary>
    public static EventRow Link(long seq, string ts, string prevHash, EventInput e)
    {
        var hash = ComputeHash(seq, ts, e.Actor, e.Kind, e.Entity, e.EntityId, e.PayloadJson, prevHash);
        return new EventRow(seq, ts, e.Actor, e.Kind, e.Entity, e.EntityId, e.PayloadJson, prevHash, hash);
    }

    /// <summary>
    /// Verify an ordered chain. Checks, for each row in sequence: the recomputed hash matches the
    /// stored hash (no field was edited), and prev_hash links to the prior row's hash (none was
    /// deleted or reordered). The first event must link to <see cref="Genesis"/>.
    /// </summary>
    public static AuditVerification VerifyChain(IReadOnlyList<EventRow> rowsInSeqOrder)
    {
        var expectedPrev = Genesis;
        long? prevSeq = null;

        foreach (var r in rowsInSeqOrder)
        {
            if (prevSeq is { } ps && r.Seq <= ps)
                return new AuditVerification(false, r.Seq, $"sequence not strictly increasing at {r.Seq}");

            if (r.PrevHash != expectedPrev)
                return new AuditVerification(false, r.Seq,
                    prevSeq is null ? "first event does not link to genesis" : "broken link to previous event");

            var recomputed = ComputeHash(r.Seq, r.Ts, r.Actor, r.Kind, r.Entity, r.EntityId, r.PayloadJson, r.PrevHash);
            if (recomputed != r.Hash)
                return new AuditVerification(false, r.Seq, "event content does not match its hash");

            expectedPrev = r.Hash;
            prevSeq = r.Seq;
        }

        return AuditVerification.Valid;
    }

    private static void WriteField(BinaryWriter w, string? s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        w.Write(bytes.Length);
        w.Write(bytes);
    }
}
