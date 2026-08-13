using SeekerSvc.Sync;

namespace SeekerSvc.Engine;

/// <summary>
/// The production <see cref="IEntitlementAckPublisher"/>: the engine's answer to a receipt it verified
/// (§4.3.3), sealed and pushed on the same e2p stream as every other payload.
///
/// This class is deliberately three lines of behaviour. All it does is give
/// <see cref="InboundDispatcher"/>'s ack seam a real caller — which is the whole gap it exists to
/// close. Before it, the dispatcher verified the Play-signed receipt, flipped the engine's own Pro
/// flag, and published nothing, while §4.3.3 makes the ack the only thing that may unlock Pro on the
/// phone. The user paid and saw nothing.
///
/// The push's success is deliberately discarded. A failed ack is not a failed grant — the entitlement
/// is already applied and persisted engine-side — and it self-heals: the phone re-reports its owned
/// purchase, the engine re-verifies and acks again. Treating a transport failure as a verification
/// failure would be strictly worse, because it would leave the two sides disagreeing about a purchase
/// that Play says happened.
/// </summary>
public sealed class SyncAckPublisher(SyncPublisher publisher) : IEntitlementAckPublisher
{
    public async Task PublishEntitlementAckAsync(string productId, string? orderId, CancellationToken ct = default)
        => await publisher.PublishEntitlementAckAsync(productId, orderId, ct).ConfigureAwait(false);
}
