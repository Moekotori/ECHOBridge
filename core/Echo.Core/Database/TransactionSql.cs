using Microsoft.Data.Sqlite;
namespace Echo.Core.Database;

internal class TransactionSql : IDisposable
{
  private readonly SqliteConnection _connection;
  private readonly SqliteTransaction _transaction;
  private readonly TransactionCommandCache? _commands;

  public TransactionSql(SqliteConnection connection, SqliteTransaction transaction, bool reuseCommands = false)
  {
    _connection = connection;
    _transaction = transaction;
    if (reuseCommands) _commands = new TransactionCommandCache(Command);
  }

  public SqliteCommand Command(string sql, params object?[] values)
  {
    var command = _connection.CreateCommand();
    command.Transaction = _transaction;
    command.CommandText = sql;
    for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$" + i, values[i] ?? DBNull.Value);
    return command;
  }
  public int Run(string sql, params object?[] values)
  {
    if (_commands is not null) return _commands.Execute(sql, values, static command => command.ExecuteNonQuery());
    using var command = Command(sql, values);
    return command.ExecuteNonQuery();
  }
  public object? Value(string sql, params object?[] values)
  {
    if (_commands is not null)
    {
      var cachedResult = _commands.Execute(sql, values, static command => command.ExecuteScalar());
      return cachedResult == DBNull.Value ? null : cachedResult;
    }
    using var command = Command(sql, values);
    return command.ExecuteScalar() is { } result && result != DBNull.Value ? result : null;
  }

  public void Dispose() => _commands?.Dispose();
}
