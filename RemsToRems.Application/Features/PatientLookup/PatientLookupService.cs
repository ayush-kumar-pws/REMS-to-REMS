using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Application.Features.PatientLookup.Matching;

namespace RemsToRems.Application.Features.PatientLookup
{
    /// <summary>
    /// REMS-to-REMS patient match: validate and normalize the request (PatientLookupValidator); compare, classify and decide release
    /// in SQL (PatientLookupRepository); then session, audit and response here.
    /// </summary>
    public sealed class PatientLookupService : IPatientLookupService
    {
        private const string AuditTableName = "User";
        private const string AuditLookupField = "PatientLookup";
        private const string AuditCandidateField = "PatientMatch";

        // No authentication yet; recorded as the system user by the audit service.
        private const long AnonymousUserId = 1;

        private readonly IPatientLookupRepository _repository;
        private readonly ILookupSessionRepository _sessions;
        private readonly IAuditTrailService _auditTrailService;
        private readonly PatientMatchOptions _options;
        private readonly DatabaseContext _database;

        public PatientLookupService(
            IPatientLookupRepository repository,
            ILookupSessionRepository sessions,
            IAuditTrailService auditTrailService,
            IOptions<PatientMatchOptions> options,
            IOptions<DatabaseContextOptions> database)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _auditTrailService = auditTrailService ?? throw new ArgumentNullException(nameof(auditTrailService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _database = database?.Value.ToDatabaseContext() ?? throw new ArgumentNullException(nameof(database));
        }

        public async Task<PatientLookupResponse> LookupAsync(
            PatientLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            // Reject bad input (400) with every problem listed; a valid request comes back normalized.
            var validated = PatientLookupValidator.Validate(request);
            var requested = validated.Patient;

            // From the "DatabaseContext" settings; the caller does not send it.
            var database = _database;

            // Steps 1-4 for stored records run in SQL: exact DOB and ZIP, SOUNDEX and a 1-edit check for names,
            // the tier, the trigger check and the review hold. Only exact and partial candidates come back.
            var candidates = await _repository
                .FindCandidatesAsync(database, requested, cancellationToken)
                .ConfigureAwait(false);

            var release = new PatientMatchReleaseResult(candidates);

            // Every completed lookup gets a session (valid SessionValidMinutes). It is not returned; it is stored
            // in the audit rows, where API 2 finds it through the remsId.
            var session = await _sessions
                .CreateAsync(database, AnonymousUserId, _options.SessionValidMinutes, cancellationToken)
                .ConfigureAwait(false);
            var sessionId = session.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // One hashed-GUID REMS ID per released match. REMS A sends it to API 2 to select that record;
            // API 2 finds it in the audit row, which also holds the session and the REMS User ID.
            var remsIds = release.Released.ToDictionary(
                candidate => candidate.UserId,
                _ => MatchUniqueId.New());

            var responseId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var asOf = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

            // Audit before responding. If it fails the lookup fails, so no lookup goes unaudited.
            //await _auditTrailService.AddAuditTrailAsync(
            //    database,
            //    BuildAuditEntries(release, validated.RequestId, responseId, sessionId, remsIds),
            //    AnonymousUserId,
            //    cancellationToken).ConfigureAwait(false);

            // Only released matches are returned. NoMatch and PendingReview both return an empty list,
            // so REMS A learns nothing about held records; the outcome is in the audit summary row.
            return new PatientLookupResponse
            {
                RequestId = validated.RequestId,
                ResponseId = responseId,
                AsOf = asOf,
                Patients = release.Released
                    .Select(candidate => ToPatientMatch(candidate, remsIds[candidate.UserId]))
                    .ToList()
            };
        }

        /// <summary>
        /// One summary row per lookup, plus one row per exact/partial candidate (RecordID = REMS User ID).
        /// Released candidates carry their issued RemsId. Every row carries the SessionId;
        /// the summary row also carries the RequestId and ResponseId.
        /// Only tiers, field results and decisions are stored; no names, DOB or ZIP.
        /// </summary>
        private static List<AuditEntry> BuildAuditEntries(
            PatientMatchReleaseResult release,
            Guid requestId,
            Guid responseId,
            string sessionId,
            IReadOnlyDictionary<int, string> remsIds)
        {
            var exact = release.Candidates.Count(candidate => candidate.Tier == PatientMatchTier.Exact);
            var partial = release.Candidates.Count(candidate => candidate.Tier == PatientMatchTier.Partial);

            var entries = new List<AuditEntry>
            {
                new()
                {
                    TransactionType = AuditTransactionType.Lookup,
                    DbTableName = AuditTableName,
                    DbFieldName = AuditLookupField,
                    PreviousValue = "-",
                    CurrentValue =
                        $"SessionId={sessionId}; RequestId={requestId}; ResponseId={responseId}; " +
                        $"Outcome={release.Outcome}; Candidates={release.Candidates.Count}; " +
                        $"Exact={exact}; Partial={partial}; Released={release.Released.Count()}"
                }
            };

            entries.AddRange(release.Candidates
                .Where(candidate => candidate.Tier != PatientMatchTier.None)
                .Select(candidate => new AuditEntry
                {
                    TransactionType = AuditTransactionType.Lookup,
                    DbTableName = AuditTableName,
                    RecordId = candidate.UserId,
                    DbFieldName = AuditCandidateField,
                    PreviousValue = "-",
                    CurrentValue =
                        $"SessionId={sessionId}; " +
                        (remsIds.TryGetValue(candidate.UserId, out var remsId) ? $"RemsId={remsId}; " : string.Empty) +
                        $"Tier={candidate.Tier}; FirstName={Describe(candidate.FirstNameMatch)}; " +
                        $"LastName={Describe(candidate.LastNameMatch)}; Zip={Describe(candidate.ZipMatch)}; " +
                        $"EnrolmentStatus={candidate.EnrolmentStatus ?? "-"}; " +
                        $"Triggered={(candidate.Triggered ? "Yes" : "No")}; Decision={candidate.Decision}"
                }));

            return entries;
        }

        private static string Describe(FieldMatchResult result) => result switch
        {
            FieldMatchResult.Exact => "Identical",
            FieldMatchResult.Partial => "Similar",
            _ => "Differs"
        };

        /// <summary>Response to REMS A: the issued REMS ID, the four elements (normalized) and the tier.</summary>
        private static PatientMatch ToPatientMatch(PatientCandidate released, string remsId) => new()
        {
            RemsId = remsId,
            FirstName = released.FirstName,
            LastName = released.LastName,
            DateOfBirth = released.DateOfBirth,
            Zipcode = released.ZipCode,
            MatchTier = released.Tier.ToString().ToUpperInvariant()
        };
    }
}
