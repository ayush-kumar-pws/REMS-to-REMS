using Microsoft.Extensions.Logging;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Auditing
{
    internal sealed class SqlAuditTrailService : IAuditTrailService
    {
        private readonly IRemsDatabaseResolver _remsDatabaseResolver;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuditTrailWriter _writer;
        private readonly ILogger<SqlAuditTrailService> _logger;

        public SqlAuditTrailService(
            IRemsDatabaseResolver remsDatabaseResolver,
            IDbConnectionFactory connectionFactory,
            IAuditTrailWriter writer,
            ILogger<SqlAuditTrailService> logger)
        {
            _remsDatabaseResolver = remsDatabaseResolver;
            _connectionFactory = connectionFactory;
            _writer = writer;
            _logger = logger;
        }

        public async Task AddAuditTrailAsync(
            DatabaseContext context,
            IReadOnlyCollection<AuditEntry> entries,
            long currentUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
            {
                return;
            }

            var remsDatabase = await _remsDatabaseResolver
                .ResolveAsync(context, cancellationToken)
                .ConfigureAwait(false);

            await using var connection = await _connectionFactory
                .CreateOpenAsync(remsDatabase, cancellationToken)
                .ConfigureAwait(false);

            // A single INSERT is already atomic; BEGIN/COMMIT would only add two round trips.
            if (entries.Count == 1)
            {
                await _writer
                    .WriteAuditTrailAsync(connection, entries, currentUserId, null, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await _writer
                .WriteAuditTrailAsync(connection, entries, currentUserId, transaction, cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task InsertErrorLogAsync(
            DatabaseContext? context,
            ErrorLog errorLog,
            long currentUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(errorLog);

            try
            {
                var database = await ResolveErrorLogDatabaseAsync(context, cancellationToken).ConfigureAwait(false);
                if (database is null)
                {
                    _logger.LogWarning(
                        "Error log for {ControllerName}.{MethodName} was not written: no REMS database context.",
                        errorLog.ControllerName,
                        errorLog.MethodName);
                    return;
                }

                await using var connection = await _connectionFactory
                    .CreateOpenAsync(database, cancellationToken)
                    .ConfigureAwait(false);

                await _writer
                    .WriteErrorLogAsync(connection, errorLog, currentUserId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Writing the error log must never mask the original failure.
                _logger.LogError(
                    ex,
                    "Failed to write error log for {ControllerName}.{MethodName}.",
                    errorLog.ControllerName,
                    errorLog.MethodName);
            }
        }

        // Audit data is written to the organization's REMS database only, never the root Survey database.
        private async Task<ResolvedDatabase?> ResolveErrorLogDatabaseAsync(
            DatabaseContext? context,
            CancellationToken cancellationToken)
        {
            if (context is null
                || string.IsNullOrWhiteSpace(context.Environment)
                || string.IsNullOrWhiteSpace(context.Organization))
            {
                return null;
            }

            return await _remsDatabaseResolver
                .ResolveAsync(context, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
