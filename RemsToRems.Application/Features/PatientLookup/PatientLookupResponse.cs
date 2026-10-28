namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// The lookup response, exactly as REMS A receives it (no wrapper). The session and the outcome
    /// (Matched / NoMatch / PendingReview) are recorded in the audit rows, not returned.
    /// </summary>
    public sealed class PatientLookupResponse
    {
        /// <summary>The RequestId sent by the caller.</summary>
        public Guid RequestId { get; init; }

        /// <summary>New for every response; audited with the lookup.</summary>
        public Guid ResponseId { get; init; }

        /// <summary>When the lookup ran (UTC, whole seconds).</summary>
        public DateTime AsOf { get; init; }

        /// <summary>Released matches only. Empty when nothing is released (no match, or held for review).</summary>
        public IReadOnlyList<PatientMatch> Patients { get; init; } = new List<PatientMatch>();
    }
}
