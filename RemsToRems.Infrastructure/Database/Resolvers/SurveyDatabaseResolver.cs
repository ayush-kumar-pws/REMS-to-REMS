using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Resolvers
{
    internal sealed class SurveyDatabaseResolver : ISurveyDatabaseResolver
    {
        private const string RootApplication = "SURVEY";

        private readonly IBlobConfigurationProvider _blobConfigurationProvider;

        public SurveyDatabaseResolver(IBlobConfigurationProvider blobConfigurationProvider)
        {
            _blobConfigurationProvider = blobConfigurationProvider;
        }

        public async Task<ResolvedDatabase> ResolveAsync(
            string environment,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environment);

            var document = await _blobConfigurationProvider
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
