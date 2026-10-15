using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Personal;

public static class PersonalTagItemsQuery
{
    public static CorePersonalTagItemsPage Execute(ReadOnlyLibraryDatabase database, string? tagId, int? requestedPage)
    {
        if (string.IsNullOrWhiteSpace(tagId) || tagId.Length > 512) throw new ArgumentException("Invalid album curation id");
        var id = tagId.Trim();
        var page = requestedPage ?? 1;
        if (page < 1 || page > 1_000_000) throw new ArgumentException("Invalid page");
        using var transaction = database.BeginDeferredRead();
        var total = Convert.ToInt32(CachedQuery.Scalar(database,
            "SELECT COUNT(*) FROM personal_tag_items WHERE tag_id = ?", command => command.Add(id)) ?? 0);
        const string sql = """
            SELECT kind, target_id, title, subtitle
            FROM personal_tag_items
            WHERE tag_id = ?
            ORDER BY kind, title COLLATE NOCASE, target_id
            LIMIT 30 OFFSET ?
            """;
        var items = new List<CorePersonalTagItemDto>(30);
        using (var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(id);
            command.Add((long)(page - 1) * 30);
        }))
        {
            while (reader.Read()) items.Add(new CorePersonalTagItemDto
            {
                Kind = reader.GetString(0), Id = reader.GetString(1),
                Title = reader.GetString(2), Subtitle = reader.GetString(3),
            });
        }
        return new CorePersonalTagItemsPage { Items = items, Page = page, Total = total, HasMore = (long)page * 30 < total };
    }
}
