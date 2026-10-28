using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RemsToRems.Infrastructure.Database.Abstractions;

namespace RemsToRems.Infrastructure.Database.Execution
{
    internal sealed class DbCommandExecutor : IDbCommandExecutor
    {
        private readonly IDbCommandFactory _commandFactory;
        private readonly ILogger<DbCommandExecutor> _logger;

        public DbCommandExecutor(IDbCommandFactory commandFactory, ILogger<DbCommandExecutor> logger)
        {
            _commandFactory = commandFactory;
            _logger = logger;
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

            var stopwatch = Stopwatch.StartNew();
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            LogTiming(connection, commandText, stopwatch.ElapsedMilliseconds);
            return affected;
        }

        public async Task<object?> ExecuteScalarAsync(
            DbConnection connection,
            string commandText,
            CancellationToken cancellationToken = default,
            params (string Name, object? Value)[] parameters)
        {
            await using var command = CreateCommand(connection, commandText, parameters);

            var stopwatch = Stopwatch.StartNew();
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            LogTiming(connection, commandText, stopwatch.ElapsedMilliseconds);
            return value;
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

            // First-row time is mostly the server (plan compile + reading data); the rest is streaming rows.
            var stopwatch = Stopwatch.StartNew();
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            var firstResultMs = stopwatch.ElapsedMilliseconds;

            var result = await map(reader).ConfigureAwait(false);
            _logger.LogInformation(
                "Timing: [{Database}] {Command} - execute {ExecuteMs} ms, read rows {ReadMs} ms",
                connection.Database,
                Describe(commandText),
                firstResultMs,
                stopwatch.ElapsedMilliseconds - firstResultMs);
            return result;
        }

        // TEMP timing: one line per command, labelled by the first line of its SQL.
        private void LogTiming(DbConnection connection, string commandText, long elapsedMs)
        {
            _logger.LogInformation(
                "Timing: [{Database}] {Command} - {ElapsedMs} ms",
                connection.Database,
                Describe(commandText),
                elapsedMs);
        }

        private static string Describe(string commandText)
        {
            var firstLine = commandText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;
            return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "...";
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
