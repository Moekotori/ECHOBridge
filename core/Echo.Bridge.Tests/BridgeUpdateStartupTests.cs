using System.Diagnostics;
using Echo.Bridge.Windows.Updates;
using Xunit;

namespace Echo.Bridge.Tests;

public sealed class BridgeUpdateStartupTests
{
    private static readonly Version Version=new(26,10,17,1);
    private static Process Child(string command)
    {
        var start=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,CreateNoWindow=true};
        foreach(var argument in new[] {"-NoProfile","-NonInteractive","-Command",command}) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    [Fact]
    public void AConfirmationMatchesTheActualProcessAndVersion()
    {
        using var fixture=new BridgeFixture(); var job=Path.Combine(fixture.Root,"job.json");
        BridgeUpdateStartup.Confirm(job,Version);
        using var process=Process.GetCurrentProcess();
        BridgeUpdateStartup.Wait(process,job,Version,TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidDataException>(()=>BridgeUpdateStartup.Wait(process,job,new Version(26,10,17,2),TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AnotherProcessCannotConfirmTheRestartedInstance()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture=new BridgeFixture(); var job=Path.Combine(fixture.Root,"job.json");
        using var child=Child("Start-Sleep -Seconds 10");
        try
        {
            BridgeUpdateStartup.Confirm(job,Version);
            Assert.Throws<InvalidDataException>(()=>BridgeUpdateStartup.Wait(child,job,Version,TimeSpan.FromSeconds(1)));
        }
        finally { BridgeUpdateStartup.Stop(child); }
        Assert.True(child.HasExited);
    }

    [Fact]
    public void AnExitedOrUnconfirmedRestartDoesNotCommit()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture=new BridgeFixture(); var job=Path.Combine(fixture.Root,"job.json");
        using (var exited=Child("exit 42"))
        {
            Assert.True(exited.WaitForExit(3000));
            Assert.Throws<IOException>(()=>BridgeUpdateStartup.Wait(exited,job,Version,TimeSpan.FromSeconds(1)));
        }
        using var unconfirmed=Child("Start-Sleep -Seconds 10");
        try { Assert.Throws<IOException>(()=>BridgeUpdateStartup.Wait(unconfirmed,job,Version,TimeSpan.FromMilliseconds(100))); }
        finally { BridgeUpdateStartup.Stop(unconfirmed); }
        Assert.True(unconfirmed.HasExited);
    }
}
