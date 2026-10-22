using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Features.PatientLookup;

namespace RemsToRems.Application.Abstractions.Repositories
{
    public interface IPatientLookupRepository
    {
        Task<IReadOnlyList<PatientMatch>> FindMatchesAsync(
            DatabaseContext database,
            PatientSearchCriteria criteria,
            CancellationToken cancellationToken = default);
    }
}
