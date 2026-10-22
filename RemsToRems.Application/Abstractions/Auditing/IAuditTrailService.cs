using RemsToRems.Application.Abstractions.Database;

namespace RemsToRems.Application.Abstractions.Auditing;

public interface IAuditTrailService
{
    /// <summary>
    /// Writes the entries to [AuditTrail] in the organization's REMS database.
    /// A <paramref name="currentUserId"/> of 0 is recorded as 1 (system), matching SurveyApi.
    /// </summary>
    Task AddAuditTrailAsync(
        DatabaseContext context,
        IReadOnlyCollection<AuditEntry> entries,
        long currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes to [ErrorLog] in the organization's REMS database. Never writes to the root
    /// Survey database: when no REMS database can be resolved, the error is only sent to the
    /// application logger. Never throws; failures are logged.
    /// </summary>
    Task InsertErrorLogAsync(
        DatabaseContext? context,
        ErrorLog errorLog,
        long currentUserId,
        CancellationToken cancellationToken = default);
}
