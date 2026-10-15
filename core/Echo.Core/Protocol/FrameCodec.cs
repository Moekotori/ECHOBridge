using System.Buffers.Binary;

namespace Echo.Core.Protocol;

public static class FrameCodec
{
    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ProtocolConstants.MaxFrameBytes)
        {
            throw new ProtocolLimitException($"Frame payload {payload.Length} exceeds {ProtocolConstants.MaxFrameBytes} bytes.");
        }

        var frame = new byte[ProtocolConstants.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, ProtocolConstants.HeaderSize), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(ProtocolConstants.HeaderSize));
        return frame;
    }

    public static int ReadPayloadLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < ProtocolConstants.HeaderSize)
        {
            throw new ProtocolLimitException("Incomplete frame header.");
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length > ProtocolConstants.MaxFrameBytes)
        {
            throw new ProtocolLimitException($"Frame length {length} exceeds {ProtocolConstants.MaxFrameBytes} bytes.");
        }

        return (int)length;
    }
}

public sealed class ProtocolLimitException : Exception
{
    public ProtocolLimitException(string message)
        : base(message)
    {
    }
}
