using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace STS2MobileIos.Lan;

internal readonly record struct LanAddress(string Host, ushort Port)
{
    public const ushort DefaultPort = 33771;

    public static LanAddress Parse(string host, string port)
    {
        host = host.Trim();
        if (!ushort.TryParse(port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0)
            throw new ArgumentException("Enter a port between 1 and 65535.");
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork
            || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)
            || address.GetAddressBytes()[0] >= 224 || address.GetAddressBytes()[0] == 0)
            throw new ArgumentException("Enter the host's IPv4 address, such as 192.168.1.20.");
        return new LanAddress(address.ToString(), number);
    }
}
