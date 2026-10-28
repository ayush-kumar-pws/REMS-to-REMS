namespace RemsToRems.Application.Features.PatientLookup.Matching
{
    /// <summary>Step 1 output: values ready to compare. Built by PatientLookupValidator.</summary>
    public sealed record NormalizedPatientIdentity(string FirstName, string LastName, DateOnly DateOfBirth, string ZipCode);

    /// <summary>
    /// REMS-to-REMS patient match rules. Step 1 for the request runs in PatientLookupValidator;
    /// steps 1-4 for stored records run in SQL in the patient lookup repository.
    /// </summary>
    public static class PatientMatcher
    {
        /// <summary>Names this long or shorter also need to be at most 1 edit apart (checked in SQL).</summary>
        public const int ShortNameLength = 3;
    }
}
