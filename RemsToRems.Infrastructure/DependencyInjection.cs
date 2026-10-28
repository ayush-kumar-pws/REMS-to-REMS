using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Application.Features.PatientLookup;
using RemsToRems.Infrastructure.Auditing;
using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Factories;
using RemsToRems.Infrastructure.Database.Execution;
using RemsToRems.Infrastructure.Database.Models;
using RemsToRems.Infrastructure.Database.Resolvers;
using RemsToRems.Infrastructure.Repositories;

namespace RemsToRems.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddMemoryCache();

            // Direct database connection strings (optional). When set, Azure Blob is not used for that database.
            services.AddOptions<DatabaseConnectionOptions>()
                .Bind(configuration.GetSection(DatabaseConnectionOptions.SectionName));

            // Azure Blob configuration
            services.AddOptions<BlobConfigurationOptions>()
                .Bind(configuration.GetSection(BlobConfigurationOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.ContainerName),
                    "Azure Blob container name is required.");

            // The connection string is checked here, when the Blob client is first needed,
            // so it is not required when ConnectionStrings:SurveyDb is set.
            services.AddSingleton(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<BlobConfigurationOptions>>()
                    .Value;

                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new InvalidOperationException(
                        "AzureBlobConfiguration:ConnectionString is required (or set ConnectionStrings:SurveyDb). " +
                        "Set it with User Secrets locally, or the AzureBlobConfiguration__ConnectionString environment " +
                        "variable (Codespaces secret / Azure app setting).");
                }

                return new BlobServiceClient(options.ConnectionString);
            });
            services.AddSingleton<IBlobConfigurationProvider, BlobConfigurationProvider>();

            // Database access
            services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
            services.AddSingleton<IDbCommandFactory, SqlCommandFactory>();
            services.AddSingleton<IDbCommandExecutor, DbCommandExecutor>();
            services.AddScoped<ISurveyDatabaseResolver, SurveyDatabaseResolver>();
            services.AddScoped<IRemsDatabaseResolver, RemsDatabaseResolver>();

            // Auditing
            services.AddSingleton<IAuditTrailWriter, SqlAuditTrailWriter>();
            services.AddScoped<IAuditTrailService, SqlAuditTrailService>();

            // Which Survey/REMS database the API works against (no longer sent by the caller).
            services.AddOptions<DatabaseContextOptions>()
                .Bind(configuration.GetSection(DatabaseContextOptions.SectionName));

            // Patient match settings (question names, trigger statuses), read by the repository and the service.
            services.AddOptions<PatientMatchOptions>()
                .Bind(configuration.GetSection(PatientMatchOptions.SectionName));

            // Feature repositories
            services.AddScoped<IPatientLookupRepository, PatientLookupRepository>();
            services.AddScoped<ILookupSessionRepository, LookupSessionRepository>();

            return services;
        }
    }
}
