using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Echo.Bridge.Link;

/// <summary>At most four interfaces, one refresh timer; offline startup keeps retrying without retaining a catalog.</summary>
public sealed class BridgeMdnsAdvertiser(string id, string name, int port) : IAsyncDisposable
{
    private sealed record Interface(IPAddress Address, UdpClient Socket, Task Receiver);
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint Destination = new(Group, 5353);
    private readonly Dictionary<IPAddress, Interface> interfaces = [];
    private readonly CancellationTokenSource lifetime = new();
    private Task? refresh;
    private bool disposed;
    public int InterfaceCount => Volatile.Read(ref count);
    private int count;

    public async Task StartAsync()
    {
        await Refresh();
        refresh = Run();
    }
    private async Task Run()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try { while (await timer.WaitForNextTickAsync(lifetime.Token)) await Refresh(); }
        catch (OperationCanceledException) { }
    }
    private async Task Refresh()
    {
        IPAddress[] addresses;
        try { addresses = BridgeLan.Addresses(); } catch (NetworkInformationException) { addresses = []; }
        foreach (var item in interfaces.Values.Where(item => !addresses.Contains(item.Address) || item.Receiver.IsCompleted).ToArray())
            await Remove(item.Address);
        foreach (var address in addresses)
        {
            if (interfaces.ContainsKey(address)) continue;
            var socket = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                socket.Client.ExclusiveAddressUse = false;
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
                socket.JoinMulticastGroup(Group, address);
                socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                interfaces.Add(address, new(address, socket, Receive(socket, address)));
            }
            catch (SocketException) { socket.Dispose(); }
        }
        Volatile.Write(ref count, interfaces.Count);
        foreach (var item in interfaces.Values) await Announce(item.Socket, item.Address, false);
    }
    private async Task Receive(UdpClient socket, IPAddress address)
    {
        var lastResponse = Environment.TickCount64 - 250;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var message = await socket.ReceiveAsync(lifetime.Token);
                var bytes = message.Buffer;
                if (!BridgeLan.IsPrivate(message.RemoteEndPoint.Address) || bytes.Length is < 12 or > 16_384
                    || (bytes[2] & 0x80) != 0 || Environment.TickCount64 - lastResponse < 250
                    || !Encoding.ASCII.GetString(bytes).Contains("_echo-link", StringComparison.Ordinal)) continue;
                lastResponse = Environment.TickCount64;
                await Announce(socket, address, false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task Announce(UdpClient socket, IPAddress address, bool goodbye)
    {
        try { await socket.SendAsync(BridgeMdnsPacket.Announcement(id, name, address, port, goodbye), Destination); }
        catch (Exception error) when (error is SocketException or ObjectDisposedException) { }
    }
    private async Task Remove(IPAddress address)
    {
        if (!interfaces.Remove(address, out var item)) return;
        await Announce(item.Socket, item.Address, true);
        item.Socket.Dispose();
        await item.Receiver;
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await lifetime.CancelAsync();
        if (refresh is not null) await refresh;
        foreach (var address in interfaces.Keys.ToArray()) await Remove(address);
        Volatile.Write(ref count, 0);
        lifetime.Dispose();
        disposed = true;
    }
}
