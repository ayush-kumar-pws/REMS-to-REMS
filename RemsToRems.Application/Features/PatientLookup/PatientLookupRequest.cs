using RemsToRems.Application.Abstractions.Database;

namespace RemsToRems.Application.Features.PatientLookup
{
    public sealed class PatientLookupRequest : IHasDatabaseContext
    {
        public string ApplicationName { get; init; } = string.Empty;

        public string Environment { get; init; } = string.Empty;

        public string Organization { get; init; } = string.Empty;

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public DateOnly DateOfBirth { get; init; }

        public string Zip5 { get; init; } = string.Empty;
    }
}
