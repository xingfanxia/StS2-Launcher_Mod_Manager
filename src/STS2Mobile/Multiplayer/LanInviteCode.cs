using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace STS2Mobile.Multiplayer;

public readonly record struct LanJoinEndpoint(IPAddress Address, ushort Port)
{
    public override string ToString() => $"{Address}:{Port}";
}

public enum LanInviteParseError
{
    None,
    Empty,
    TooLong,
    UnsupportedVersion,
    InvalidFormat,
    InvalidAddress,
    UnsafeAddress,
    InvalidPort,
}

// Versioned, copyable signaling for the launcher's existing ENet direct
// transport. This intentionally contains no relay promise, authentication, or
// hidden payload: it is a stricter representation of IP:port.
public static class LanInviteCode
{
    public const ushort DefaultGamePort = 33771;
    public const int MaxInputLength = 128;
    public const int MaxShareChoices = 8;
    public const string V1Prefix = "sts2lan:v1:";

    public static string Format(LanJoinEndpoint endpoint)
    {
        if (!IsShareableAddress(endpoint.Address) || endpoint.Port == 0)
            throw new ArgumentException("Invite endpoint is not shareable.", nameof(endpoint));
        return V1Prefix + endpoint;
    }

    public static bool TryParseJoinInput(
        string input,
        out LanJoinEndpoint endpoint,
        out LanInviteParseError error
    )
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = LanInviteParseError.Empty;
            return false;
        }

        input = input.Trim();
        if (input.Length > MaxInputLength)
        {
            error = LanInviteParseError.TooLong;
            return false;
        }

        string endpointText;
        if (input.StartsWith("sts2lan:", StringComparison.OrdinalIgnoreCase))
        {
            if (!input.StartsWith(V1Prefix, StringComparison.OrdinalIgnoreCase))
            {
                error = LanInviteParseError.UnsupportedVersion;
                return false;
            }
            endpointText = input.Substring(V1Prefix.Length);
        }
        else
        {
            endpointText = input;
        }

        var parts = endpointText.Split(':');
        if (parts.Length is < 1 or > 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            error = LanInviteParseError.InvalidFormat;
            return false;
        }
        if (!TryParseDottedDecimalIpv4(parts[0], out var address))
        {
            error = LanInviteParseError.InvalidAddress;
            return false;
        }
        if (!IsConnectableAddress(address))
        {
            error = LanInviteParseError.UnsafeAddress;
            return false;
        }

        ushort port = DefaultGamePort;
        if (parts.Length == 2 && !TryParsePort(parts[1], out port))
        {
            error = LanInviteParseError.InvalidPort;
            return false;
        }

        endpoint = new LanJoinEndpoint(address, port);
        error = LanInviteParseError.None;
        return true;
    }

    public static IReadOnlyList<LanJoinEndpoint> SelectShareableEndpoints(
        IEnumerable<IPAddress> addresses,
        ushort port = DefaultGamePort,
        IPAddress preferredAddress = null
    )
    {
        if (addresses == null || port == 0)
            return Array.Empty<LanJoinEndpoint>();
        return addresses
            .Where(IsShareableAddress)
            .Distinct()
            // Android devices can expose container/VPN interfaces alongside
            // Wi-Fi. Prefer the address selected by the default route so Copy
            // does not silently choose an unreachable virtual interface.
            .OrderBy(address => address.Equals(preferredAddress) ? 0 : 1)
            .ThenBy(AddressPriority)
            .ThenBy(address => AddressSortKey(address), StringComparer.Ordinal)
            .Take(MaxShareChoices)
            .Select(address => new LanJoinEndpoint(address, port))
            .ToList();
    }

    public static bool IsShareableAddress(IPAddress address)
    {
        if (!IsConnectableAddress(address))
            return false;
        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsConnectableAddress(IPAddress address)
    {
        if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] != 0
            && bytes[0] != 127
            && bytes[0] < 224
            && !bytes.All(b => b == 255)
            && !IPAddress.IsLoopback(address);
    }

    private static int AddressPriority(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31)
            return 0;
        if (b[0] == 100 && b[1] is >= 64 and <= 127)
            return 1;
        return 2;
    }

    private static string AddressSortKey(IPAddress address) =>
        string.Join(".", address.GetAddressBytes().Select(b => b.ToString("D3")));

    private static bool TryParseDottedDecimalIpv4(string text, out IPAddress address)
    {
        address = null;
        var parts = text.Split('.');
        if (parts.Length != 4)
            return false;

        var bytes = new byte[4];
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            // Reject ambiguous legacy forms such as 127.1, hexadecimal/octal
            // octets, signs, Unicode digits, and leading zeroes.
            if (part.Length is < 1 or > 3 || part.Length > 1 && part[0] == '0')
                return false;
            int value = 0;
            foreach (char c in part)
            {
                if (c is < '0' or > '9')
                    return false;
                value = value * 10 + (c - '0');
            }
            if (value > byte.MaxValue)
                return false;
            bytes[i] = (byte)value;
        }

        address = new IPAddress(bytes);
        return true;
    }

    private static bool TryParsePort(string text, out ushort port)
    {
        port = 0;
        if (text.Length is < 1 or > 5)
            return false;
        int value = 0;
        foreach (char c in text)
        {
            if (c is < '0' or > '9')
                return false;
            value = value * 10 + (c - '0');
        }
        if (value is < 1 or > ushort.MaxValue)
            return false;
        port = (ushort)value;
        return true;
    }
}
