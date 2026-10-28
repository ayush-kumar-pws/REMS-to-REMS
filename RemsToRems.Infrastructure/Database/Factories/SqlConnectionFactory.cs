using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;

namespace RemsToRems.Infrastructure.Database.Factories
{
    internal sealed class SqlConnectionFactory : IDbConnectionFactory
    {
        private readonly ILogger<SqlConnectionFactory> _logger;

        public SqlConnectionFactory(ILogger<SqlConnectionFactory> logger)
        {
            _logger = logger;
        }

        public async Task<DbConnection> CreateOpenAsync(
            ResolvedDatabase database,
            CancellationToken cancellationToken = default)
        {
            var connection = Create(database);
            try
            {
                // TEMP timing: a new physical connection takes 100s of ms or more; a pooled one ~0 ms.
                var stopwatch = Stopwatch.StartNew();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Timing: opened connection to {DataSource}/{Database} in {ElapsedMs} ms",
                    connection.DataSource,
                    connection.Database,
                    stopwatch.ElapsedMilliseconds);
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
