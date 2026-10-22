using System.Data.Common;
using RemsToRems.Application.Abstractions.Auditing;

namespace RemsToRems.Infrastructure.Auditing
{
    /// <summary>
    /// Writes audit rows on a connection the caller already has open, so a repository
    /// can record the audit trail inside the same transaction as its data change.
    /// </summary>
    internal interface IAuditTrailWriter
    {
        Task WriteAuditTrailAsync(
            DbConnection connection,
            IReadOnlyCollection<AuditEntry> entries,
            long currentUserId,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default);

        Task WriteErrorLogAsync(
            DbConnection connection,
            ErrorLog errorLog,
            long currentUserId,
            CancellationToken cancellationToken = default);
    }
}
