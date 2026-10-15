using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Core.Protocol;

using System.Text.Json.Serialization.Metadata;
namespace Echo.Core.Database;

internal sealed class CommandReceiptJournal<TCommand, TResult>(TransactionSql sql, JsonTypeInfo<TCommand> commandType, JsonTypeInfo<TResult> resultType, Func<string, TResult> uncommitted, bool libraryMutation = false)
    where TCommand : class, IRecoverableCommand where TResult : class
{
  private const long LifetimeMs = 24 * 60 * 60 * 1000;
  private const long MaximumBytes = 16 * 1024 * 1024;

  public TResult? Find(TCommand request, long now)
  {
    if (!Guid.TryParseExact(request.OperationId, "D", out _)) throw new ArgumentException("A UUID operation ID is required.");
    if (request.IssuedAt <= now - LifetimeMs || request.IssuedAt > now + 60_000)
    {
      if (request.LookupOnly) return uncommitted("expired");
      throw new ArgumentException("Operation recovery window expired or issue time is invalid. Refresh before a new operation.");
    }
    using var command = sql.Command("SELECT request_hash,result_json FROM core_command_journal WHERE operation_id=$0", request.OperationId);
    using var reader = command.ExecuteReader();
    if (!reader.Read()) return request.LookupOnly ? uncommitted("notCommitted") : null;
    if (reader.GetString(0) != Hash(request))
      throw new ArgumentException("Operation ID was already used for a different command.");
    return JsonSerializer.Deserialize(reader.GetString(1), resultType)
        ?? throw new InvalidOperationException("Invalid command journal result.");
  }

  public void Save(TCommand request, TResult result, long now)
  {
    sql.Run("DELETE FROM core_command_journal WHERE expires_at <= $0", now);
    var json = JsonSerializer.Serialize(result, resultType);
    var bytes = Encoding.UTF8.GetByteCount(json);
    // Legacy mutation results always contain `removed`; playlist results never do.
    // Classify existing receipts without deleting/moving them or changing their hashes.
    // Each class retains its own 4096-row / 16 MiB bound (32 MiB combined).
    var scope = libraryMutation ? "json_type(result_json,'$.removed') IS NOT NULL" : "json_type(result_json,'$.removed') IS NULL";
    var total = Convert.ToInt64(sql.Value($"SELECT COALESCE(SUM(result_bytes),0) FROM core_command_journal WHERE {scope}"));
    var count = Convert.ToInt64(sql.Value($"SELECT COUNT(*) FROM core_command_journal WHERE {scope}"));
    // Never evict an unexpired receipt: doing so could execute a retry twice.
    // Exceeding the budget aborts this transaction, including its business write.
    if (count >= 4096 || total + bytes > MaximumBytes)
      throw new InvalidOperationException("Command recovery journal is full. Retry the same operation later.");
    sql.Run("INSERT INTO core_command_journal VALUES ($0,$1,$2,$3,$4)",
        request.OperationId, Hash(request), request.IssuedAt + LifetimeMs, json, bytes);
  }

  private string Hash(TCommand request)
  {
    // Lookup and execution must identify exactly the same command payload.
    var lookup = request.LookupOnly;
    try
    {
      request.LookupOnly = false;
      return Convert.ToHexString(SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(request, commandType)));
    }
    finally { request.LookupOnly = lookup; }
  }
}
