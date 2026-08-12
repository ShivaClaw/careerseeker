using System.Text.Json;

namespace SeekerSvc.Sync;

/// <summary>What the inbound dispatcher did with one phone→engine envelope.</summary>
public enum InboundOutcome
{
    /// <summary>The envelope failed the receiver (version/key/replay/decrypt/signature/kind). See ReceiveError.</summary>
    ReceiveRejected,
    /// <summary>A verified entitlement was applied and Pro enabled.</summary>
    EntitlementApplied,
    /// <summary>The entitlement decrypted and its device sig verified, but the payload was not a valid Pro grant. See EntitlementReason.</summary>
    EntitlementRejected,
    /// <summary>An outcome was handed to the outcome applier (§2.5).</summary>
    OutcomeApplied,
    /// <summary>A pull_request asked the engine to re-publish; a fresh snapshot was requested from the republisher.</summary>
    SnapshotRepublished,
    /// <summary>doc_edit is a recognised kind with no engine handler yet — P3 owns it. Never stubbed here.</summary>
    DocEditUnimplemented,
    /// <summary>A shipping kind that has no inbound meaning (e.g. an e2p-only kind seen on p2e); no action.</summary>
    Ignored,
}

/// <summary>The result of dispatching one inbound envelope.</summary>
public sealed record InboundResult(
    InboundOutcome Outcome, SyncError? ReceiveError, string? Kind,
    EntitlementReject EntitlementReason = EntitlementReject.None);

/// <summary>
/// Applies a phone-originated `outcome` (Pro outcome tracking). Filled by the engine's store-backed
/// applier in §2.5; a null applier means outcome dispatch is a no-op seam for now.
/// </summary>
public interface IOutcomeApplier
{
    Task ApplyAsync(string outcomeBodyJson, string deviceFingerprint, CancellationToken ct = default);
}

/// <summary>
/// Re-publishes a fresh snapshot in response to a `pull_request` (§6.2: a large gap → request a fresh
/// snapshot). Backed by the engine's SyncPublisher/bridge; null means the seam is inert (no vault yet).
/// </summary>
public interface ISnapshotRepublisher
{
    Task RepublishSnapshotAsync(long sinceSeq, CancellationToken ct = default);
}

/// <summary>
/// Publishes the engine's `entitlement_ack` (§4.3.3) once a receipt verifies — backed by the pairing's
/// <see cref="SyncPublisher.PublishEntitlementAckAsync"/>; null leaves the seam inert like the other two.
///
/// This seam is what closes the purchase path. Without it the engine verifies the receipt, flips its own
/// Pro flag, and tells the phone nothing — and §4.3.3 makes the ack the only thing that may unlock Pro
/// there, so the user pays and never sees the feature.
/// </summary>
public interface IEntitlementAckPublisher
{
    Task PublishEntitlementAckAsync(string productId, string? orderId, CancellationToken ct = default);
}

/// <summary>
/// The minimal inbound phone→engine path (P4 §2.4): take one envelope, run it through the shipping
/// <see cref="EnvelopeReceiver"/> (which verifies the device signature on state-changing kinds), then
/// dispatch by kind. Structural only — it is constructed behind the `--sync` seam and stays inert until
/// the pairing vault exists; the host's pull loop (RelayClient.PullAsync + p2e high-water persistence)
/// wires it in the device session.
///
/// Dispatch is deliberately narrow: `entitlement` → the EntitlementService verifier; `pull_request` →
/// re-publish a snapshot; `outcome` → the §2.5 applier seam; `doc_edit` → recognised-but-unimplemented
/// (P3's editing surface — this class NEVER touches the doc-edit apply path, not even a stub). No kind
/// here can transmit email; there is no send path.
/// </summary>
public sealed class InboundDispatcher
{
    private readonly EnvelopeReceiver _receiver;
    private readonly EntitlementService _entitlement;
    private readonly string _deviceFingerprint;
    private readonly IOutcomeApplier? _outcomeApplier;
    private readonly ISnapshotRepublisher? _republisher;
    private readonly IEntitlementAckPublisher? _ackPublisher;
    private readonly Func<string, byte[]> _keyForDir;

