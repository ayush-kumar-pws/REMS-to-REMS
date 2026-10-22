using RemsToRems.Application.Common.Exceptions;

namespace RemsToRems.Application.Features.PatientLookup
{
    internal static class PatientLookupValidator
    {
        public static void Validate(PatientLookupRequest? request)
        {
            if (request is null)
            {
                throw new ValidationException(new[] { "A request body is required." });
            }

            var errors = new List<string>();

            Require(errors, request.ApplicationName, nameof(request.ApplicationName));
            Require(errors, request.Environment, nameof(request.Environment));
            Require(errors, request.Organization, nameof(request.Organization));
            Require(errors, request.FirstName, nameof(request.FirstName));
            Require(errors, request.LastName, nameof(request.LastName));
            Require(errors, request.Zip5, nameof(request.Zip5));

            if (request.DateOfBirth == default)
            {
                errors.Add($"{nameof(request.DateOfBirth)} is required.");
            }

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }
        }

        private static void Require(List<string> errors, string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{name} is required.");
            }
        }
    }
}
