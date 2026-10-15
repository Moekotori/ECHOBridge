using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Core.Protocol;

namespace Echo.Core.Database;

/// <summary>Advancing the sequence acknowledges the previous result, never its replay.</summary>
internal sealed class MutationStreamJournal<TCommand, TResult>(TransactionSql sql, JsonTypeInfo<TCommand> commandType, JsonTypeInfo<TResult> resultType, Func<string, TResult> uncommitted)
    where TCommand : class, IStreamedCommand where TResult : class
{
    private const long LifetimeMs = 24 * 60 * 60 * 1000;
    private const long MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumResultBytes = 256 * 1024;

    public TResult? Find(TCommand request, long now)
    {
        var stream = request.Stream ?? throw new ArgumentException("Mutation stream required.");
        if (!Guid.TryParseExact(stream.Id, "D", out _) || !Guid.TryParseExact(request.OperationId, "D", out _)
            || stream.Sequence < 1 || stream.Sequence > 9_007_199_254_740_991)
            throw new ArgumentException("Invalid mutation stream identity or sequence.");
        if (stream.IssuedAt <= now - LifetimeMs || stream.IssuedAt > now + 60_000)
        {
            if (request.LookupOnly) return uncommitted("expired");
            throw new ArgumentException("Mutation stream expired or issue time is invalid.");
        }
        using var command = sql.Command("SELECT issued_at,sequence,request_hash,result_json FROM core_mutation_streams WHERE stream_id=$0", stream.Id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            if (stream.Sequence != 1) throw new InvalidOperationException("Mutation stream is missing; cannot resume a later batch.");
            return request.LookupOnly ? uncommitted("notCommitted") : null;
        }
        if (reader.GetInt64(0) != stream.IssuedAt) throw new ArgumentException("Mutation stream issue time changed.");
        var sequence = reader.GetInt64(1);
        if (stream.Sequence < sequence) throw new InvalidOperationException("Mutation batch was already acknowledged; replay is forbidden.");
        if (stream.Sequence == sequence)
        {
            if (reader.GetString(2) != Hash(request)) throw new ArgumentException("Mutation sequence was already used for a different command.");
            return JsonSerializer.Deserialize(reader.GetString(3), resultType)
                ?? throw new InvalidOperationException("Invalid mutation stream receipt.");
        }
        if (stream.Sequence != sequence + 1) throw new ArgumentException("Mutation sequence must advance by one.");
        return request.LookupOnly ? uncommitted("notCommitted") : null;
    }

    public void Save(TCommand request, TResult result, long now)
    {
        var stream = request.Stream!;
        sql.Run("DELETE FROM core_mutation_streams WHERE expires_at <= $0", now);
        var json = JsonSerializer.Serialize(result, resultType);
        var bytes = Encoding.UTF8.GetByteCount(json);
        var previousBytes = sql.Value("SELECT result_bytes FROM core_mutation_streams WHERE stream_id=$0", stream.Id);
        var total = Convert.ToInt64(sql.Value("SELECT COALESCE(SUM(result_bytes),0) FROM core_mutation_streams"));
        var count = Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM core_mutation_streams"));
        // Never evict live streams: their sequence is the durable replay barrier.
        if (bytes > MaximumResultBytes || total - Convert.ToInt64(previousBytes) + bytes > MaximumBytes
            || (previousBytes is null && count >= 128))
            throw new InvalidOperationException("Mutation stream recovery budget is full.");
        sql.Run("""
            INSERT INTO core_mutation_streams VALUES($0,$1,$2,$3,$4,$5,$6)
            ON CONFLICT(stream_id) DO UPDATE SET sequence=excluded.sequence,request_hash=excluded.request_hash,
                result_json=excluded.result_json,result_bytes=excluded.result_bytes
            """, stream.Id, stream.IssuedAt, stream.IssuedAt + LifetimeMs, stream.Sequence, Hash(request), json, bytes);
    }

    private string Hash(TCommand request)
    {
        var lookup = request.LookupOnly;
        try
        {
            request.LookupOnly = false;
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, commandType)));
        }
        finally { request.LookupOnly = lookup; }
    }
}
