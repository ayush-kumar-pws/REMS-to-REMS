namespace RemsToRems.Application.Features.PatientLookup
{
    public sealed class PatientMatch
    {
        public string UniqueId { get; init; } = string.Empty;

        public string EmailID { get; init; } = string.Empty;

        public string Username { get; init; } = string.Empty;

        public int CreatedBy { get; init; }

        public DateTime CreatedDate { get; init; }

        public int UpdatedBy { get; init; }

        public DateTime UpdatedDate { get; init; }

        public int Status { get; init; }

        public string NormalizedFirstName { get; init; } = string.Empty;

        public string NormalizedLastName { get; init; } = string.Empty;

        public DateOnly DateOfBirth { get; init; }

        public string Zip5 { get; init; } = string.Empty;

        public FieldMatchResult NameMatch { get; init; }

        public FieldMatchResult FirstNameMatch { get; init; }

        public FieldMatchResult LastNameMatch { get; init; }

        public FieldMatchResult ZipMatch { get; init; }

        public PatientMatchTier Tier { get; init; }

        public bool Triggered { get; init; }

        public IReadOnlyList<string> TriggerReasons { get; init; } = Array.Empty<string>();

        public bool ReviewRequired { get; init; }

        public string Display { get; init; } = string.Empty;
    }
}