    public InboundDispatcher(
        EnvelopeReceiver receiver, EntitlementService entitlement, string deviceFingerprint,
        Func<string, byte[]> keyForDir, IOutcomeApplier? outcomeApplier = null, ISnapshotRepublisher? republisher = null,
        IEntitlementAckPublisher? ackPublisher = null)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _entitlement = entitlement ?? throw new ArgumentNullException(nameof(entitlement));
        _deviceFingerprint = deviceFingerprint ?? throw new ArgumentNullException(nameof(deviceFingerprint));
        _keyForDir = keyForDir ?? throw new ArgumentNullException(nameof(keyForDir));
        _outcomeApplier = outcomeApplier;
        _republisher = republisher;
        _ackPublisher = ackPublisher;
    }

    public async Task<InboundResult> DispatchAsync(ReceivedEnvelope env, CancellationToken ct = default)
    {
        var received = _receiver.Receive(env, _keyForDir);
        if (!received.Accepted)
            return new InboundResult(InboundOutcome.ReceiveRejected, received.Error, null);

        switch (received.Kind)
        {
            case "entitlement":
            {
                if (!TryReadEntitlement(received.Plaintext!, out var originalJson, out var signature))
                    return new InboundResult(InboundOutcome.EntitlementRejected, null, received.Kind, EntitlementReject.Malformed);
                var verdict = _entitlement.Apply(originalJson, signature, _deviceFingerprint);
                if (!verdict.Accepted)
                    return new InboundResult(InboundOutcome.EntitlementRejected, null, received.Kind, verdict.Reason);

                // §4.3.3: the ack is emitted on an ACCEPTED verdict and on no other path. There is no
                // negative form of this kind, so a rejection must fall out above rather than reach here
                // with a flag — an ack means granted, full stop. The product and order come from the
                // verdict (i.e. from the Play-signed receipt the verifier just checked), never from the
                // phone's request body: the phone is a courier, and letting it name the product it is
                // acknowledged for would hand the unlock decision back to the device.
                if (_ackPublisher is not null)
                    await _ackPublisher.PublishEntitlementAckAsync(verdict.ProductId!, verdict.OrderId, ct).ConfigureAwait(false);

                return new InboundResult(InboundOutcome.EntitlementApplied, null, received.Kind);
            }

            case "outcome":
            {
                if (_outcomeApplier is not null)
                    await _outcomeApplier.ApplyAsync(BodyJson(received.Plaintext!), _deviceFingerprint, ct).ConfigureAwait(false);
                return new InboundResult(InboundOutcome.OutcomeApplied, null, received.Kind);
            }

            case "pull_request":
            {
                var since = ReadSinceSeq(received.Plaintext!);
                if (_republisher is not null)
                    await _republisher.RepublishSnapshotAsync(since, ct).ConfigureAwait(false);
                return new InboundResult(InboundOutcome.SnapshotRepublished, null, received.Kind);
            }

            // doc_edit is recognised (a shipping, state-changing kind whose signature the receiver just
            // verified) but has no engine handler in P4 — it is P3's editing surface. We return the
            // recognised-but-unimplemented disposition (reply code `unimplemented`, §7.2) and touch
            // nothing else. Stubbing any part of the doc-edit apply path here is explicitly forbidden.
            case "doc_edit":
                return new InboundResult(InboundOutcome.DocEditUnimplemented, null, received.Kind);

            default:
                return new InboundResult(InboundOutcome.Ignored, null, received.Kind);
        }
    }

    /// <summary>The wire error code an inbound doc_edit yields once the reply path exists (§7.2).</summary>
    public static string DocEditReplyCode => SyncError.Unimplemented.ToWire();

    private static bool TryReadEntitlement(byte[] plaintext, out string originalJson, out string signature)
    {
        originalJson = "";
        signature = "";
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            if (!doc.RootElement.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
                return false;
            if (!body.TryGetProperty("original_json", out var oj) || oj.ValueKind != JsonValueKind.String) return false;
            if (!body.TryGetProperty("signature", out var sig) || sig.ValueKind != JsonValueKind.String) return false;
            originalJson = oj.GetString()!;
            signature = sig.GetString()!;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static long ReadSinceSeq(byte[] plaintext)
    {
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            if (doc.RootElement.TryGetProperty("body", out var body)
                && body.TryGetProperty("since_seq", out var s) && s.ValueKind == JsonValueKind.Number
                && s.TryGetInt64(out var v))
                return v;
        }
        catch (JsonException) { /* fall through to 0 */ }
        return 0;
    }

    private static string BodyJson(byte[] plaintext)
    {
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            if (doc.RootElement.TryGetProperty("body", out var body))
                return body.GetRawText();
        }
        catch (JsonException) { /* fall through */ }
        return "{}";
    }
}
