namespace RemsToRems.Application.Features.PatientLookup
{
    public sealed class PatientLookupResponse
    {
        public IReadOnlyList<PatientMatch> Matches { get; init; } = new List<PatientMatch>();
    }
}
