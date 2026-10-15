namespace Echo.Core.Hosting;

/// <summary>Owned desktop services terminate when their parent's private stdin pipe closes.</summary>
public static class ManagedServiceLifetime
{
    public static Task WatchAsync(Stream parentInput, CancellationTokenSource lifetime) => Task.Run(async () =>
    {
        try
        {
            var buffer = new byte[1];
            while (!lifetime.IsCancellationRequested
                && await parentInput.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false) != 0) { }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
        }
    });
}
