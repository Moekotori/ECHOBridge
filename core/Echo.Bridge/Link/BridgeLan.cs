using System.Net;
using System.Net.NetworkInformation;

namespace Echo.Bridge.Link;

public static class BridgeLan
{
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168);
    }
    public static bool IsAllowed(IPAddress? address)
    {
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        return address is not null && (IPAddress.IsLoopback(address) || IsPrivate(address));
    }
    public static IPAddress[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses).Select(item => item.Address)
        .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && IsPrivate(address))
        .Distinct().Take(4).ToArray();
}
