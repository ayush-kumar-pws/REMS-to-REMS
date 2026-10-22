namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// The validated, trimmed search values passed to the repository.
    /// </summary>
    public sealed record PatientSearchCriteria(
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        string Zip5);
}
