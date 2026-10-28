namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// Patient lookup request, as REMS A sends it:
    /// { "requestId": "...", "patient": { "firstName", "lastName", "dateOfBirth", "zipcode" } }.
    /// Every value is kept as received text, so PatientLookupValidator can report each problem with its code.
    /// The target database is not part of the request; it comes from the "DatabaseContext" and
    /// "ConnectionStrings" settings.
    /// </summary>
    public sealed class PatientLookupRequest
    {
        /// <summary>The caller's ID for this request (a UUID); echoed in the response.</summary>
        public string? RequestId { get; init; }

        public PatientLookupPatient? Patient { get; init; }

        /// <summary>
        /// Fields (PatientLookupFields names) whose JSON value was not a string, e.g. a ZIP sent as a number.
        /// Set by PatientLookupRequestReader; such a field is invalid, not missing.
        /// </summary>
        public IReadOnlyCollection<string> NonStringFields { get; init; } = Array.Empty<string>();
    }

    public sealed class PatientLookupPatient
    {
        public string? FirstName { get; init; }

        public string? LastName { get; init; }

        /// <summary>yyyy-MM-dd (UTC date).</summary>
        public string? DateOfBirth { get; init; }

        /// <summary>Exactly five digits, as text so leading zeroes are kept.</summary>
        public string? Zipcode { get; init; }
    }
}
