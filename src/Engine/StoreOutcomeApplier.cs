using System.Globalization;
using System.Text.Json;
using SeekerSvc.Store;
using SeekerSvc.Sync;

namespace SeekerSvc.Engine;

/// <summary>
/// Fills the inbound <see cref="IOutcomeApplier"/> seam (P4 §2.4 dispatch) with the store: a phone-sent
/// `outcome` envelope body <c>{app_id, outcome, at}</c> is persisted via <see cref="ISeekerStore.SetOutcomeAsync"/>,
/// which appends an audit event. The envelope's device signature was already verified by the receiver
/// before dispatch; this applier is the trusted-side effect.
///
/// Robust by construction: it parses fully before writing and NEVER partially applies. A malformed body,
/// an unparseable app_id, or an outcome outside <see cref="ApplicationOutcome.PhoneSettable"/> is a no-op —
/// the phone UI only offers the settable subset, so `no_reply` (a desktop-set observation) or an unknown
/// value means a buggy or hostile client, and it changes nothing rather than throwing on the pull loop.
///
/// Each of those no-ops returns a NAMED refusal (PQ-S6-1). They used to be bare `return`s, which the
/// dispatcher could not tell apart from a successful persist — six paths that dropped a user's mark and
/// reported it applied. The behaviour is unchanged; what changed is that the caller can now see it.
/// </summary>
public sealed class StoreOutcomeApplier(ISeekerStore store) : IOutcomeApplier
{
    public async Task<OutcomeVerdict> ApplyAsync(string outcomeBodyJson, string deviceFingerprint, CancellationToken ct = default)
    {
        long appId;
        string outcome, at;
        try
        {
            using var doc = JsonDocument.Parse(outcomeBodyJson);
            var body = doc.RootElement;
            if (body.ValueKind != JsonValueKind.Object) return OutcomeVerdict.Reject(OutcomeReject.Malformed);
            if (!TryString(body, "app_id", out var appIdStr) || !long.TryParse(appIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId)) return OutcomeVerdict.Reject(OutcomeReject.Malformed);
            if (!TryString(body, "outcome", out outcome!)) return OutcomeVerdict.Reject(OutcomeReject.Malformed);
            if (!TryString(body, "at", out at!)) return OutcomeVerdict.Reject(OutcomeReject.Malformed);
        }
        catch (JsonException) { return OutcomeVerdict.Reject(OutcomeReject.Malformed); }

        // The phone may only set the wire-settable subset; no_reply and unknown values are rejected here.
        if (!ApplicationOutcome.PhoneSettable.Contains(outcome)) return OutcomeVerdict.Reject(OutcomeReject.NotPhoneSettable);

        // actor='user': the human marked this outcome on their paired phone. (The audit actor column is
        // constrained to engine|user|relay; the delivering device was already sig-verified at receipt.)
        await store.SetOutcomeAsync(appId, outcome, at, actor: "user", ct).ConfigureAwait(false);
        return OutcomeVerdict.Ok;
    }

    private static bool TryString(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString();
        return !string.IsNullOrEmpty(value);
    }
}
