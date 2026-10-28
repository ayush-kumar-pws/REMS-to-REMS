using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Resolvers
{
    internal sealed class SurveyDatabaseResolver : ISurveyDatabaseResolver
    {
        private const string RootApplication = "SURVEY";

        private readonly IServiceProvider _serviceProvider;
        private readonly string? _directConnectionString;

        // IServiceProvider, not IBlobConfigurationProvider: the Blob client is only created when
        // SurveyDb is not set, so no Blob connection string is needed when it is.
        public SurveyDatabaseResolver(IServiceProvider serviceProvider, IOptions<DatabaseConnectionOptions> connections)
        {
            _serviceProvider = serviceProvider;
            _directConnectionString = connections.Value.SurveyDb;
        }

        public async Task<ResolvedDatabase> ResolveAsync(
            string environment,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environment);

            // ConnectionStrings:SurveyDb set: use it directly, no Azure Blob read.
            if (!string.IsNullOrWhiteSpace(_directConnectionString))
            {
                return new ResolvedDatabase(RootApplication, environment, _directConnectionString);
            }

            // Resolved only when needed, so the Blob connection string is not required when SurveyDb is set.
            var blobConfigurationProvider = _serviceProvider.GetRequiredService<IBlobConfigurationProvider>();
            var document = await blobConfigurationProvider
                .ReadAsync(RootApplication, environment, cancellationToken)
                .ConfigureAwait(false);

            var expectedApplicationName = $"{RootApplication}-{environment.Trim()}";
            var entry = document.Config?.Database.FirstOrDefault(item =>
                string.Equals(item.ApplicationName?.Trim(), expectedApplicationName, StringComparison.OrdinalIgnoreCase));
            if (entry is null || string.IsNullOrWhiteSpace(entry.DatabaseConnectionString))
            {
                throw new InvalidOperationException(
                    $"No database connection is configured for '{expectedApplicationName}'.");
            }

            return new ResolvedDatabase(RootApplication, environment, entry.DatabaseConnectionString);
        }
    }
}
