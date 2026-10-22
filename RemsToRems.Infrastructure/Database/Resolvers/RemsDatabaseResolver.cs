using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Common.Exceptions;
using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Resolvers
{
    internal sealed class RemsDatabaseResolver : IRemsDatabaseResolver
    {
        private readonly ISurveyDatabaseResolver _surveyDatabaseResolver;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDbCommandExecutor _commandExecutor;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration;

        public RemsDatabaseResolver(
            ISurveyDatabaseResolver surveyDatabaseResolver,
            IDbConnectionFactory connectionFactory,
            IDbCommandExecutor commandExecutor,
            IMemoryCache cache,
            IOptions<BlobConfigurationOptions> options)
        {
            _surveyDatabaseResolver = surveyDatabaseResolver;
            _connectionFactory = connectionFactory;
            _commandExecutor = commandExecutor;
            _cache = cache;
            _cacheDuration = options.Value.CacheDuration;
        }

        public async Task<ResolvedDatabase> ResolveAsync(
            DatabaseContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The Client -> REMS catalog mapping rarely changes; cache it so each request
            // does not pay a Survey database round trip (the lookup and its audit both resolve it).
            var cacheKey = $"rems-db:{context.Environment.Trim()}:{context.Organization.Trim()}"
                .ToUpperInvariant();
            if (_cache.TryGetValue(cacheKey, out ResolvedDatabase? cached) && cached is not null)
            {
                return cached;
            }

            var resolved = await ResolveFromSurveyAsync(context, cancellationToken).ConfigureAwait(false);
            _cache.Set(cacheKey, resolved, _cacheDuration);
            return resolved;
        }

        private async Task<ResolvedDatabase> ResolveFromSurveyAsync(
            DatabaseContext context,
            CancellationToken cancellationToken)
        {
            var surveyDatabase = await _surveyDatabaseResolver
                .ResolveAsync(context.Environment, cancellationToken)
                .ConfigureAwait(false);

            await using var connection = await _connectionFactory
                .CreateOpenAsync(surveyDatabase, cancellationToken)
                .ConfigureAwait(false);

            var clientOrganization = await _commandExecutor.ExecuteScalarAsync(
                connection,
                "SELECT TOP (1) [Organization] FROM [Client] WHERE [Name] = @Organization;",
                cancellationToken,
                ("@Organization", context.Organization.Trim())).ConfigureAwait(false);

            if (clientOrganization is null || clientOrganization is DBNull)
            {
                throw new NotFoundException(
                    $"No Survey client was found for organization '{context.Organization}'.");
            }

            var targetCatalog = Convert.ToString(clientOrganization)?.Trim();
            if (string.IsNullOrWhiteSpace(targetCatalog))
            {
                throw new InvalidOperationException("The Survey client has no REMS organization catalog.");
            }

            var builder = new SqlConnectionStringBuilder(surveyDatabase.ConnectionString)
            {
                InitialCatalog = targetCatalog
            };

            return new ResolvedDatabase(targetCatalog, context.Environment, builder.ConnectionString);
        }
    }
}
