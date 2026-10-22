using System.Data.Common;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Infrastructure.Database.Abstractions;

namespace RemsToRems.Infrastructure.Auditing
{
    internal sealed class SqlAuditTrailWriter : IAuditTrailWriter
    {
        // SurveyApi records unauthenticated/system actions as user 1.
        private const long SystemUserId = 1;
        private const int ActiveStatus = 1;

        private readonly IDbCommandExecutor _commandExecutor;

        public SqlAuditTrailWriter(IDbCommandExecutor commandExecutor)
        {
            _commandExecutor = commandExecutor;
        }

        public async Task WriteAuditTrailAsync(
            DbConnection connection,
            IReadOnlyCollection<AuditEntry> entries,
            long currentUserId,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(entries);

            var userId = ResolveUserId(currentUserId);
            // SurveyApi stamps audit rows with the app server's local time; keep that so both apps' rows line up.
            var now = DateTime.Now;

            foreach (var entry in entries)
            {
                await _commandExecutor.ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO [dbo].[AuditTrail]
                        ([RecordID], [DBTableName], [DBFieldName], [TransactionType],
                         [PreviousValue], [CurrentValue],
                         [CreatedBy], [CreatedDate], [UpdatedBy], [UpdatedDate], [Status])
                    VALUES
                        (@RecordID, @DBTableName, @DBFieldName, @TransactionType,
                         @PreviousValue, @CurrentValue,
                         @CreatedBy, @CreatedDate, @UpdatedBy, @UpdatedDate, @Status);
                    """,
                    cancellationToken,
                    ("@RecordID", entry.RecordId),
                    ("@DBTableName", entry.DbTableName),
                    ("@DBFieldName", entry.DbFieldName ?? string.Empty),
                    ("@TransactionType", entry.TransactionType),
                    ("@PreviousValue", entry.PreviousValue ?? string.Empty),
                    ("@CurrentValue", entry.CurrentValue ?? string.Empty),
                    ("@CreatedBy", userId),
                    ("@CreatedDate", now),
                    ("@UpdatedBy", userId),
                    ("@UpdatedDate", now),
                    ("@Status", ActiveStatus)).ConfigureAwait(false);
            }
        }

        public async Task WriteErrorLogAsync(
            DbConnection connection,
            ErrorLog errorLog,
            long currentUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(errorLog);

            var userId = ResolveUserId(currentUserId);
            var now = DateTime.Now;

            await _commandExecutor.ExecuteNonQueryAsync(
                connection,
                """
                INSERT INTO [dbo].[ErrorLog]
                    ([OriginURL], [ControllerName], [MethodName], [RequestJSON], [ErrorJSON],
                     [CreatedBy], [CreatedDate], [UpdatedBy], [UpdatedDate], [Status])
                VALUES
                    (@OriginURL, @ControllerName, @MethodName, @RequestJSON, @ErrorJSON,
                     @CreatedBy, @CreatedDate, @UpdatedBy, @UpdatedDate, @Status);
                """,
                cancellationToken,
                ("@OriginURL", errorLog.OriginUrl),
                ("@ControllerName", errorLog.ControllerName),
                ("@MethodName", errorLog.MethodName),
                ("@RequestJSON", errorLog.RequestJson),
                ("@ErrorJSON", errorLog.ErrorJson),
                ("@CreatedBy", userId),
                ("@CreatedDate", now),
                ("@UpdatedBy", userId),
                ("@UpdatedDate", now),
                ("@Status", ActiveStatus)).ConfigureAwait(false);
        }

        private static long ResolveUserId(long currentUserId)
        {
            return currentUserId == 0 ? SystemUserId : currentUserId;
        }
    }
}
