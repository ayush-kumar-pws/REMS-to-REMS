using System.Threading;
using System.Threading.Tasks;

namespace RemsToRems.Infrastructure.AzureBlob
{
    internal interface IBlobConfigurationProvider
    {
        Task<BlobConfigurationDocument> ReadAsync(
            string applicationName,
            string environment,
            CancellationToken cancellationToken = default);
    }
}
