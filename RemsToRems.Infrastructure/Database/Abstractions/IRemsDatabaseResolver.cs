using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Abstractions
{
    /// <summary>
    /// Resolves an organization's REMS database (Survey Client.Organization is the catalog name).
    /// </summary>
    internal interface IRemsDatabaseResolver
    {
        Task<ResolvedDatabase> ResolveAsync(
            DatabaseContext context,
            CancellationToken cancellationToken = default);
    }
}
