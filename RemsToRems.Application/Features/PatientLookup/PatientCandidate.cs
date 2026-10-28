using RemsToRems.Application.Features.PatientLookup.Matching;

namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// A REMS patient that matched the request (exact or partial). Normalizing, comparing, the tier and the
    /// release decision (steps 1-4) all run in SQL; names and ZIP are the normalized values that were compared.
    /// </summary>
    public sealed record PatientCandidate
    {
        /// <summary>REMS [User].[ID]; used as the audit RecordID.</summary>
        public int UserId { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        /// <summary>Always the requested DOB: the SQL DOB gate is exact.</summary>
        public DateOnly DateOfBirth { get; init; }

        /// <summary>"Zip Code" answer from the latest patient enrollment, first 5 digits; empty if missing or unusable.</summary>
        public string ZipCode { get; init; } = string.Empty;

        public FieldMatchResult FirstNameMatch { get; init; }

        public FieldMatchResult LastNameMatch { get; init; }

        public FieldMatchResult ZipMatch { get; init; }

        public PatientMatchTier Tier { get; init; }

        /// <summary>EnrolmentStatus.Name of the latest patient enrollment (Pending, Enrolled, ...).</summary>
        public string? EnrolmentStatus { get; init; }

        /// <summary>Step 4 trigger check: the enrollment status is one of the trigger statuses.</summary>
        public bool Triggered { get; init; }

        /// <summary>Step 4 release decision, made in SQL.</summary>
        public CandidateDecision Decision { get; init; }

        public string Username { get; init; } = string.Empty;

        public string EmailID { get; init; } = string.Empty;

        public int CreatedBy { get; init; }

        public DateTime CreatedDate { get; init; }

        public int UpdatedBy { get; init; }

        public DateTime UpdatedDate { get; init; }

        public int Status { get; init; }
    }
}
