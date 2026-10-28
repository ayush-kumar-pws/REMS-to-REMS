using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Features.PatientLookup;
using RemsToRems.Application.Features.PatientLookup.Matching;

namespace RemsToRems.Application.Abstractions.Repositories
{
    public interface IPatientLookupRepository
    {
        /// <summary>
        /// REMS patients whose latest enrollment DOB equals the requested DOB and whose tier is exact or partial.
        /// Steps 1-4 (normalize stored values, compare with SOUNDEX and a 1-edit check, classify, release) run in SQL.
        /// </summary>
        Task<IReadOnlyList<PatientCandidate>> FindCandidatesAsync(
            DatabaseContext database,
            NormalizedPatientIdentity requested,
            CancellationToken cancellationToken = default);
    }
}
