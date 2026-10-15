using Echo.Core.Maintenance;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Albums;
using SQLitePCL;

namespace Echo.Core.Writes;

public static class LibraryMutations
{
    private static readonly bool ReuseScanCommands = Environment.GetEnvironmentVariable("ECHO_CORE_SCAN_COMMAND_REUSE") != "0";
    public const string Capability = "library.mutations/v2";
    /// <summary>repair / snapshots / seen / markMissing operations plus the getScanMarkedMissing read.</summary>
    public const string ScanTailCapability = "library.scanTail/v1";
    public const string AlbumGroupingCapability = "library.albumGrouping/v1";
    public const string GroupingRebuildCapability = "library.groupingRebuild/v1";
    public static LibraryMutationResult Execute(string path, LibraryMutation request, CancellationToken cancellation = default)
    {
        if (request.Items.Count > 128 || request.Candidates.Count > 128 || request.Repairs.Count > 128 || request.Snapshots.Count > 128)
            throw new ArgumentException("At most 128 rows per mutation.");
        using var connection = SortKeyMaintenanceSql.OpenWriter(path);
        if (request.Operation is "albumGrouping" or "groupingRebuild") GroupingRebuildWriter.Register(connection);
        using var interrupt = cancellation.Register(() => raw.sqlite3_interrupt(connection.Handle));
        cancellation.ThrowIfCancellationRequested();
        using var transaction = connection.BeginTransaction();
        var sql = new TransactionSql(connection, transaction);
        using var scanSql = request.Operation == "scan" && ReuseScanCommands ? new TransactionSql(connection, transaction, reuseCommands: true) : null;
        var journal = new CommandReceiptJournal<LibraryMutation,LibraryMutationResult>(sql,
            CoreJsonContext.Default.LibraryMutation, CoreJsonContext.Default.LibraryMutationResult, state => new(state), libraryMutation: true);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var streamJournal = request.Stream is null ? null : new MutationStreamJournal<LibraryMutation, LibraryMutationResult>(sql,
            CoreJsonContext.Default.LibraryMutation, CoreJsonContext.Default.LibraryMutationResult, state => new(state));
        var previous = streamJournal is null ? journal.Find(request, now) : streamJournal.Find(request, now);
        if (previous is not null) return previous;
        ScanWriteGuard.ValidateMutation(sql, request);
        ScanExecutionCheckpoint.ValidateMutation(sql, request);
        var missingReplay = request.Operation == "markMissing" && request.MarkMissing is not null
            && sql.Value("SELECT 1 FROM core_scan_tasks WHERE job_id=$0 AND missing_done=1",request.MarkMissing.ScanJobId) is not null;
        var result = request.Operation switch
        {
            "groupingRebuild" => GroupingRebuildWriter.Rebuild(sql, request.GroupingRebuild
                ?? throw new ArgumentException("Grouping rebuild required."), cancellation),
            "albumGrouping" => SeedAlbums(sql, request.AlbumGrouping
                ?? throw new ArgumentException("Album grouping batch required."), cancellation),
            "albumCovers" => ScanAlbumCoverRefreshWriter.Refresh(sql, request.AlbumCovers
                ?? throw new ArgumentException("Album cover refresh batch required.")),
            "artistRefresh" => ScanArtistRefreshWriter.Enqueue(sql, request.ArtistRefresh
                ?? throw new ArgumentException("Artist refresh batch required.")),
            "scan" => Scan(scanSql ?? sql, request),
            "cleanup" => CleanupWriter.Delete(sql, request.Candidates),
            "repair" => new LibraryMutationResult(Repairs: request.Repairs.Select(item => ScanRepairWriter.Write(sql, item)).ToList()),
            "snapshots" => Snapshots(sql, request.Snapshots),
            "seen" => Seen(sql, request.Seen ?? throw new ArgumentException("Seen paths required.")),
            "markMissing" => new LibraryMutationResult(Removed: ScanMissingWriter.Mark(sql, request.MarkMissing ?? throw new ArgumentException("markMissing request required."))),
            _ => throw new ArgumentException("Unknown library mutation."),
        };
        if (!missingReplay) ScanCommittedCounts.Record(sql, request, result);
        ScanExecutionCheckpoint.RecordMutation(sql, request);
        if (streamJournal is null) journal.Save(request, result, now);
        else streamJournal.Save(request, result, now);
        cancellation.ThrowIfCancellationRequested();
        transaction.Commit();
        return result;
    }

    private static LibraryMutationResult SeedAlbums(TransactionSql sql, AlbumGroupingBatch request, CancellationToken cancellation)
    {
        if (request.Strategy is not ("standard" or "sameTitleAndCover"))
            throw new ArgumentException("Unknown album grouping strategy.");
        var ids = request.PreserveMembership
            ? AlbumGroupingWriter.Refresh(sql, request.TrackIds, request.UpdatedAt, cancellation)
            : AlbumGroupingWriter.Seed(sql, request.TrackIds, request.UpdatedAt, cancellation, request.Strategy == "sameTitleAndCover");
        for (var offset = 0; offset < request.TrackIds.Count; offset += 128)
            ScanArtistRefreshWriter.Enqueue(sql, new(request.TrackIds.Skip(offset).Take(128).ToList(), [], request.UpdatedAt));
        for (var offset = 0; offset < ids.Count; offset += 128)
            ScanArtistRefreshWriter.Enqueue(sql, new([], ids.Skip(offset).Take(128).ToList(), request.UpdatedAt));
        AlbumGroupingStage.Release(sql);
        return new LibraryMutationResult(AlbumIds: ids.ToList());
    }

    private static LibraryMutationResult Scan(TransactionSql sql, LibraryMutation request)
    {
        var tracks = request.Items.Select(item => ScanTrackWriter.Write(sql, item)).ToList();
        if (request.QueueArtistRefresh && tracks.Count > 0)
        {
            var timestamp = request.ScanTimestamp ?? JsonFields.Text(request.Items[0].Track, "updatedAt")
                ?? throw new ArgumentException("Scan artist refresh timestamp required.");
            ScanArtistRefreshWriter.Enqueue(sql, new(tracks.Select(track => track.Id).ToList(), [], timestamp));
        }
        if (request.ScanJobId is not null)
            ScanInboxStaging.RecordAdded(sql, request.ScanJobId, request.ScanTimestamp, tracks);
        return new LibraryMutationResult(Tracks: tracks);
    }

    private static LibraryMutationResult Snapshots(TransactionSql sql, List<ScanSnapshotItem> snapshots)
    {
        foreach (var snapshot in snapshots) ScanSnapshotWriter.Write(sql, snapshot);
        return new LibraryMutationResult();
    }

    private static LibraryMutationResult Seen(TransactionSql sql, ScanSeenPaths seen)
    {
        ScanMissingWriter.Seen(sql, seen);
        return new LibraryMutationResult();
    }
}
