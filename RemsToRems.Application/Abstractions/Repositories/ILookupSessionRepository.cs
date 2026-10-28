using RemsToRems.Application.Abstractions.Database;

namespace RemsToRems.Application.Abstractions.Repositories
{
    /// <summary>
    /// Lookup sessions, stored in the REMS [Session] table. The lookup creates one and returns its ID;
    /// a separate API validates it later (expired when ExpiredDate has passed, by database time).
    /// </summary>
    public interface ILookupSessionRepository
    {
        Task<LookupSession> CreateAsync(
            DatabaseContext database,
            long userId,
            int validMinutes,
            CancellationToken cancellationToken = default);
    }

    /// <param name="SessionId">[Session].[ID].</param>
    /// <param name="ExpiresAt">[Session].[ExpiredDate], in the REMS database server's time.</param>
    public sealed record LookupSession(long SessionId, DateTime ExpiresAt);
}
