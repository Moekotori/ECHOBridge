using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Echo.Bridge.Windows;

internal sealed class BridgeSingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipe;
    private readonly CancellationTokenSource lifetime = new();
    private Task? listener;
    public bool IsFirst { get; }
    public BridgeSingleInstance()
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))[..24];
        pipe = "echo-bridge-ui-" + key;
        mutex = new Mutex(false, "Local\\" + pipe);
        try { IsFirst = mutex.WaitOne(0); } catch (AbandonedMutexException) { IsFirst = true; }
    }
    public void ActivateExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(1000); client.Write("show"u8);
        }
        catch (Exception error) when (error is IOException or TimeoutException) { }
    }
    public void Listen(Action show) => listener = Run(show);
    private async Task Run(Action show)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                readTimeout.CancelAfter(1000);
                var message = new byte[4];
                try { await server.ReadExactlyAsync(message, readTimeout.Token).ConfigureAwait(false); if (message.AsSpan().SequenceEqual("show"u8)) show(); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException) { }
    }
    public void Dispose()
    {
        lifetime.Cancel();
        listener?.GetAwaiter().GetResult();
        lifetime.Dispose();
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
