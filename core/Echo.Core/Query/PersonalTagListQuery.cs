using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PersonalTagListQuery
{
    public static List<CorePersonalTagDto> Execute(ReadOnlyLibraryDatabase database, CorePersonalTagTarget? target)
    {
        if (target is not null && (target.Kind is not ("track" or "album" or "artist")
            || string.IsNullOrWhiteSpace(target.Id) || target.Id.Length > 512))
            throw new UnsupportedQueryException("Invalid personal tag target.");

        const string sql = """
            SELECT personal_tags.id, personal_tags.name,
              (SELECT COUNT(*) FROM personal_tag_items WHERE tag_id = personal_tags.id),
              EXISTS (SELECT 1 FROM personal_tag_items
                WHERE tag_id = personal_tags.id AND kind = ? AND target_id = ?)
            FROM personal_tags
            ORDER BY name COLLATE NOCASE, id
            LIMIT 200
            """;
        var tags = new List<CorePersonalTagDto>();
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(target?.Kind ?? string.Empty);
            command.Add(target?.Id ?? string.Empty);
        });
        while (reader.Read())
        {
            tags.Add(new CorePersonalTagDto
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                ItemCount = reader.GetInt32(2),
                Assigned = target is null ? null : reader.GetInt32(3) != 0,
            });
        }
        return tags;
    }
}
