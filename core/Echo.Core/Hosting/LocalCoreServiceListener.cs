using System.IO.Pipes;
using System.Net.Sockets;

namespace Echo.Core.Hosting;

/// <summary>One current-user IPC session at a time; never binds a TCP port.</summary>
internal sealed class LocalCoreServiceListener : IDisposable
{
    private readonly LocalCoreServiceOptions _options;
    private readonly Socket? _socket;
    private NamedPipeServerStream? _pipe;

    public LocalCoreServiceListener(LocalCoreServiceOptions options)
    {
        _options = options;
        if (OperatingSystem.IsWindows()) _pipe = CreatePipe();
        else
        {
            File.Delete(options.Endpoint);
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                _socket.Bind(new UnixDomainSocketEndPoint(options.Endpoint));
                File.SetUnixFileMode(options.Endpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                _socket.Listen(1);
            }
            catch { _socket.Dispose(); throw; }
        }
    }

    public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
    {
        if (_socket is not null)
            return new NetworkStream(await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false), ownsSocket: true);
        _pipe ??= CreatePipe();
        await _pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connected = _pipe;
        _pipe = null;
        return connected;
    }

    private NamedPipeServerStream CreatePipe() => new(_options.PipeName, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);

    public void Dispose()
    {
        _pipe?.Dispose();
        _socket?.Dispose();
        if (!OperatingSystem.IsWindows()) File.Delete(_options.Endpoint);
    }
}
