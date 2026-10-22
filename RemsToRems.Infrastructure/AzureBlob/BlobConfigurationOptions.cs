namespace RemsToRems.Infrastructure.AzureBlob
{
    public sealed class BlobConfigurationOptions
    {
        public const string SectionName = "AzureBlobConfiguration";

        public string? ConnectionString { get; set; }

        public string? ContainerName { get; set; }

        public string BlobNamePattern { get; set; } = "{environment}.json";

        /// <summary>
        /// How long a downloaded configuration blob is reused before it is read again.
        /// </summary>
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    }
}
