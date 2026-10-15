using Echo.Core.Database;
using Echo.Core.Maintenance;
using Echo.Core.Protocol;

namespace Echo.Core.Personal;

public static class PersonalTagCommands
{
    public static PersonalTagCommandResult Execute(string databasePath, PersonalTagCommand request)
    {
        using var connection = SortKeyMaintenanceSql.OpenWriter(databasePath);
        using var transaction = connection.BeginTransaction();
        var sql = new TransactionSql(connection, transaction);
        var journal = new CommandReceiptJournal<PersonalTagCommand, PersonalTagCommandResult>(sql,
            CoreJsonContext.Default.PersonalTagCommand, CoreJsonContext.Default.PersonalTagCommandResult,
            state => new PersonalTagCommandResult(state));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var previous = journal.Find(request, now);
        if (previous is not null) return previous;

        var tagId = request.Action switch
        {
            "create" => Create(sql, request),
            "rename" => Rename(sql, request),
            "delete" => Delete(sql, request),
            "assign" => Assign(sql, request),
            _ => throw new ArgumentException("Invalid tag action."),
        };
        var result = new PersonalTagCommandResult("committed", tagId);
        journal.Save(request, result, now);
        transaction.Commit();
        return result;
    }

    private static string Create(TransactionSql sql, PersonalTagCommand request)
    {
        var (name, key) = Name(request);
        if (Convert.ToInt32(sql.Value("SELECT COUNT(*) FROM personal_tags") ?? 0) >= 200)
            throw new InvalidOperationException("Personal tag limit reached");
        var id = Guid.NewGuid().ToString();
        sql.Run("INSERT INTO personal_tags VALUES ($0, $1, $2)", id, name, key);
        return id;
    }

    private static string Rename(TransactionSql sql, PersonalTagCommand request)
    {
        var id = Id(request.Id);
        var (name, key) = Name(request);
        if (sql.Run("UPDATE personal_tags SET name = $0, name_key = $1 WHERE id = $2", name, key, id) == 0)
            throw new InvalidOperationException("Tag not found");
        return id;
    }

    private static string Delete(TransactionSql sql, PersonalTagCommand request)
    {
        var id = Id(request.Id);
        sql.Run("DELETE FROM personal_tags WHERE id = $0", id);
        return id;
    }

    private static string Assign(TransactionSql sql, PersonalTagCommand request)
    {
        var id = Id(request.Id);
        var target = request.Target ?? throw new ArgumentException("Invalid tag target");
        if (target.Kind is not ("track" or "album" or "artist")) throw new ArgumentException("Invalid tag target kind");
        var targetId = Id(target.Id);
        if (request.Assigned is null) throw new ArgumentException("Invalid tag assignment");
        if (request.Assigned == false)
        {
            sql.Run("DELETE FROM personal_tag_items WHERE tag_id = $0 AND kind = $1 AND target_id = $2", id, target.Kind, targetId);
            return id;
        }
        var snapshot = request.Snapshot ?? throw new ArgumentException("Library item unavailable");
        if (snapshot.Title.Length > 512 || snapshot.Subtitle.Length > 512) throw new ArgumentException("Tag snapshot exceeds the limit");
        sql.Run("""
            INSERT INTO personal_tag_items VALUES ($0, $1, $2, $3, $4)
            ON CONFLICT(tag_id, kind, target_id) DO UPDATE SET title = excluded.title, subtitle = excluded.subtitle
            """, id, target.Kind, targetId, snapshot.Title, snapshot.Subtitle);
        return id;
    }

    private static string Id(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512
        ? id.Trim() : throw new ArgumentException("Invalid album curation id");

    private static (string Name, string Key) Name(PersonalTagCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80
            || request.Name != request.Name.Trim() || string.IsNullOrEmpty(request.NameKey) || request.NameKey.Length > 4096)
            throw new ArgumentException("Invalid tag name");
        return (request.Name, request.NameKey);
    }
}
