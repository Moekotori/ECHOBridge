using Echo.Core.Database;
using Echo.Core.Maintenance;
using Echo.Core.Protocol;

namespace Echo.Core.Playback;

/// <summary>One short write transaction per playback event, recovered through the shared receipt / stream journals.</summary>
public static class PlaybackCommands
{
    public const string Capability = "playback.commands/v1";

    public static PlaybackCommandResult Execute(string databasePath, PlaybackCommand request)
    {
        using var connection = SortKeyMaintenanceSql.OpenWriter(databasePath);
        using var transaction = connection.BeginTransaction();
        var sql = new TransactionSql(connection, transaction);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var streamJournal = request.Stream is null ? null : new MutationStreamJournal<PlaybackCommand, PlaybackCommandResult>(sql,
            CoreJsonContext.Default.PlaybackCommand, CoreJsonContext.Default.PlaybackCommandResult, state => new(state));
        var journal = new CommandReceiptJournal<PlaybackCommand, PlaybackCommandResult>(sql,
            CoreJsonContext.Default.PlaybackCommand, CoreJsonContext.Default.PlaybackCommandResult, state => new(state));
        var previous = streamJournal is null ? journal.Find(request, now) : streamJournal.Find(request, now);
        if (previous is not null) return previous;
        var result = request.Operation switch
        {
            "start" => PlaybackHistoryWriter.Start(sql, request.Start ?? throw new ArgumentException("Playback start payload required.")),
            "finish" => PlaybackHistoryWriter.Finish(sql, request.Finish ?? throw new ArgumentException("Playback finish payload required.")),
            "playCount" => PlaybackHistoryWriter.PlayCount(sql, request.TrackId ?? "", request.Timestamp ?? ""),
            _ => throw new ArgumentException("Unknown playback command."),
        };
        if (streamJournal is null) journal.Save(request, result, now);
        else streamJournal.Save(request, result, now);
        transaction.Commit();
        return result;
    }
}
