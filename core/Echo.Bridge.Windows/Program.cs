namespace Echo.Bridge.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--apply-update", var updateJob]) { Updates.BridgeUpdateHandoff.Apply(updateJob); return; }
        if (args is ["--apply-update-smoke", var smokeJob, var smokeCache])
        { Updates.BridgeUpdateHandoff.Apply(smokeJob, BridgeSmoke.Evidence(smokeCache)); return; }
        if (args is ["--update-download-smoke", var downloadEvidence])
        { Updates.BridgeUpdateSmoke.Download(downloadEvidence).GetAwaiter().GetResult(); return; }
        if (args is ["--updated-smoke", var completeJob, var completeEvidence])
        { Updates.BridgeUpdateSmoke.Complete(completeJob, completeEvidence); return; }
        if (args is ["--sharing-smoke", var sharingLibrary, var sharingEvidence])
        { BridgeSharingSmoke.Run(sharingLibrary, sharingEvidence); return; }
        if (args is ["--sharing-smoke", var automaticLibrary, var automaticEvidence, var automaticAppData])
        { BridgeSharingSmoke.Run(automaticLibrary, automaticEvidence, automaticAppData); return; }
        if (args is ["--startup-smoke", var startupEvidence]) { BridgeStartupSmoke.Run(startupEvidence); return; }
        if (args is ["--sharing-recovery-smoke", var recoveryLibrary, var recoveryEvidence, var recoveryMode])
        { BridgeSharingRecoverySmoke.Run(recoveryLibrary, recoveryEvidence, recoveryMode); return; }
        if (args is ["--selection-smoke", var selectionAppData, var expectedDatabase, var selectionEvidence, var selectionMode])
        { BridgeLibrarySelectionSmoke.Run(selectionAppData, expectedDatabase, selectionEvidence, selectionMode); return; }
        if (args is ["--ui-smoke", var evidence]) { BridgeSmoke.Window(evidence); return; }
        if (args is ["--ui-smoke", var themedEvidence, var theme]) { BridgeSmoke.Window(themedEvidence, theme == "dark"); return; }
        if (args is ["--library-smoke", var database, var result]) { BridgeSmoke.Library(database, result).GetAwaiter().GetResult(); return; }
        if (args is ["--discovery-smoke", var library, var discoveryEvidence]) { BridgeSmoke.Discovery(library, discoveryEvidence).GetAwaiter().GetResult(); return; }
        if (args is ["--discovery-smoke", var fixtureLibrary, var fixtureEvidence, var fixtureAppData])
        { BridgeSmoke.Discovery(fixtureLibrary, fixtureEvidence, fixtureAppData).GetAwaiter().GetResult(); return; }
        using var instance = new BridgeSingleInstance();
        var startInTray = args is ["--startup"];
        string? restartJob = null;
        if (args is ["--updated", var completedJob])
        {
            try { startInTray = Updates.BridgeUpdateHandoff.RestartInTray(completedJob); restartJob = completedJob; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
            { Environment.ExitCode = 1; return; }
        }
        if (!instance.IsFirst) { if (!startInTray) instance.ActivateExisting(); return; }
        ApplicationConfiguration.Initialize();
        using var form = new BridgeForm(BridgeSettings.Load());
        form.EnableUpdates();
        _ = form.Handle; instance.Listen(form.Restore);
        Task? updateCompletion = null;
        if (restartJob is not null)
            form.BeginInvoke((Action)(() =>
            {
                updateCompletion = Task.Run(() =>
                {
                    try { Updates.BridgeUpdateHandoff.Complete(restartJob); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
                    { MessageBox.Show("更新已启动，临时文件未能清理。请保留原目录中的备份。", "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                });
            }));
        if (startInTray)
        {
            using var context = new ApplicationContext();
            form.FormClosed += (_, _) => context.ExitThread();
            form.StartInTray(); Application.Run(context);
        }
        else Application.Run(form);
        updateCompletion?.GetAwaiter().GetResult();
    }
}
