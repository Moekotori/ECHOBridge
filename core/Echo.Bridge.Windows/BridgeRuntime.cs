namespace Echo.Bridge.Windows;

/// <summary>The form owns a single sharing session; transitions are serialized independently of window visibility.</summary>
internal sealed class BridgeRuntime
{
    private readonly SemaphoreSlim transition = new(1, 1);
    public BridgeSession? Session { get; private set; }
    public async Task Start(BridgeSettings settings)
    {
        await transition.WaitAsync();
        try
        {
            if (Session is not null) return;
            Session = await BridgeSession.StartAsync(settings.DatabasePath ?? "", settings.DeviceId, settings.DeviceName);
        }
        finally { transition.Release(); }
    }
    public async Task Stop()
    {
        await transition.WaitAsync();
        try { if (Session is not null) { await Session.DisposeAsync(); Session = null; } }
        finally { transition.Release(); }
    }
}
