namespace Echo.Bridge;

/// <summary>Only public error codes cross the LAN or enter diagnostics.</summary>
public sealed class BridgeException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
}
