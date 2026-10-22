namespace RemsToRems.Application.Features.PatientLookup
{
    public interface IPatientLookupService
    {
        Task<PatientLookupResponse> LookupAsync(
            PatientLookupRequest request,
            CancellationToken cancellationToken = default);
    }
}
