namespace RemsToRems.Application.Features.PatientLookup.Matching
{
    public enum PatientLookupOutcome
    {
        /// <summary>No exact or partial candidate passed the trigger check.</summary>
        NoMatch = 0,

        /// <summary>One or more matches are released to the requesting REMS.</summary>
        Matched = 1,

        /// <summary>Too many exact candidates: reviewed internally, nothing released yet.</summary>
        PendingReview = 2
    }

    /// <summary>Values match the Decision column returned by the patient lookup SQL.</summary>
    public enum CandidateDecision
    {
        /// <summary>Tier "none": not a match, never disclosed (such records never leave SQL).</summary>
        NotAMatch = 0,

        /// <summary>A match, but the trigger check failed: no trigger, no disclosure.</summary>
        WithheldNoTrigger = 1,

        /// <summary>Held for internal review because of multiple exact matches.</summary>
        HeldForReview = 2,

        Released = 3
    }

    /// <summary>
    /// Step 4 result. The trigger check and the review hold are decided per candidate in SQL;
    /// the lookup outcome follows from those decisions.
    /// </summary>
    public sealed record PatientMatchReleaseResult(IReadOnlyList<PatientCandidate> Candidates)
    {
        public IEnumerable<PatientCandidate> Released =>
            Candidates.Where(candidate => candidate.Decision == CandidateDecision.Released);

        public PatientLookupOutcome Outcome =>
            Candidates.Any(candidate => candidate.Decision == CandidateDecision.HeldForReview) ? PatientLookupOutcome.PendingReview
            : Released.Any() ? PatientLookupOutcome.Matched
            : PatientLookupOutcome.NoMatch;
    }
}
