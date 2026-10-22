using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Infrastructure.Auditing;
using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Factories;
using RemsToRems.Infrastructure.Database.Execution;
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

            // Azure Blob configuration
            services.AddOptions<BlobConfigurationOptions>()
                .Bind(configuration.GetSection(BlobConfigurationOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                    "AzureBlobConfiguration:ConnectionString is required. Set it with User Secrets locally, " +
                    "or the AzureBlobConfiguration__ConnectionString environment variable (Codespaces secret / Azure app setting).")
                .Validate(options => !string.IsNullOrWhiteSpace(options.ContainerName),
                    "Azure Blob container name is required.");

            services.AddSingleton(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<BlobConfigurationOptions>>()
                    .Value;

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

            // Feature repositories
            services.AddScoped<IPatientLookupRepository, PatientLookupRepository>();

            return services;
        }
    }
}
