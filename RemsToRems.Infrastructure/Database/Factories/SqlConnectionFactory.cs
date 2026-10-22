using System.Data.Common;
using Microsoft.Data.SqlClient;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Factories
{
    internal sealed class SqlConnectionFactory : IDbConnectionFactory
    {
        public async Task<DbConnection> CreateOpenAsync(
            ResolvedDatabase database,
            CancellationToken cancellationToken = default)
        {
            var connection = Create(database);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static DbConnection Create(ResolvedDatabase database)
        {
            ArgumentNullException.ThrowIfNull(database);

            if (string.IsNullOrWhiteSpace(database.ConnectionString))
            {
                throw new ArgumentException("A database connection string is required.", nameof(database));
            }

            var builder = new SqlConnectionStringBuilder(database.ConnectionString)
            {
                TrustServerCertificate = true
            };

            return new SqlConnection(builder.ConnectionString);
        }
    }
}
