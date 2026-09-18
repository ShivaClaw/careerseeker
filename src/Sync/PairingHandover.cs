using System.Security.Cryptography;
using System.Text;

namespace SeekerSvc.Sync;

/// <summary>
/// The §5.2.3 relay-token handover: after a pairing completes, the channel's credential must
/// rotate from the QR-derived provisional token to the ikm-derived final one, because every
/// later engine call — push, pull, unpair — presents the final token.
///
/// <para>This unit exists because the handover was missing from production entirely. Until
/// 2026-09-18 the only caller of <see cref="RelayClient.RotateTokenAsync"/> in the whole tree
/// was the live smoke (<c>tests/SyncLiveSmoke/Program.cs</c>), which rotated manually — so the
/// one gate that touched a real relay masked the gap, and the production seam
/// (<c>src/Engine/Program.cs</c>, <c>BuildPairingSeam</c>) paired, persisted the final token,
/// and then left the relay honouring only the provisional hash: 401 on every route, from an
/// engine that believed it was paired, while the phone (which §5.2.3 forbids from rotating)
/// looked healthily connected. Sync-Protocol.md §2's interop note now names this seam as the
/// production caller.</para>
///
/// <para>Two properties are load-bearing and pinned by SyncHarness:</para>
/// <list type="bullet">
/// <item><description><b>The hex form.</b> The relay's gate is <c>/^[0-9a-f]{64}$/</c>,
/// case-sensitive, and C#'s <see cref="Convert.ToHexString(byte[])"/> is uppercase — the exact
/// trap §2 documents. <see cref="TokenSha256Hex"/> is the one place that spelling lives.
/// </description></item>
/// <item><description><b>The recovery retry.</b> Rotation is one-way and idempotent relay-side:
/// once rotated, the provisional bearer is dead, but the final bearer re-presenting the same
/// <c>rotate_to</c> answers 200. So a first attempt that half-succeeded (rotated, but the
/// response was lost) is distinguished from a real refusal by retrying as the final bearer,
/// instead of stranding the engine on the one call that locks it out if misread.
/// </description></item>
/// </list>
/// </summary>
public static class PairingHandover
{
    public enum Outcome
    {
        /// <summary>The provisional bearer rotated the channel: the normal path.</summary>
        Rotated,

        /// <summary>The provisional bearer was refused but the final bearer was accepted with the
        /// same target — a previous attempt had already rotated the channel.</summary>
        AlreadyRotated,

        /// <summary>Both bearers refused (or the relay was unreachable). The vault holds the final
        /// token but the relay does not: sync cannot work until the user unpairs and re-pairs.
        /// Callers surface this; they never report it as success.</summary>
        Failed,
    }

    /// <summary>
    /// Lowercase SHA-256 hex of the token's UTF-8 bytes — the only spelling the relay's
    /// case-sensitive <c>rotate_to</c> gate accepts (§2). Lowercasing here, not at call sites,
    /// so the trap cannot be reintroduced by a caller that forgets.
    /// </summary>
    public static string TokenSha256Hex(string relayToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relayToken))).ToLowerInvariant();

    /// <summary>
    /// Perform the handover. <paramref name="rotate"/> is
    /// <see cref="RelayClient.RotateTokenAsync"/>'s shape — (currentBearer, newTokenSha256Hex, ct)
    /// — injected so the harness can drive this without a relay. Transport exceptions count as a
    /// refused attempt; only the caller's own cancellation propagates, per the house rule that a
    /// requested shutdown must not be laundered into "the relay did not answer".
    /// </summary>
    public static async Task<Outcome> RotateAsync(
        Func<string, string, CancellationToken, Task<bool>> rotate,
        string provisionalToken,
        string finalToken,
        CancellationToken ct = default)
    {
        var hex = TokenSha256Hex(finalToken);

        if (await AttemptAsync(rotate, provisionalToken, hex, ct).ConfigureAwait(false))
            return Outcome.Rotated;

        if (await AttemptAsync(rotate, finalToken, hex, ct).ConfigureAwait(false))
            return Outcome.AlreadyRotated;

        return Outcome.Failed;
    }

    private static async Task<bool> AttemptAsync(
        Func<string, string, CancellationToken, Task<bool>> rotate,
        string bearer, string hex, CancellationToken ct)
    {
        try { return await rotate(bearer, hex, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
}
