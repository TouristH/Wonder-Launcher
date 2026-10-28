// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Net;
using System.Net.Sockets;
using Wonder.Application;

namespace Wonder.Infrastructure;

/// <summary>Connects to the checked IP itself, so DNS cannot change between validation and use.</summary>
internal sealed class TextureConnectionPolicy(
    Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
    Func<IPAddress, int, CancellationToken, ValueTask<Stream>>? connect = null)
{
    // Conservative public-unicast policy. Special-purpose and transition ranges require consent.
    private static readonly IPNetwork[] RestrictedV4 =
    [
        IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"), IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"), IPNetwork.Parse("192.88.99.0/24"), IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"), IPNetwork.Parse("198.51.100.0/24"), IPNetwork.Parse("203.0.113.0/24")
    ];
    private static readonly IPNetwork PublicV6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork[] RestrictedV6 =
    [
        IPNetwork.Parse("2001::/23"), IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"), IPNetwork.Parse("3fff::/20")
    ];

    public SocketsHttpHandler CreateHandler(bool allowPrivateNetwork) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = (context, ct) => ConnectAsync(context.DnsEndPoint, allowPrivateNetwork, ct)
    };

    public static void CheckLiteralTarget(Uri target, bool allowPrivateNetwork)
    {
        var host = target.IdnHost.Trim('[', ']');
        if (IPAddress.TryParse(host, out var address))
        {
            if (!IsAllowedAddress(address, allowPrivateNetwork)) throw Blocked();
        }
        else if (!allowPrivateNetwork && IsLocalName(host)) throw Blocked();
    }

    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, bool allowPrivateNetwork, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var host = endpoint.Host.Trim('[', ']');
        if (!allowPrivateNetwork && IsLocalName(host) && !IPAddress.TryParse(host, out _)) throw Blocked();
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await (resolve ?? ResolveAsync)(host, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (addresses.Length == 0) throw new HttpRequestException("纹理来源未解析到可用地址。");
        // Reject a mixed answer as a whole, before trying any address or falling back.
        if (addresses.Any(address => !IsAllowedAddress(address, allowPrivateNetwork))) throw Blocked();
        SocketException? lastError = null;
        foreach (var address in addresses.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try { return await (connect ?? ConnectSocketAsync)(address, endpoint.Port, ct).ConfigureAwait(false); }
            catch (SocketException error) { lastError = error; }
        }
        throw new HttpRequestException("无法连接纹理来源。", lastError);
    }

    private static Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);

    private static async ValueTask<Stream> ConnectSocketAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }

    private static bool IsLocalName(string host)
    {
        host = host.TrimEnd('.');
        return !host.Contains('.') || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedAddress(IPAddress address, bool allowPrivateNetwork)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = address.GetAddressBytes()[0];
            if (first == 0 || first >= 224) return false;
            return allowPrivateNetwork || !RestrictedV4.Any(range => range.Contains(address));
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast) return false;
        return allowPrivateNetwork || (address.ScopeId == 0 && PublicV6.Contains(address) && !RestrictedV6.Any(range => range.Contains(address)));
    }

    internal static AccountAuthenticationException Blocked() => new("纹理来源指向受限网络，已阻止连接。可在此账户的纹理来源设置中明确允许可信来源。");
}
