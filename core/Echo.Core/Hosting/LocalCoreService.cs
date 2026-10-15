namespace Echo.Core.Hosting;

/// <summary>Independent Core lifetime, using the existing bounded execution lanes and protocol.</summary>
public sealed class LocalCoreService(LocalCoreServiceOptions options, TextWriter log)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        options.PrepareDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        // Keep lock files after exit: deleting a locked inode allows a second owner to bypass it.
        using var libraryLock = LocalCoreServiceOptions.AcquireLock(options.DatabasePath + ".echo-core-service.lock");
        using var directoryLock = LocalCoreServiceOptions.AcquireLock(Path.Combine(options.DirectoryPath, "service.lock"));
        using var listener = new LocalCoreServiceListener(options);
        var bootstrap = Echo.Core.Database.LibraryDatabaseBootstrap.Initialize(options.DatabasePath, cancellationToken);
        if (options.RecoverInterruptedScans)
            Echo.Core.Writes.InterruptedScanJobRecovery.Recover(options.DatabasePath, cancellationToken, options.RecoverScansCreatedBefore);
        options.PublishDescriptor(bootstrap);
        log.WriteLine("[echo-library-core] local service ready");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                await RunSessionAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { File.Delete(options.DescriptorPath); }
    }

    private async Task RunSessionAsync(Stream connection, CancellationToken cancellationToken)
    {
        StdioServer? server = null;
        try
        {
            // Backup/repair may have replaced the database while the old session was fully stopped.
            Echo.Core.Database.LibraryDatabaseBootstrap.Initialize(options.DatabasePath, cancellationToken);
            server = new StdioServer(connection, connection, TextWriter.Null, validate: options.ValidateRequest,
                amendHandshake: handshake =>
                {
                    handshake.Capabilities.Add("library.localService/v1");
                    handshake.Capabilities.Add("library.schemaOwner/v1");
                }, cancelResponsesOnDisconnect: true);
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Protocol/database errors are scoped to the client; never log paths or payloads.
            log.WriteLine("[echo-library-core] local client session failed");
        }
        finally
        {
            // Peer EOF is a cleanup acknowledgement. Never send it while a worker still owns resources.
            server?.Dispose();
            if (server is not null && !server.IsStopped)
            {
                log.WriteLine("[echo-library-core] session cleanup unconfirmed; stopping service");
                Environment.Exit(1);
            }
            connection.Dispose();
        }
    }
}
