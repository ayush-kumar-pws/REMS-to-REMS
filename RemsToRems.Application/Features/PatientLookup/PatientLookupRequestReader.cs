using System.Text.Json;

namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// Reads the JSON body into a PatientLookupRequest without rejecting anything, so every problem reaches
    /// PatientLookupValidator and is reported together. A missing or null value stays null (missing);
    /// any other non-string value is recorded in NonStringFields (invalid). Property names are matched
    /// case-insensitively ("zipcode" or "zipCode").
    /// </summary>
    public static class PatientLookupRequestReader
    {
        public static PatientLookupRequest Read(JsonElement body)
        {
            // A body that is not a JSON object has no fields: each one is reported missing.
            if (body.ValueKind != JsonValueKind.Object)
            {
                return new PatientLookupRequest();
            }

            var nonStringFields = new List<string>();
            var requestId = ReadString(body, PatientLookupFields.RequestId, nonStringFields);

            // A missing patient, or one that is not an object, leaves every patient field missing.
            PatientLookupPatient? patient = null;
            if (TryGetProperty(body, PatientLookupFields.Patient, out var patientElement)
                && patientElement.ValueKind == JsonValueKind.Object)
            {
                patient = new PatientLookupPatient
                {
                    FirstName = ReadString(patientElement, PatientLookupFields.FirstName, nonStringFields),
                    LastName = ReadString(patientElement, PatientLookupFields.LastName, nonStringFields),
                    DateOfBirth = ReadString(patientElement, PatientLookupFields.DateOfBirth, nonStringFields),
                    Zipcode = ReadString(patientElement, PatientLookupFields.ZipCode, nonStringFields)
                };
            }

            return new PatientLookupRequest
            {
                RequestId = requestId,
                Patient = patient,
                NonStringFields = nonStringFields
            };
        }

        private static string? ReadString(JsonElement parent, string field, List<string> nonStringFields)
        {
            if (!TryGetProperty(parent, field, out var value))
            {
                return null;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Null:
                    return null;
                default:
                    nonStringFields.Add(field);
                    return null;
            }
        }

        private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
        {
            foreach (var property in parent.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
