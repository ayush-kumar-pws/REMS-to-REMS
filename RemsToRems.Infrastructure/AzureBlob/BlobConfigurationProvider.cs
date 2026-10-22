using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Common.Exceptions;
using System.Text.Json;

namespace RemsToRems.Infrastructure.AzureBlob
{
    internal sealed class BlobConfigurationProvider : IBlobConfigurationProvider
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobConfigurationOptions _options;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);

        // One entry per blob (i.e. per environment), so requests for different environments never share a document.
        private readonly Dictionary<string, CachedDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

        public BlobConfigurationProvider(
            BlobServiceClient blobServiceClient,
            IOptions<BlobConfigurationOptions> options)
        {
            _blobServiceClient = blobServiceClient
                ?? throw new ArgumentNullException(nameof(blobServiceClient));
            _options = options?.Value
                ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<BlobConfigurationDocument> ReadAsync(
            string applicationName,
            string environment,
            CancellationToken cancellationToken = default)
        {
            var app = Required(applicationName, nameof(applicationName));
            var env = Required(environment, nameof(environment));
            var container = Required(_options.ContainerName, nameof(_options.ContainerName));
            var pattern = Required(_options.BlobNamePattern, nameof(_options.BlobNamePattern));

            var blobName = pattern.Replace(
                "{environment}", env, StringComparison.OrdinalIgnoreCase);

            if (blobName.Contains('{') || blobName.Contains('}'))
                throw new InvalidOperationException(
                    "The Blob name pattern contains an unsupported placeholder.");

            try
            {
                var document = await GetDocumentAsync(
                    container, blobName, cancellationToken).ConfigureAwait(false);

                if (document.Config?.Database is null)
                    throw new InvalidOperationException(
                        "Blob configuration does not contain a valid config database.");

                var expected = $"{app}-{env}";

                if (!document.Config.Database.Exists(entry =>
                    string.Equals(entry.ApplicationName?.Trim(),
                        expected, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Blob configuration was not found for application '{app}' " +
                        $"and environment '{env}'.");
                }

                return document;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                throw new NotFoundException(
                    $"Blob configuration was not found for environment '{env}'.", ex);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "Blob configuration contains invalid JSON.", ex);
            }
        }

        private async Task<BlobConfigurationDocument> GetDocumentAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"{containerName}/{blobName}";

            await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_documents.TryGetValue(cacheKey, out var cached)
                    && cached.ExpiresAt > DateTimeOffset.UtcNow)
                    return cached.Document;

                var blob = _blobServiceClient
                    .GetBlobContainerClient(containerName)
                    .GetBlobClient(blobName);

                var result = await blob.DownloadContentAsync(cancellationToken)
                    .ConfigureAwait(false);

                var document = result.Value.Content
                    .ToObjectFromJson<BlobConfigurationDocument>();

                if (document?.Config is null)
                    throw new InvalidOperationException(
                        "Blob configuration does not contain a config object.");

                _documents[cacheKey] = new CachedDocument(
                    document, DateTimeOffset.UtcNow.Add(_options.CacheDuration));
                return document;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private static string Required(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    $"Azure Blob setting '{name}' is required.");

            return value.Trim();
        }

        private sealed record CachedDocument(BlobConfigurationDocument Document, DateTimeOffset ExpiresAt);
    }
}