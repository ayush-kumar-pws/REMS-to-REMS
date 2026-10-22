using System.Data.Common;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Abstractions
{
    internal interface IDbConnectionFactory
    {
        Task<DbConnection> CreateOpenAsync(
            ResolvedDatabase database,
            CancellationToken cancellationToken = default);
    }
}
