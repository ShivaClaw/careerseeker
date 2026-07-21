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
    /// <summary>Resolves a host to its candidate addresses. Injectable so the guard is testable offline.</summary>
    public delegate Task<IPAddress[]> DnsResolver(string host, CancellationToken ct);

    /// <summary>
    /// True when <paramref name="ip"/> is a globally routable public address — i.e. not loopback,
    /// private, link-local (incl. the cloud metadata range 169.254/16), CGNAT, benchmark, multicast, or
    /// an unspecified/reserved range. IPv4-mapped IPv6 is unwrapped first so a mapped private v4 cannot
    /// slip through as v6.
    /// </summary>
    public static bool IsPubliclyRoutable(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] is not 0 and not 10 and not 127 and not >= 224 &&
                   !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
                   !(b[0] == 169 && b[1] == 254) &&
                   !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                   !(b[0] == 192 && b[1] == 168) &&
                   !(b[0] == 198 && b[1] >= 18 && b[1] <= 19);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !ip.IsIPv6LinkLocal &&
                   !ip.IsIPv6SiteLocal &&
                   !ip.IsIPv6Multicast &&
                   !IPAddress.IsLoopback(ip) &&
                   !ip.Equals(IPAddress.IPv6Any) && // :: — unspecified; dialing it typically hits loopback
                   (b[0] & 0xfe) != 0xfc;
        }

        return false;
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
