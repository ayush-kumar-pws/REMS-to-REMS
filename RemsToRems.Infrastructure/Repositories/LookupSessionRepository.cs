using System.Data.Common;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Infrastructure.Database.Abstractions;

namespace RemsToRems.Infrastructure.Repositories
{
    /// <summary>
    /// Writes lookup sessions to the REMS [Session] table (the same table SurveyApi uses for REMS sessions).
    /// ExpiredDate uses the database clock (GETDATE()), as SurveyApi's GetREMSSessionID check does.
    /// </summary>
    internal sealed class LookupSessionRepository : ILookupSessionRepository
    {
        private const int ActiveStatus = 1;

        private readonly IRemsDatabaseResolver _remsDatabaseResolver;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDbCommandExecutor _commandExecutor;

        public LookupSessionRepository(
            IRemsDatabaseResolver remsDatabaseResolver,
            IDbConnectionFactory connectionFactory,
            IDbCommandExecutor commandExecutor)
        {
            _remsDatabaseResolver = remsDatabaseResolver;
            _connectionFactory = connectionFactory;
            _commandExecutor = commandExecutor;
        }

        public async Task<LookupSession> CreateAsync(
            DatabaseContext database,
            long userId,
            int validMinutes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(validMinutes);

            var remsDatabase = await _remsDatabaseResolver
                .ResolveAsync(database, cancellationToken)
                .ConfigureAwait(false);

            await using var connection = await _connectionFactory
                .CreateOpenAsync(remsDatabase, cancellationToken)
                .ConfigureAwait(false);

            return await _commandExecutor.ExecuteReaderAsync(
                connection,
                """
                INSERT INTO [dbo].[Session]
                    ([UserID], [ExpiredDate], [CreatedBy], [CreatedDate], [UpdatedBy], [UpdatedDate], [Status])
                OUTPUT INSERTED.[ID], INSERTED.[ExpiredDate]
                VALUES
                    (@UserID, DATEADD(minute, @ValidMinutes, GETDATE()), @UserID, GETDATE(), @UserID, GETDATE(), @Status);
                """,
                reader => ReadSessionAsync(reader, cancellationToken),
                cancellationToken,
                ("@UserID", userId),
                ("@ValidMinutes", validMinutes),
                ("@Status", ActiveStatus)).ConfigureAwait(false);
        }

        private static async Task<LookupSession> ReadSessionAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The REMS Session row was not created.");
            }

            return new LookupSession(Convert.ToInt64(reader["ID"]), Convert.ToDateTime(reader["ExpiredDate"]));
        }
    }
}
