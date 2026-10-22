using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Abstractions
{
    /// <summary>
    /// Resolves the root Survey database for an environment from the Azure Blob configuration.
    /// </summary>
    internal interface ISurveyDatabaseResolver
    {
        Task<ResolvedDatabase> ResolveAsync(
            string environment,
            CancellationToken cancellationToken = default);
    }
}
