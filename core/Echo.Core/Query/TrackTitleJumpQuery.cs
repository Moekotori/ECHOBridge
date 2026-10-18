using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

/// <summary>Resolve a title position without sorting or hydrating track pages.</summary>
internal static class TrackTitleJumpQuery
{
    public static LibraryPageDto<LibraryTrackListRowDto> Execute(
        ReadOnlyLibraryDatabase database,
        CoreLibraryPageQuery query,
        NormalizedPage page,
        CoreNormalizedTrackSearch? search)
    {
        var letter = query.TitleJumpLetter!;
        if (letter.Length != 1 || letter[0] is < 'A' or > 'Z' || page.TotalHint is null)
            throw new UnsupportedQueryException("titleJumpLetter requires A–Z and totalHint.");
        if (!PersistedSortKeys.AreTrackKeysFresh(database))
            throw new UnsupportedQueryException("local_track_sort_keys are stale; writer must sync before Core can locate titles.");

        var extra = TrackPageQuery.BuildTrackPredicates(query, page, search, TrackFilterColumns.Local, includeYearAndExclude: true,
            indexedExcludes: TrackQueryIndexes.Ready(database, "exclude"));
        var hasFts = !string.IsNullOrWhiteSpace(search?.FtsMatch);
        var needsTracks = hasFts || extra.Sql.Length > 0;
        var useDuplicates = page.HideDuplicates || page.ShowDuplicatesOnly;
        var duplicateJoin = useDuplicates ? SqlFragments.DuplicateMembersJoin("sort_keys.track_id") : string.Empty;
        var duplicateFilter = page.ShowDuplicatesOnly ? "duplicate_members.track_id IS NOT NULL"
            : page.HideDuplicates ? "COALESCE(duplicate_members.hidden, 0) = 0" : string.Empty;
        var where = SqlFragments.And(
            needsTracks
                ? "WHERE tracks.missing = 0 AND tracks.library_hidden = 0 AND sort_keys.title_key COLLATE NOCASE < ?"
                : "WHERE sort_keys.title_key COLLATE NOCASE < ? AND sort_keys.track_id NOT IN (SELECT id FROM tracks WHERE library_hidden = 1)",
            string.Join(" AND ", new[] { duplicateFilter, hasFts ? "tracks_fts MATCH ?" : string.Empty, extra.Sql }.Where(static part => part.Length > 0)));
        var sql = $"""
            SELECT COUNT(*)
            FROM local_track_sort_keys AS sort_keys INDEXED BY idx_local_track_sort_keys_title
            {(needsTracks ? "INNER JOIN tracks ON tracks.id = sort_keys.track_id" : string.Empty)}
            {duplicateJoin}
            {(hasFts ? "INNER JOIN tracks_fts ON tracks_fts.rowid = tracks.rowid" : string.Empty)}
            {where}
            """;
        var before = Convert.ToInt64(CachedQuery.Scalar(database, sql, command =>
        {
            if (useDuplicates) command.Add(page.DuplicateMode);
            command.Add(letter.ToLowerInvariant());
            if (hasFts) command.Add(search!.FtsMatch);
            foreach (var value in extra.Parameters) command.Add(value);
        }));
        return new()
        {
            Page = page.Page,
            PageSize = page.PageSize,
            Total = page.TotalHint.Value,
            HasMore = false,
            TitleJumpOffset = Math.Min(before, Math.Max(0, page.TotalHint.Value - 1)),
        };
    }
}
