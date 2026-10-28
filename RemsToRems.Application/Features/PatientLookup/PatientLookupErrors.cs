namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>JSON field names of the lookup request, as used in the body and in validation errors.</summary>
    public static class PatientLookupFields
    {
        public const string RequestId = "requestId";
        public const string Patient = "patient";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string DateOfBirth = "dateOfBirth";
        public const string ZipCode = "zipcode";
    }

    /// <summary>
    /// Partner error codes for API 1 validation. 100-111 are agreed; 112 is proposed.
    /// Cases without an agreed code use the nearest one (pending partner confirmation):
    /// all-zero requestId -> 101, DOB in the future -> 108, a value that is not a JSON string -> that
    /// field's invalid code (100, 112, 108, 110), missing patient object -> each patient field's missing code.
    /// </summary>
    public static class PatientLookupErrorCodes
    {
        public const int RequestIdInvalid = 100;
        public const int RequestIdMissing = 101;
        public const int FirstNameTooLong = 104;
        public const int FirstNameMissing = 105;
        public const int LastNameTooLong = 106;
        public const int LastNameMissing = 107;
        public const int DateOfBirthInvalid = 108;
        public const int DateOfBirthMissing = 109;
        public const int ZipCodeInvalid = 110;
        public const int ZipCodeMissing = 111;
        public const int NameUnsupportedCharacter = 112;
    }
}
