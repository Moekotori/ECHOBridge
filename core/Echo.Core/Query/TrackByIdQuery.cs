using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Query;

public static class TrackByIdQuery
{
    public static List<LibraryTrackDto> Execute(ReadOnlyLibraryDatabase database, IReadOnlyList<string>? trackIds)
    {
        var ids = Normalize(trackIds);
        return TrackPageQuery.HydrateTracks(database, ids, list: false)
            .ConvertAll(static track => (LibraryTrackDto)track);
    }

    private static List<string> Normalize(IReadOnlyList<string>? trackIds)
    {
        if (trackIds is null || trackIds.Count == 0)
        {
            return [];
        }
        if (trackIds.Count > ProtocolConstants.MaxTrackIds)
            throw new ProtocolLimitException("Track hydration request exceeds the ID limit; split into bounded batches.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var value in trackIds)
        {
            var id = value?.Trim();
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                continue;
            }

            ids.Add(id);
            if (ids.Count >= ProtocolConstants.MaxTrackIds)
            {
                break;
            }
        }

        return ids;
    }
}
