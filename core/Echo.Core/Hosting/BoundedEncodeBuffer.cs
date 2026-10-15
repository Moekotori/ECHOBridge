using System.Buffers;
using System.Buffers.Binary;
using System.Text.Encodings.Web;
using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Hosting;

internal sealed class BoundedEncodeBuffer : IBufferWriter<byte>, IDisposable
{
    internal const int InitialBytes = 128 * 1024;
    private const int MinWrite = 256;
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private byte[] _rented = [];
    private int _index;
    private bool _hasRented;

    public ReadOnlyMemory<byte> EncodeFrame(Action<Utf8JsonWriter> write)
    {
        EnsureCapacity(ProtocolConstants.HeaderSize + InitialBytes);
        _index = ProtocolConstants.HeaderSize;
        using var writer = new Utf8JsonWriter(this, WriterOptions);
        write(writer);
        writer.Flush();
        var payloadLength = _index - ProtocolConstants.HeaderSize;
        if (payloadLength > ProtocolConstants.MaxFrameBytes)
        {
            throw new ProtocolLimitException("Encoded Core response exceeded the frame limit.");
        }

        BinaryPrimitives.WriteUInt32BigEndian(_rented.AsSpan(0, ProtocolConstants.HeaderSize), (uint)payloadLength);
        return new ReadOnlyMemory<byte>(_rented, 0, _index);
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_index + count > _rented.Length)
        {
            throw new InvalidOperationException("Utf8JsonWriter advanced past the rented encode buffer.");
        }

        _index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureRemaining(Math.Max(sizeHint, MinWrite));
        return _rented.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    public void Dispose() => Return();

    private void EnsureRemaining(int needed)
    {
        if (_rented.Length - _index >= needed)
        {
            return;
        }

        var cap = ProtocolConstants.HeaderSize + ProtocolConstants.MaxFrameBytes;
        if ((long)_index + needed > cap)
        {
            throw new ProtocolLimitException("Encoded Core response exceeded the frame limit.");
        }

        var desired = Math.Max(_rented.Length <= 0 ? InitialBytes : _rented.Length * 2, _index + needed);
        EnsureCapacity((int)Math.Min(cap, desired));
    }

    private void EnsureCapacity(int size)
    {
        if (_hasRented && _rented.Length >= size)
        {
            return;
        }

        var next = ArrayPool<byte>.Shared.Rent(size);
        if (_hasRented && _index > 0)
        {
            Buffer.BlockCopy(_rented, 0, next, 0, _index);
        }

        Return();
        _rented = next;
        _hasRented = true;
    }

    private void Return()
    {
        if (!_hasRented)
        {
            return;
        }

        var buffer = _rented;
        _rented = [];
        _hasRented = false;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
