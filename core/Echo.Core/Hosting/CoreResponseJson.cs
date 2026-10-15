using System.Text.Json;
using Echo.Core.Protocol;
using Echo.Core.Query;

namespace Echo.Core.Hosting;

internal static class CoreResponseJson
{
    public static void WriteOk(Utf8JsonWriter writer, long id, object? result)
    {
        writer.WriteStartObject();
        writer.WriteNumber("v", ProtocolConstants.ProtocolVersion);
        writer.WriteNumber("id", id);
        writer.WriteBoolean("ok", true);
        writer.WriteString("executedBy", "dotnet");
        writer.WritePropertyName("result");
        switch (result)
        {
            case ScanCueBatchResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanCueBatchResult);
                break;
            case ScanCueResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanCueResult);
                break;
            case NativeMetadataResponse value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.NativeMetadataResponse);
                break;
            case CoverCandidateResponse value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.CoverCandidateResponse);
                break;
            case AsciiSearchTermsResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.AsciiSearchTermsResult);
                break;
            case LibraryMutationResult value:
                JsonSerializer.Serialize(writer,value,CoreJsonContext.Default.LibraryMutationResult);
                break;
            case ScanJobCommandResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanJobCommandResult);
                break;
            case ScanFinalizeResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanFinalizeResult);
                break;
            case Echo.Core.Duplicates.DuplicateIndexSummary value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.DuplicateIndexSummary);
                break;
            case Echo.Core.Duplicates.DuplicateMemberPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.DuplicateMemberPage);
                break;
            case Echo.Core.Duplicates.DuplicateCleanupCandidatesPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.DuplicateCleanupCandidatesPage);
                break;
            case CleanupPage value:
                JsonSerializer.Serialize(writer,value,CoreJsonContext.Default.CleanupPage);
                break;
            case ScanCacheStatesPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanCacheStatesPage);
                break;
            case ScanChangedFilesResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanChangedFilesResult);
                break;
            case ScanMarkedMissingPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ScanMarkedMissingPage);
                break;
            case TrackRefPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.TrackRefPage);
                break;
            case PlaybackCommandResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaybackCommandResult);
                break;
            case Dictionary<string, bool> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.DictionaryStringBoolean);
                break;
            case Dictionary<string, int> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.DictionaryStringInt32);
                break;
            case Echo.Core.SmartPlaylists.SmartPreviewResult value:
                JsonSerializer.Serialize(writer,value,CoreJsonContext.Default.SmartPreviewResult);
                break;
            case PlaylistResultPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaylistResultPage);
                break;
            case PlaylistCommandResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaylistCommandResult);
                break;
            case PersonalTagCommandResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PersonalTagCommandResult);
                break;
            case null:
                writer.WriteNullValue();
                break;
            case LibraryAlbumDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryAlbumDto);
                break;
            case SortKeyMaintenanceResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.SortKeyMaintenanceResult);
                break;
            case HandshakeResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.HandshakeResult);
                break;
            case PingResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PingResult);
                break;
            case CancelResult value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.CancelResult);
                break;
            case LibrarySummaryDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibrarySummaryDto);
                break;
            case LibraryPageDto<LibraryTrackListRowDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryPageDtoLibraryTrackListRowDto);
                break;
            case LibraryPageDto<LibraryAlbumDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryPageDtoLibraryAlbumDto);
                break;
            case LibraryPageDto<LibraryArtistDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryPageDtoLibraryArtistDto);
                break;
            case LibraryPageDto<LibraryGenreDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryPageDtoLibraryGenreDto);
                break;
            case List<LibraryTrackDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ListLibraryTrackDto);
                break;
            case AlbumFocusFacetsDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.AlbumFocusFacetsDto);
                break;
            case List<LibraryPlaylistDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ListLibraryPlaylistDto);
                break;
            case List<LibraryFolderOverviewDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ListLibraryFolderOverviewDto);
                break;
            case List<LibraryFolderNodeDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ListLibraryFolderNodeDto);
                break;
            case CoreQualityIssuePage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.CoreQualityIssuePage);
                break;
            case List<CoreInboxBatch> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ListCoreInboxBatch);
                break;
            case CoreInboxPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.CoreInboxPage);
                break;
            case List<CorePersonalTagDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.ListCorePersonalTagDto);
                break;
            case CorePersonalTagItemsPage value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.CorePersonalTagItemsPage);
                break;
            case SteamAchievementHistoryStatsDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.SteamAchievementHistoryStatsDto);
                break;
            case PlaybackHistorySummaryDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaybackHistorySummaryDto);
                break;
            case LibraryPageDto<PlaybackHistoryEntryDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryPageDtoPlaybackHistoryEntryDto);
                break;
            case PlaybackStatsDashboardDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaybackStatsDashboardDto);
                break;
            case PlaybackStatsInsightsDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaybackStatsInsightsDto);
                break;
            case PlaybackMemoryGraphDto value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.PlaybackMemoryGraphDto);
                break;
            case LibraryPageDto<LibraryPlaylistItemDto> value:
                JsonSerializer.Serialize(writer, value, CoreJsonContext.Default.LibraryPageDtoLibraryPlaylistItemDto);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Core result type {result.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    public static void WriteFail(Utf8JsonWriter writer, long id, Exception exception, TextWriter log)
    {
        var message = exception.Message.Length > 240 ? exception.Message[..240] : exception.Message;
        log.WriteLine($"[echo-library-core] {exception.GetType().Name}: {message}");
        var (code, retryable) = exception switch
        {
            Echo.Core.Media.NativeMetadataReadException media => (media.Code, false),
            UnexecutableQueryException => ("unexecutable_query", false),
            UnsupportedQueryException => ("unsupported_combination", false),
            Echo.Core.Query.PlaylistSortKeysNotReadyException => ("sort_keys_not_ready", false),
            StaleDatabaseGenerationException => ("stale_generation", false),
            Echo.Core.Media.ScanCueBatchLimitException => ("protocol_limit", false),
            ProtocolLimitException => ("protocol_limit", false),
            OperationCanceledException => ("cancelled", false),
            FileNotFoundException => ("database_missing", false),
            _ => ("query_failed", false),
        };
        writer.WriteStartObject();
        writer.WriteNumber("v", ProtocolConstants.ProtocolVersion);
        writer.WriteNumber("id", id);
        writer.WriteBoolean("ok", false);
        writer.WriteString("executedBy", "dotnet");
        writer.WritePropertyName("error");
        writer.WriteStartObject();
        writer.WriteString("code", code);
        writer.WriteString("message", message);
        writer.WriteBoolean("retryable", retryable);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

}
