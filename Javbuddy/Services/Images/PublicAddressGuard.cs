using System.Net;
using System.Net.Sockets;

namespace Javbuddy.Services.Images;

/// <summary>Raised by <see cref="PublicAddressGuard"/> when a user-supplied URL resolves only to
/// loopback, private, link-local or otherwise non-public addresses.</summary>
public sealed class BlockedAddressException(string host) : Exception($"{host} resolves to a local or private network address.");

/// <summary>Keeps the "import image from URL" flows (actor portrait, movie cover, movie
/// extrafanart) from being pointed at the server itself or its private network. The check runs
/// in the connection callback, on the addresses actually connected to, so it also covers
/// redirects and a DNS name that resolves to an internal address.</summary>
public static class PublicAddressGuard
{
    public static SocketsHttpHandler CreateHandler() => new() { ConnectCallback = ConnectAsync };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                    // 0.0.0.0/8 "this network"
                || b[0] == 10                                     // 10.0.0.0/8
                || b[0] == 127                                    // loopback
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)     // 100.64.0.0/10 carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                   // link-local, incl. cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)      // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                   // 192.168.0.0/16
                || b[0] >= 224);                                  // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.IPv6None)             // ::
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC);                        // fc00::/7 unique local
        }

        return false;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        var publicAddresses = addresses.Where(IsPublic).ToArray();
        if (publicAddresses.Length == 0) throw new BlockedAddressException(host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(publicAddresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
