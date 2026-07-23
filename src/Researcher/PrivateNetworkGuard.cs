using System.Net;
using System.Net.Sockets;

namespace SeekerSvc.Researcher;

/// <summary>
/// SSRF defense for outbound web-research fetches. Search results and their redirect targets are
/// untrusted, so a private-range destination must be refused at two layers:
/// <list type="number">
/// <item>a cheap string pre-filter on the candidate URL's host (<see cref="IsBlockedHost"/>), and</item>
/// <item>a connect-time check on the <b>resolved</b> IP (<see cref="ResolveGuardedAsync"/>), wired into
///   every socket the fetch handler opens via <see cref="CreateGuardedHttpClient"/>.</item>
/// </list>
/// The string filter alone is insufficient: <c>HttpClient</c> follows redirects automatically (a public
/// page can 302 to <c>http://169.254.169.254/…</c>), and a hostname that is not a literal IP can resolve
/// to a private address (DNS rebinding). Only a check at connect time — on the address actually being
/// dialed — closes both. The check fails closed: an unresolvable host, or any resolved address in a
/// non-routable range, refuses the whole connection.
/// </summary>
public static class PrivateNetworkGuard
{
    private static readonly byte[] IetfProtocolAssignments = { 0x20, 0x01, 0x00 };
    private static readonly byte[] DocumentationV6 = { 0x20, 0x01, 0x0d, 0xb8 };
    private static readonly byte[] DocumentationV6New = { 0x3f, 0xff, 0x00 };

    /// <summary>Resolves a host to its candidate addresses. Injectable so the guard is testable offline.</summary>
    public delegate Task<IPAddress[]> DnsResolver(string host, CancellationToken ct);

