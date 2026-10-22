using System.Data.Common;
using RemsToRems.Infrastructure.Database.Abstractions;

namespace RemsToRems.Infrastructure.Database.Execution
{
    internal sealed class DbCommandExecutor : IDbCommandExecutor
    {
        private readonly IDbCommandFactory _commandFactory;

        public DbCommandExecutor(IDbCommandFactory commandFactory)
        {
            _commandFactory = commandFactory;
        }

        public Task<int> ExecuteNonQueryAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters)
        {
            return ExecuteNonQueryAsync(connection, null, commandText, cancellationToken, parameters);
        }

        public async Task<int> ExecuteNonQueryAsync(
            DbConnection connection,
            DbTransaction? transaction,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters)
        {
            if (transaction is not null && transaction.Connection != connection)
            {
                throw new ArgumentException(
                    "The transaction must belong to the supplied connection.",
                    nameof(transaction));
            }

            await using var command = CreateCommand(connection, commandText, parameters);
            command.Transaction = transaction;
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<object?> ExecuteScalarAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters)
        {
            await using var command = CreateCommand(connection, commandText, parameters);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<TResult> ExecuteReaderAsync<TResult>(
            DbConnection connection,
            string commandText,
            Func<DbDataReader, Task<TResult>> map,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters)
        {
            ArgumentNullException.ThrowIfNull(map);

            await using var command = CreateCommand(connection, commandText, parameters);
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            return await map(reader).ConfigureAwait(false);
        }

        private DbCommand CreateCommand(
            DbConnection connection,
            string commandText,
            (string Name, object? Value)[] parameters)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

            return _commandFactory.Create(connection, commandText, parameters);
        }
    }
}
