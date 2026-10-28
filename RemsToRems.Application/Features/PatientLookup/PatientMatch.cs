namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// One released match as sent to REMS A: an issued REMS ID, the four matched elements
    /// (normalized, as compared) and the tier. Nothing else about the patient is disclosed.
    /// </summary>
    public sealed class PatientMatch
    {
        /// <summary>
        /// Hashed GUID issued for this match (not the REMS User ID). REMS A sends it to API 2 to select
        /// the record; API 2 maps it back to the REMS User ID through the audit row.
        /// </summary>
        public string RemsId { get; init; } = string.Empty;

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public DateOnly DateOfBirth { get; init; }

        public string Zipcode { get; init; } = string.Empty;

        /// <summary>"NO MATCH" or "EXACT" or "PARTIAL".</summary>
        public string MatchTier { get; init; } = string.Empty;
    }
}
