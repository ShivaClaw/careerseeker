using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

public sealed record AuditExportOptions(bool IncludePayloads = false);

public static class AuditExport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<string> BuildJsonAsync(
        ISeekerStore store,
        AuditExportOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AuditExportOptions();
        var verification = await store.VerifyAuditAsync(ct).ConfigureAwait(false);
        var events = await store.GetEventsAsync(ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            audit = new
            {
                ok = verification.Ok,
                firstBrokenSeq = verification.FirstBrokenSeq,
                reason = verification.Reason,
                eventCount = events.Count,
            },
            payloadsIncluded = options.IncludePayloads,
            events = events
                .OrderBy(e => e.Seq)
                .Select(e => EventForExport(e, options.IncludePayloads))
                .ToArray(),
        }, JsonOptions);
    }

    private static object EventForExport(EventRow e, bool includePayload) =>
        includePayload
            ? new
            {
                e.Seq,
                e.Ts,
                e.Actor,
                e.Kind,
                e.Entity,
                e.EntityId,
                e.PayloadJson,
                PayloadSha256 = Sha256(e.PayloadJson),
                PayloadLength = e.PayloadJson.Length,
                e.PrevHash,
                e.Hash,
            }
            : new
            {
                e.Seq,
                e.Ts,
                e.Actor,
                e.Kind,
                e.Entity,
                e.EntityId,
                PayloadSha256 = Sha256(e.PayloadJson),
                PayloadLength = e.PayloadJson.Length,
                e.PrevHash,
                e.Hash,
            };

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
