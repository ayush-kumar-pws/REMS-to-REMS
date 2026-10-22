using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RemsToRems.Infrastructure.AzureBlob
{
    internal sealed class BlobConfigurationDocument
    {
        [JsonPropertyName("config")]
        public BlobConfigurationRoot? Config { get; set; }
    }

    internal sealed class BlobConfigurationRoot
    {
        [JsonPropertyName("database")]
        public List<BlobDatabaseEntry> Database { get; set; } = new();
    }

    internal sealed class BlobDatabaseEntry
    {
        public string? DatabaseConnectionString { get; set; }

        public string? ApplicationName { get; set; }

        public string? ApplicationURL { get; set; }

        public string? Environment { get; set; }
    }
}
