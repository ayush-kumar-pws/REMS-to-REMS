using System.Data.Common;

namespace RemsToRems.Infrastructure.Database.Abstractions
{
    /// <summary>
    /// Runs parameterized SQL on an open connection. All data access goes through this.
    /// </summary>
    internal interface IDbCommandExecutor
    {
        Task<int> ExecuteNonQueryAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters);

        Task<int> ExecuteNonQueryAsync(
            DbConnection connection,
            DbTransaction? transaction,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters);

        Task<object?> ExecuteScalarAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters);

        Task<TResult> ExecuteReaderAsync<TResult>(
            DbConnection connection,
            string commandText,
            Func<DbDataReader, Task<TResult>> map,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters);
    }
}