    /// <summary>
    /// True when <paramref name="ip"/> is a globally routable public address — i.e. not loopback,
    /// private, link-local (incl. the cloud metadata range 169.254/16), CGNAT, benchmark, multicast, or
    /// an unspecified/reserved range. Any IPv6 form that carries an embedded IPv4 destination — mapped
    /// (<c>::ffff:0:0/96</c>), compatible (<c>::/96</c>), NAT64 (<c>64:ff9b::/96</c>), or 6to4
    /// (<c>2002::/16</c>) — is unwrapped and re-classified as that IPv4, so a private v4 cannot slip
    /// through as v6 in any of these disguises.
    /// </summary>
    public static bool IsPubliclyRoutable(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6 &&
                 TryExtractEmbeddedIPv4(ip.GetAddressBytes()) is { } embedded)
            ip = embedded; // ::/96, 64:ff9b::/96, or 2002::/16 — judge by the IPv4 it reaches

        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] is not 0 and not 10 and not 127 and not >= 224 &&
                   !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
                   !(b[0] == 169 && b[1] == 254) &&
                   !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                   !(b[0] == 192 && b[1] == 0 && b[2] == 0) &&
                   !(b[0] == 192 && b[1] == 0 && b[2] == 2) &&
                   !(b[0] == 192 && b[1] == 88 && b[2] == 99) &&
                   !(b[0] == 192 && b[1] == 168) &&
                   !(b[0] == 198 && b[1] >= 18 && b[1] <= 19) &&
                   !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
                   !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Globally assigned unicast is 2000::/3. Translation forms handled above are judged by
            // their embedded IPv4; the exclusions inside /3 are IANA non-global special-purpose blocks.
            return (b[0] & 0xe0) == 0x20 &&
                   !InPrefix(b, IetfProtocolAssignments, 23) &&
                   !InPrefix(b, DocumentationV6, 32) &&
                   !InPrefix(b, DocumentationV6New, 20) &&
                   !ip.IsIPv6LinkLocal &&
                   !ip.IsIPv6SiteLocal &&
                   !ip.IsIPv6Multicast &&
                   !IPAddress.IsLoopback(ip) &&
                   !ip.Equals(IPAddress.IPv6Any) && // :: — unspecified; dialing it typically hits loopback
                   (b[0] & 0xfe) != 0xfc;
        }

        return false;
    }

    private static bool InPrefix(byte[] address, byte[] prefix, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        for (var i = 0; i < wholeBytes; i++)
            if (address[i] != prefix[i]) return false;

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0) return true;
        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
    }

    /// <summary>
    /// If <paramref name="b"/> (16 IPv6 bytes) is a form that carries an embedded IPv4 destination,
    /// returns that IPv4 so the caller can classify by where the packet actually lands; otherwise null.
    /// Covers IPv4-compatible <c>::/96</c> (the deprecated <c>::a.b.c.d</c>, still parseable and the
    /// classic filter bypass — e.g. <c>::7f00:1</c> is 127.0.0.1), NAT64 <c>64:ff9b::/96</c>, and 6to4
    /// <c>2002::/16</c>. IPv4-mapped <c>::ffff:0:0/96</c> is handled by the framework's
    /// <see cref="IPAddress.IsIPv4MappedToIPv6"/> before this is reached. A public global-unicast v6
    /// address never matches these fixed prefixes, so legitimate v6 is untouched.
    /// </summary>
    private static IPAddress? TryExtractEmbeddedIPv4(byte[] b)
    {
        static bool AllZero(byte[] a, int start, int count)
        {
            for (var i = start; i < start + count; i++)
                if (a[i] != 0) return false;
            return true;
        }

        // 2002::/16 — 6to4: the embedded IPv4 is bytes 2..5.
        if (b[0] == 0x20 && b[1] == 0x02)
            return new IPAddress(new[] { b[2], b[3], b[4], b[5] });

        // 64:ff9b::/96 — NAT64 well-known prefix: embedded IPv4 is the low 32 bits.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && AllZero(b, 4, 8))
            return new IPAddress(new[] { b[12], b[13], b[14], b[15] });

        // ::/96 — IPv4-compatible (high 96 bits zero). Excludes :: and ::1, which the v6 checks already
        // reject as unspecified/loopback; reclassifying them as 0.0.0.0/0.0.0.1 would reject them anyway.
        if (AllZero(b, 0, 12) && !(b[12] == 0 && b[13] == 0 && b[14] == 0 && b[15] <= 1))
            return new IPAddress(new[] { b[12], b[13], b[14], b[15] });

        return null;
    }

    /// <summary>
    /// True when the URL host should be rejected up front: the literal name <c>localhost</c>, or a
    /// literal IP that is loopback or otherwise non-routable. Hosts that are not literal IPs pass here
    /// (they cannot be classified without resolving) and are caught later at connect time.
    /// </summary>
    public static bool IsBlockedHost(string host)
    {
        host = host.TrimEnd('.');
        if (host.Length == 0) return true;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var ipHost = host.Trim('[', ']');
        return IPAddress.TryParse(ipHost, out var ip) &&
               (IPAddress.IsLoopback(ip) || !IsPubliclyRoutable(ip));
    }

    /// <summary>
    /// Resolve <paramref name="host"/> and return an address safe to connect to, or throw
    /// <see cref="SsrfBlockedException"/>. A literal-IP host (e.g. a redirect straight to
    /// <c>169.254.169.254</c>) is validated without a lookup. For a name, every resolved address must be
    /// publicly routable — if any is private the whole host is refused, which defeats DNS rebinding.
    /// The returned address is one that was validated, so the caller connects to exactly what was checked.
    /// </summary>
    internal static async Task<IPAddress> ResolveGuardedAsync(
        string host, DnsResolver resolve, CancellationToken ct)
    {
        var literalHost = host.Trim('[', ']');
        if (IPAddress.TryParse(literalHost, out var literal))
        {
            if (!IsPubliclyRoutable(literal))
                throw new SsrfBlockedException(host, literal);
            return literal;
        }

        var addresses = await resolve(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new SsrfBlockedException(host, null, "host did not resolve to any address");

        foreach (var addr in addresses)
            if (!IsPubliclyRoutable(addr))
                throw new SsrfBlockedException(host, addr);

        return addresses[0];
    }

    /// <summary>
    /// An <see cref="HttpClient"/> for fetching untrusted pages. Redirects are followed (real sites use
    /// them) but bounded, and every connection — the initial request and each redirect hop alike — is
    /// re-validated against <see cref="ResolveGuardedAsync"/> before the socket is dialed.
    /// </summary>
    public static HttpClient CreateGuardedHttpClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = GuardedConnectAsync,
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    private static async ValueTask<Stream> GuardedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var target = await ResolveGuardedAsync(host, DefaultResolveAsync, ct).ConfigureAwait(false);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, port), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static Task<IPAddress[]> DefaultResolveAsync(string host, CancellationToken ct)
        => Dns.GetHostAddressesAsync(host, ct);
}

/// <summary>
/// Thrown when the SSRF guard refuses a destination. The fetch loop treats any fetch failure as a
/// skipped URL, so this fails closed without taking down a research run.
/// </summary>
public sealed class SsrfBlockedException : Exception
{
    public SsrfBlockedException(string host, IPAddress? address, string? reason = null)
        : base($"Blocked non-public research target '{host}'" +
               (address is null ? "" : $" -> {address}") +
               (reason is null ? "." : $": {reason}."))
    {
    }
}
