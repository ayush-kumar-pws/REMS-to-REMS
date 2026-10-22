using System.Data.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Application.Features.PatientLookup;
using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;

namespace RemsToRems.Infrastructure.Repositories
{
    internal sealed class PatientLookupRepository : IPatientLookupRepository
    {
        private const string UserRoleName = "REMS Patient";

        private readonly ISurveyDatabaseResolver _surveyDatabaseResolver;
        private readonly IRemsDatabaseResolver _remsDatabaseResolver;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDbCommandExecutor _commandExecutor;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration;

        public PatientLookupRepository(
            ISurveyDatabaseResolver surveyDatabaseResolver,
            IRemsDatabaseResolver remsDatabaseResolver,
            IDbConnectionFactory connectionFactory,
            IDbCommandExecutor commandExecutor,
            IMemoryCache cache,
            IOptions<BlobConfigurationOptions> options)
        {
            _surveyDatabaseResolver = surveyDatabaseResolver;
            _remsDatabaseResolver = remsDatabaseResolver;
            _connectionFactory = connectionFactory;
            _commandExecutor = commandExecutor;
            _cache = cache;
            _cacheDuration = options.Value.CacheDuration;
        }

        public async Task<IReadOnlyList<PatientMatch>> FindMatchesAsync(
            DatabaseContext database,
            PatientSearchCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(criteria);

            var roleId = await GetPatientRoleIdAsync(database.Environment, cancellationToken).ConfigureAwait(false);

            var remsDatabase = await _remsDatabaseResolver
                .ResolveAsync(database, cancellationToken)
                .ConfigureAwait(false);

            await using var remsConnection = await _connectionFactory
                .CreateOpenAsync(remsDatabase, cancellationToken)
                .ConfigureAwait(false);

            // DateOfBirth and Zip5 are not filtered yet: the REMS [User] table has no such columns.
            return await _commandExecutor.ExecuteReaderAsync(
                remsConnection,
                """
                SELECT [EmailID], [FirstName], [LastName], [CreatedBy], [CreatedDate],
                       [UpdatedBy], [UpdatedDate], [Status], [Username]
                FROM [dbo].[User]
                WHERE [UserRoleID] = @UserRoleID
                  AND LTRIM(RTRIM([FirstName])) = @FirstName
                  AND LTRIM(RTRIM([LastName])) = @LastName;
                """,
                reader => ReadMatchesAsync(reader, cancellationToken),
                cancellationToken,
                ("@UserRoleID", roleId),
                // Comparison follows the database collation (case-insensitive by default on SQL Server).
                ("@FirstName", criteria.FirstName),
                ("@LastName", criteria.LastName)).ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<PatientMatch>> ReadMatchesAsync(
            DbDataReader reader,
            CancellationToken cancellationToken)
        {
            var matches = new List<PatientMatch>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var email = reader["EmailID"] as string ?? string.Empty;
                var username = reader["Username"] as string ?? string.Empty;

                matches.Add(new PatientMatch
                {
                    UniqueId = !string.IsNullOrWhiteSpace(username) ? username : email,
                    EmailID = email,
                    NormalizedFirstName = reader["FirstName"] as string ?? string.Empty,
                    NormalizedLastName = reader["LastName"] as string ?? string.Empty,
                    Username = username,
                    CreatedBy = Convert.ToInt32(reader["CreatedBy"]),
                    CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                    UpdatedBy = Convert.ToInt32(reader["UpdatedBy"]),
                    UpdatedDate = Convert.ToDateTime(reader["UpdatedDate"]),
                    Status = Convert.ToInt32(reader["Status"]),
                    // The SQL only returns rows whose first and last names equal the request.
                    FirstNameMatch = FieldMatchResult.Exact,
                    LastNameMatch = FieldMatchResult.Exact,
                    NameMatch = FieldMatchResult.Exact
                });
            }

            return matches;
        }

        // The role ID is fixed per Survey environment; cache it to avoid a Survey round trip per lookup.
        private async Task<int> GetPatientRoleIdAsync(
            string environment,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"role-id:{environment.Trim()}:{UserRoleName}".ToUpperInvariant();
            if (_cache.TryGetValue(cacheKey, out int cachedRoleId))
            {
                return cachedRoleId;
            }

            var surveyDatabase = await _surveyDatabaseResolver
                .ResolveAsync(environment, cancellationToken)
                .ConfigureAwait(false);

            await using var surveyConnection = await _connectionFactory
                .CreateOpenAsync(surveyDatabase, cancellationToken)
                .ConfigureAwait(false);

            var roleValue = await _commandExecutor.ExecuteScalarAsync(
                surveyConnection,
                """
                SELECT TOP (1) [ID]
                FROM [dbo].[UserRole]
                WHERE [Name] = @RoleName
                  AND [Status] = 1;
                """,
                cancellationToken,
                ("@RoleName", UserRoleName)).ConfigureAwait(false);

            if (roleValue is null || roleValue is DBNull)
            {
                throw new InvalidOperationException(
                    $"The Survey UserRole '{UserRoleName}' was not found.");
            }

            var roleId = Convert.ToInt32(roleValue);
            _cache.Set(cacheKey, roleId, _cacheDuration);
            return roleId;
        }
    }
}
