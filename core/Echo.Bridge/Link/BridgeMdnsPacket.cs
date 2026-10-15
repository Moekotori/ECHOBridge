using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Echo.Bridge.Link;

public static class BridgeMdnsPacket
{
    public static byte[] Announcement(string id, string displayName, IPAddress address, int port, bool goodbye = false)
    {
        const string service = "_echo-link._tcp.local";
        var instance = "ECHO Bridge " + id[^12..] + "." + service;
        var host = "echo-" + id + ".local";
        var ttl = goodbye ? 0u : 45u;
        using var packet = new MemoryStream();
        packet.Write(new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 5, 0, 0, 0, 0 });
        Record(packet, "_services._dns-sd._udp.local", 12, ttl, Name(service), false);
        Record(packet, service, 12, ttl, Name(instance), false);
        using var srv = new MemoryStream();
        srv.Write(new byte[4]); Word(srv, (ushort)port); srv.Write(Name(host));
        Record(packet, instance, 33, ttl, srv.ToArray());
        using var txt = new MemoryStream();
        foreach (var text in new[] { "name=" + displayName, "deviceId=" + id, "version=1", "versions=1", "direct=1",
            "library=1", "remoteControl=0", "role=bridge", "apiPath=/echo-link/peer/v1" })
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 255) throw new ArgumentException("mDNS text exceeds its wire limit.");
            txt.WriteByte((byte)bytes.Length); txt.Write(bytes);
        }
        Record(packet, instance, 16, ttl, txt.ToArray());
        Record(packet, host, 1, ttl, address.GetAddressBytes());
        return packet.ToArray();
    }
    private static byte[] Name(string name)
    {
        using var stream = new MemoryStream();
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > 63) throw new ArgumentException("mDNS label exceeds its wire limit.");
            stream.WriteByte((byte)bytes.Length); stream.Write(bytes);
        }
        stream.WriteByte(0); return stream.ToArray();
    }
    private static void Record(Stream output, string name, ushort type, uint ttl, byte[] data, bool flush = true)
    {
        output.Write(Name(name)); Word(output, type); Word(output, flush ? (ushort)0x8001 : (ushort)1);
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, ttl); output.Write(number);
        Word(output, (ushort)data.Length); output.Write(data);
    }
    private static void Word(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); output.Write(bytes);
    }
}
