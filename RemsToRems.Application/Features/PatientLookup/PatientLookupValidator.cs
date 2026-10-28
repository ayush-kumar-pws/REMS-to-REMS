using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RemsToRems.Application.Common.Exceptions;
using RemsToRems.Application.Features.PatientLookup.Matching;
using Codes = RemsToRems.Application.Features.PatientLookup.PatientLookupErrorCodes;
using Fields = RemsToRems.Application.Features.PatientLookup.PatientLookupFields;

namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>The validated request: the parsed requestId and the patient values ready to compare.</summary>
    public sealed record ValidatedLookupRequest(Guid RequestId, NormalizedPatientIdentity Patient);

    /// <summary>
    /// Every check on the request, in one place. Each field gets at most one error, with its partner code,
    /// and all errors are reported together as one 400. Names are normalized here, because a name that is
    /// empty after cleanup is also an error.
    /// </summary>
    internal static partial class PatientLookupValidator
    {
        public const string DateOfBirthFormat = "yyyy-MM-dd";

        public const int MaxNameLength = 255;

        // Checked after PatientNameNormalizer.ToAscii, so accents, curly apostrophes and long dashes are already plain.
        [GeneratedRegex("^[A-Za-z '.,-]+\\z")]
        private static partial Regex AllowedName();

        [GeneratedRegex("^[0-9]{5}\\z")]
        private static partial Regex AllowedZip();

        public static ValidatedLookupRequest Validate(PatientLookupRequest? request)
        {
            // An absent body or patient object is reported field by field, each as missing.
            request ??= new PatientLookupRequest();
            var patient = request.Patient ?? new PatientLookupPatient();
            var errors = new List<ValidationError>();
            var nonString = request.NonStringFields;

            var requestId = CheckRequestId(errors, request.RequestId, nonString);
            var firstName = CheckName(errors, patient.FirstName, Fields.FirstName, Codes.FirstNameMissing,
                Codes.FirstNameTooLong, PatientNameNormalizer.NormalizeFirstName, nonString);
            var lastName = CheckName(errors, patient.LastName, Fields.LastName, Codes.LastNameMissing,
                Codes.LastNameTooLong, PatientNameNormalizer.NormalizeLastName, nonString);
            var dateOfBirth = CheckDateOfBirth(errors, patient.DateOfBirth, nonString);
            var zipCode = CheckZip(errors, patient.Zipcode, nonString);

            if (errors.Count > 0)
            {
                throw new ValidationException(errors, requestId);
            }

            return new ValidatedLookupRequest(
                requestId!.Value,
                new NormalizedPatientIdentity(firstName!, lastName!, dateOfBirth!.Value, zipCode!));
        }

        private static Guid? CheckRequestId(List<ValidationError> errors, string? value, IReadOnlyCollection<string> nonString)
        {
            if (nonString.Contains(Fields.RequestId))
            {
                errors.Add(new(Fields.RequestId, "must be a string", Codes.RequestIdInvalid));
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new(Fields.RequestId, "missing", Codes.RequestIdMissing));
                return null;
            }

            // Only the standard UUID form (8-4-4-4-12 hex digits); braces or 32 bare digits are rejected.
            if (!Guid.TryParseExact(value, "D", out var requestId))
            {
                errors.Add(new(Fields.RequestId, "not a valid UUID", Codes.RequestIdInvalid));
                return null;
            }

            // The all-zero UUID carries no ID, so it counts as missing (pending partner confirmation).
            if (requestId == Guid.Empty)
            {
                errors.Add(new(Fields.RequestId, "missing", Codes.RequestIdMissing));
                return null;
            }

            return requestId;
        }

        // missing -> too long -> unsupported character -> empty after cleanup.
        private static string? CheckName(
            List<ValidationError> errors,
            string? value,
            string field,
            int missingCode,
            int tooLongCode,
            Func<string, string> cleanUp,
            IReadOnlyCollection<string> nonString)
        {
            if (nonString.Contains(field))
            {
                errors.Add(new(field, "must be a string", Codes.NameUnsupportedCharacter));
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new(field, "missing", missingCode));
                return null;
            }

            var text = value.Trim().Normalize(NormalizationForm.FormC);
            if (text.Length > MaxNameLength)
            {
                errors.Add(new(field, $"longer than {MaxNameLength} characters", tooLongCode));
                return null;
            }

            // Characters that cannot be made plain ASCII are rejected, never silently removed.
            var ascii = PatientNameNormalizer.ToAscii(text);
            if (!AllowedName().IsMatch(ascii))
            {
                errors.Add(new(field, "unsupported character", Codes.NameUnsupportedCharacter));
                return null;
            }

            // "Dr." as a first name, or only punctuation, leaves nothing to compare.
            var cleaned = cleanUp(ascii);
            if (cleaned.Length == 0)
            {
                errors.Add(new(field, "empty after cleanup", missingCode));
                return null;
            }

            return cleaned;
        }

        private static DateOnly? CheckDateOfBirth(List<ValidationError> errors, string? value, IReadOnlyCollection<string> nonString)
        {
            if (nonString.Contains(Fields.DateOfBirth))
            {
                errors.Add(new(Fields.DateOfBirth, "must be a string", Codes.DateOfBirthInvalid));
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new(Fields.DateOfBirth, "missing", Codes.DateOfBirthMissing));
                return null;
            }

            // yyyy-MM-dd and a real calendar date (no 1980-02-30).
            if (!DateOnly.TryParseExact(value.Trim(), DateOfBirthFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dateOfBirth))
            {
                errors.Add(new(Fields.DateOfBirth, "not a valid date in the format yyyy-MM-dd", Codes.DateOfBirthInvalid));
                return null;
            }

            // No agreed code for a future date; 108 is the nearest (pending partner confirmation).
            if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                errors.Add(new(Fields.DateOfBirth, "in the future", Codes.DateOfBirthInvalid));
                return null;
            }

            return dateOfBirth;
        }

        private static string? CheckZip(List<ValidationError> errors, string? value, IReadOnlyCollection<string> nonString)
        {
            if (nonString.Contains(Fields.ZipCode))
            {
                errors.Add(new(Fields.ZipCode, "must be a string", Codes.ZipCodeInvalid));
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new(Fields.ZipCode, "missing", Codes.ZipCodeMissing));
                return null;
            }

            if (!AllowedZip().IsMatch(value.Trim()))
            {
                errors.Add(new(Fields.ZipCode, "not exactly 5 digits", Codes.ZipCodeInvalid));
                return null;
            }

            return PatientNameNormalizer.NormalizeZip(value);
        }
    }
}
