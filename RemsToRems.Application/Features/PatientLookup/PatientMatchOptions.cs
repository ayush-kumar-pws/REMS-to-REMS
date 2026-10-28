namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// Settings for the REMS-to-REMS patient match, bound from the "PatientMatching" section.
    /// The rules are "not final", so the parts most likely to change live here.
    /// </summary>
    public sealed class PatientMatchOptions
    {
        public const string SectionName = "PatientMatching";

        /// <summary>Question.Name of the DOB answer on the patient enrollment form.</summary>
        public string DobQuestionName { get; set; } = "DOB";

        /// <summary>Question.Name of the ZIP answer on the patient enrollment form.</summary>
        public string ZipQuestionName { get; set; } = "Zip Code";

        /// <summary>
        /// Trigger check: a candidate is disclosed only if its latest enrollment status is in this list
        /// (EnrolmentStatus.Name: Pending, Enrolled, Deactivated, Unenrolled). Empty = nothing is disclosed.
        /// </summary>
        // An array, not a List: configuration replaces an array but appends to an existing List.
        public string[] TriggerEnrolmentStatuses { get; set; } = { "Enrolled" };

        /// <summary>This many exact, triggered candidates (or more) go to internal review instead of being released.</summary>
        public int ReviewThresholdExactMatches { get; set; } = 2;

        /// <summary>How long the session returned by a lookup stays valid.</summary>
        public int SessionValidMinutes { get; set; } = 5;
    }
}
