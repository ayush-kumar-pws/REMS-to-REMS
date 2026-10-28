using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Application.Common.Exceptions;
using RemsToRems.Application.Features.PatientLookup;
using RemsToRems.Application.Features.PatientLookup.Matching;

namespace RemsToRems.Application.Tests.PatientLookup
{
    /// <summary>
    /// End-to-end examples of step 4 (release) and the audit rows, with the database replaced by fakes.
    /// </summary>
    public sealed class PatientLookupServiceTests
    {
        private static readonly DateOnly Dob = new(1980, 1, 31);

        private static readonly IOptions<DatabaseContextOptions> DatabaseOptions =
            Options.Create(new DatabaseContextOptions { ApplicationName = "REMS", Environment = "DEV", Organization = "Sponsor" });

        private static readonly Guid RequestId = Guid.Parse("3de6e667-9c77-4ec3-b18e-cdfcc19cae4c");

        private static PatientLookupRequest Request(
            string? first = "John", string? last = "Smith", string? zip = "12345", string? dob = "1980-01-31",
            string? requestId = "3de6e667-9c77-4ec3-b18e-cdfcc19cae4c") => new()
        {
            RequestId = requestId,
            Patient = new PatientLookupPatient
            {
                FirstName = first,
                LastName = last,
                DateOfBirth = dob,
                Zipcode = zip
            }
        };

        // One field changed from a valid request; field is the JSON name (PatientLookupFields).
        private static PatientLookupRequest RequestWith(string field, string? value) => field switch
        {
            PatientLookupFields.RequestId => Request(requestId: value),
            PatientLookupFields.FirstName => Request(first: value),
            PatientLookupFields.LastName => Request(last: value),
            PatientLookupFields.DateOfBirth => Request(dob: value),
            PatientLookupFields.ZipCode => Request(zip: value),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        // What the repository returns: steps 1-4 already ran in SQL, so values are normalized and
        // the tier, trigger and decision are set.
        private static PatientCandidate Exact(int id, CandidateDecision decision = CandidateDecision.Released) => new()
        {
            UserId = id,
            FirstName = "JOHN",
            LastName = "SMITH",
            DateOfBirth = Dob,
            ZipCode = "12345",
            FirstNameMatch = FieldMatchResult.Exact,
            LastNameMatch = FieldMatchResult.Exact,
            ZipMatch = FieldMatchResult.Exact,
            Tier = PatientMatchTier.Exact,
            EnrolmentStatus = decision == CandidateDecision.WithheldNoTrigger ? "Unenrolled" : "Enrolled",
            Triggered = decision != CandidateDecision.WithheldNoTrigger,
            Decision = decision,
            Username = $"user{id}"
        };

        // JON vs JOHN: same SOUNDEX, edit distance 1.
        private static PatientCandidate Partial(int id, CandidateDecision decision = CandidateDecision.Released) => Exact(id, decision) with
        {
            FirstName = "JON",
            FirstNameMatch = FieldMatchResult.Partial,
            Tier = PatientMatchTier.Partial
        };

        private static (PatientLookupService Service, FakeAudit Audit) Create(params PatientCandidate[] candidates)
            => Create(new FakeSessions(), candidates);

        private static (PatientLookupService Service, FakeAudit Audit) Create(FakeSessions sessions, params PatientCandidate[] candidates)
        {
            var audit = new FakeAudit();
            var service = new PatientLookupService(
                new FakeRepository(candidates),
                sessions,
                audit,
                Options.Create(new PatientMatchOptions()), DatabaseOptions);
            return (service, audit);
        }

        [Fact]
        public async Task OneExactEnrolledPatient_IsReleased()
        {
            var (service, audit) = Create(Exact(10));

            var response = await service.LookupAsync(Request());

            Assert.Contains("Outcome=Matched;", audit.Entries[0].CurrentValue);
            Assert.Equal(RequestId, response.RequestId);
            Assert.NotEqual(Guid.Empty, response.ResponseId);
            Assert.Equal(DateTimeKind.Utc, response.AsOf.Kind);
            Assert.Equal(0, response.AsOf.Ticks % TimeSpan.TicksPerSecond);

            // Response to REMS A: issued REMS ID, the four elements and the tier. Nothing else.
            var match = Assert.Single(response.Patients);
            Assert.Matches("^[0-9a-f]{32}$", match.RemsId);
            Assert.Equal("JOHN", match.FirstName);
            Assert.Equal("SMITH", match.LastName);
            Assert.Equal(Dob, match.DateOfBirth);
            Assert.Equal("12345", match.Zipcode);
            Assert.Equal("EXACT", match.MatchTier);
            Assert.Equal(
                new[] { "DateOfBirth", "FirstName", "LastName", "MatchTier", "RemsId", "Zipcode" },
                typeof(PatientMatch).GetProperties().Select(property => property.Name).OrderBy(name => name));

            // Summary row + one row for the candidate (with the issued ID), with no names, DOB or ZIP.
            Assert.Equal(2, audit.Entries.Count);
            Assert.Equal(
                $"SessionId=900; RequestId={RequestId}; ResponseId={response.ResponseId}; " +
                "Outcome=Matched; Candidates=1; Exact=1; Partial=0; Released=1",
                audit.Entries[0].CurrentValue);
            Assert.Equal(10, audit.Entries[1].RecordId);
            Assert.StartsWith($"SessionId=900; RemsId={match.RemsId}; Tier=Exact;", audit.Entries[1].CurrentValue);
            Assert.Contains("Decision=Released", audit.Entries[1].CurrentValue);
            Assert.DoesNotContain(audit.Entries, entry => (entry.CurrentValue ?? "").Contains("SMITH"));
        }

        [Fact]
        public async Task EachReleasedMatch_GetsItsOwnRemsId()
        {
            // Exact (ID 60) + partial (ID 61): one exact is below the review threshold, so both are released.
            var (service, _) = Create(
                Exact(60),
                Partial(61));

            var response = await service.LookupAsync(Request());

            Assert.Equal(2, response.Patients.Count);
            Assert.Equal(2, response.Patients.Select(match => match.RemsId).Distinct().Count());
        }

        [Fact]
        public async Task Response_Json_IsExactlyTheExchangeFormat()
        {
            var (service, _) = Create(Exact(70), Partial(71));

            var response = await service.LookupAsync(Request());

            // Serialized the way ASP.NET Core does it (camelCase web defaults).
            var json = System.Text.Json.JsonSerializer.SerializeToElement(
                response, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            Assert.Equal(
                new[] { "requestId", "responseId", "asOf", "patients" },
                json.EnumerateObject().Select(property => property.Name));
            Assert.All(json.GetProperty("patients").EnumerateArray(), patient => Assert.Equal(
                new[] { "remsId", "firstName", "lastName", "dateOfBirth", "zipcode", "matchTier" },
                patient.EnumerateObject().Select(property => property.Name)));

            Assert.Equal("3de6e667-9c77-4ec3-b18e-cdfcc19cae4c", json.GetProperty("requestId").GetString());
            Assert.Matches("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$", json.GetProperty("asOf").GetString());
            var first = json.GetProperty("patients")[0];
            Assert.Equal("1980-01-31", first.GetProperty("dateOfBirth").GetString());
            Assert.Equal("EXACT", first.GetProperty("matchTier").GetString());
        }

        [Fact]
        public async Task PartialEnrolledPatient_IsReleasedAsPartial()
        {
            var (service, audit) = Create(Partial(11));

            var response = await service.LookupAsync(Request());

            Assert.Contains("Outcome=Matched;", audit.Entries[0].CurrentValue);
            Assert.Equal("PARTIAL", Assert.Single(response.Patients).MatchTier);
        }

        [Fact]
        public async Task MatchWithoutTrigger_IsNotDisclosed()
        {
            // Unenrolled is not a trigger status: no trigger, no disclosure.
            var (service, audit) = Create(Exact(12, CandidateDecision.WithheldNoTrigger));

            var response = await service.LookupAsync(Request());

            Assert.Contains("Outcome=NoMatch;", audit.Entries[0].CurrentValue);
            Assert.Empty(response.Patients);
            Assert.Contains("Decision=WithheldNoTrigger", audit.Entries[1].CurrentValue);
        }

        [Fact]
        public async Task TwoExactEnrolledPatients_GoToReview_AndNothingIsReleased()
        {
            var (service, audit) = Create(
                Exact(20, CandidateDecision.HeldForReview),
                Exact(21, CandidateDecision.HeldForReview));

            var response = await service.LookupAsync(Request());

            Assert.Contains("Outcome=PendingReview;", audit.Entries[0].CurrentValue);
            // Nothing is returned: REMS A cannot tell "held for review" from "no match".
            Assert.Empty(response.Patients);
            Assert.All(audit.Entries.Skip(1), entry => Assert.Contains("Decision=HeldForReview", entry.CurrentValue));
            Assert.DoesNotContain(audit.Entries, entry => (entry.CurrentValue ?? "").Contains("RemsId="));
        }

        [Fact]
        public async Task NoCandidates_IsNoMatch_WithOnlyTheSummaryRow()
        {
            // Tier "none" records never leave SQL, so the repository returns nothing.
            var (service, audit) = Create();

            var response = await service.LookupAsync(Request());

            Assert.Contains("Outcome=NoMatch;", audit.Entries[0].CurrentValue);
            var summary = Assert.Single(audit.Entries);
            Assert.Equal(
                $"SessionId=900; RequestId={RequestId}; ResponseId={response.ResponseId}; " +
                "Outcome=NoMatch; Candidates=0; Exact=0; Partial=0; Released=0",
                summary.CurrentValue);
            Assert.Empty(response.Patients);
        }

        // ---- Validation: bad input is a 400 with partner codes, never a "no match" ---------

        [Theory]
        [InlineData(PatientLookupFields.RequestId, null, 101, "missing")]
        [InlineData(PatientLookupFields.RequestId, "", 101, "missing")]
        [InlineData(PatientLookupFields.RequestId, "   ", 101, "missing")]
        [InlineData(PatientLookupFields.RequestId, "00000000-0000-0000-0000-000000000000", 101, "missing")]
        [InlineData(PatientLookupFields.RequestId, "not-a-guid", 100, "not a valid UUID")]
        [InlineData(PatientLookupFields.RequestId, "{3de6e667-9c77-4ec3-b18e-cdfcc19cae4c}", 100, "not a valid UUID")]
        [InlineData(PatientLookupFields.FirstName, null, 105, "missing")]
        [InlineData(PatientLookupFields.FirstName, "", 105, "missing")]
        [InlineData(PatientLookupFields.FirstName, "   ", 105, "missing")]
        [InlineData(PatientLookupFields.FirstName, "Dr.", 105, "empty after cleanup")]
        [InlineData(PatientLookupFields.FirstName, "Mr Dr", 105, "empty after cleanup")]
        [InlineData(PatientLookupFields.FirstName, "'.-", 105, "empty after cleanup")]
        [InlineData(PatientLookupFields.FirstName, "Smith3", 112, "unsupported character")]
        [InlineData(PatientLookupFields.FirstName, "John@", 112, "unsupported character")]
        [InlineData(PatientLookupFields.FirstName, "Иван", 112, "unsupported character")]   // not Latin
        [InlineData(PatientLookupFields.LastName, null, 107, "missing")]
        [InlineData(PatientLookupFields.LastName, ", .", 107, "empty after cleanup")]
        [InlineData(PatientLookupFields.LastName, "Smith 3rd", 112, "unsupported character")]
        [InlineData(PatientLookupFields.DateOfBirth, null, 109, "missing")]
        [InlineData(PatientLookupFields.DateOfBirth, "", 109, "missing")]
        [InlineData(PatientLookupFields.DateOfBirth, "01/31/1980", 108, "not a valid date in the format yyyy-MM-dd")]
        [InlineData(PatientLookupFields.DateOfBirth, "1980-02-30", 108, "not a valid date in the format yyyy-MM-dd")]
        [InlineData(PatientLookupFields.DateOfBirth, "2021-02-29", 108, "not a valid date in the format yyyy-MM-dd")]
        [InlineData(PatientLookupFields.DateOfBirth, "1980-01-31T00:00:00Z", 108, "not a valid date in the format yyyy-MM-dd")]
        [InlineData(PatientLookupFields.DateOfBirth, "2999-01-01", 108, "in the future")]
        [InlineData(PatientLookupFields.ZipCode, null, 111, "missing")]
        [InlineData(PatientLookupFields.ZipCode, "", 111, "missing")]
        [InlineData(PatientLookupFields.ZipCode, "1234", 110, "not exactly 5 digits")]
        [InlineData(PatientLookupFields.ZipCode, "12345-6789", 110, "not exactly 5 digits")]   // ZIP+4 is not accepted
        [InlineData(PatientLookupFields.ZipCode, "ABCDE", 110, "not exactly 5 digits")]
        [InlineData(PatientLookupFields.ZipCode, "１２３４５", 110, "not exactly 5 digits")]   // full-width digits
        public async Task InvalidField_IsRejectedWithItsCode(string field, string? value, int code, string reason)
        {
            var repository = new FakeRepository(new[] { Exact(34) });
            var sessions = new FakeSessions();
            var service = new PatientLookupService(
                repository, sessions, new FakeAudit(), Options.Create(new PatientMatchOptions()), DatabaseOptions);

            var error = await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(RequestWith(field, value)));

            Assert.Equal(new ValidationError(field, reason, code), Assert.Single(error.Errors));
            Assert.Equal(0, repository.Calls);
            Assert.Equal(0, sessions.Created);
        }

        [Theory]
        [InlineData(PatientLookupFields.FirstName, 104)]
        [InlineData(PatientLookupFields.LastName, 106)]
        public async Task NameLongerThan255_IsRejected(string field, int code)
        {
            var (service, _) = Create();

            await service.LookupAsync(RequestWith(field, new string('A', 255)));
            var error = await Assert.ThrowsAsync<ValidationException>(
                () => service.LookupAsync(RequestWith(field, new string('A', 256))));

            Assert.Equal(new ValidationError(field, "longer than 255 characters", code), Assert.Single(error.Errors));
        }

        [Fact]
        public async Task AllProblems_AreReportedTogether_OnePerField()
        {
            var repository = new FakeRepository(Array.Empty<PatientCandidate>());
            var service = new PatientLookupService(
                repository, new FakeSessions(), new FakeAudit(), Options.Create(new PatientMatchOptions()), DatabaseOptions);

            var error = await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(
                Request(first: "", last: "Sm1th", zip: "1", dob: "bad", requestId: "nope")));

            Assert.Equal(
                new[]
                {
                    new ValidationError("requestId", "not a valid UUID", 100),
                    new ValidationError("firstName", "missing", 105),
                    new ValidationError("lastName", "unsupported character", 112),
                    new ValidationError("dateOfBirth", "not a valid date in the format yyyy-MM-dd", 108),
                    new ValidationError("zipcode", "not exactly 5 digits", 110)
                },
                error.Errors);
            Assert.Null(error.RequestId);
            Assert.Equal(0, repository.Calls);
        }

        [Fact]
        public async Task ValidRequestId_IsKeptOnTheValidationError()
        {
            var (service, _) = Create();

            var error = await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(Request(zip: "1")));

            Assert.Equal(RequestId, error.RequestId);
        }

        [Fact]
        public async Task Errors_NeverContainTheSubmittedValues()
        {
            var (service, _) = Create();

            var error = await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(
                Request(first: "Zebedee9", last: "Quixote@", zip: "9876", dob: "1999-13-01")));

            Assert.All(new[] { "Zebedee", "Quixote", "9876", "1999-13-01" },
                value => Assert.DoesNotContain(value, error.Message));
        }

        [Fact]
        public async Task MissingPatientObject_ReportsEachPatientFieldMissing()
        {
            var (service, _) = Create();

            var error = await Assert.ThrowsAsync<ValidationException>(
                () => service.LookupAsync(new PatientLookupRequest { RequestId = RequestId.ToString() }));

            Assert.Equal(new[] { 105, 107, 109, 111 }, error.Errors.Select(e => e.Code));
        }

        [Fact]
        public async Task NonStringValues_AreInvalid_NotMissing()
        {
            var (service, _) = Create();
            var request = new PatientLookupRequest
            {
                Patient = new PatientLookupPatient(),
                NonStringFields = new[] { "requestId", "firstName", "lastName", "dateOfBirth", "zipcode" }
            };

            var error = await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(request));

            Assert.Equal(new[] { 100, 112, 112, 108, 110 }, error.Errors.Select(e => e.Code));
            Assert.All(error.Errors, e => Assert.Equal("must be a string", e.Reason));
        }

        [Fact]
        public async Task NullRequest_ReportsEveryFieldMissing()
        {
            var (service, _) = Create();

            var error = await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(null!));

            Assert.Equal(new[] { 101, 105, 107, 109, 111 }, error.Errors.Select(e => e.Code));
        }

        // ---- Normalization of the request (what the repository receives) -------------------

        [Theory]
        [InlineData("José", "JOSE")]
        [InlineData("José", "JOSE")]            // decomposed accent
        [InlineData("Mary-Ann", "MARYANN")]
        [InlineData("Mary–Ann", "MARYANN")]      // en dash
        [InlineData("Mary Ann", "MARY")]
        [InlineData("J.R.", "JR")]                    // periods removed without a space
        [InlineData("D‘Angelo", "DANGELO")]      // left curly quote
        [InlineData("Søren", "SOREN")]
        [InlineData("Dr. Mrs. Mary-Ann Lou", "MARYANN")]
        public async Task FirstName_IsNormalized(string first, string expected)
        {
            Assert.Equal(expected, (await Normalized(Request(first: first))).FirstName);
        }

        [Theory]
        [InlineData("O'Brien", "OBRIEN")]
        [InlineData("O’Brien", "OBRIEN")]             // curly apostrophe (phones, Word)
        [InlineData("De la Cruz", "DELACRUZ")]
        [InlineData("Smith, Jr.", "SMITH")]
        [InlineData("Van Der Berg Jr. III", "VANDERBERG")]
        [InlineData("Jr", "JR")]                      // a suffix is dropped only while another word remains
        [InlineData("Straße", "STRASSE")]
        public async Task LastName_IsNormalized(string last, string expected)
        {
            Assert.Equal(expected, (await Normalized(Request(last: last))).LastName);
        }

        [Fact]
        public async Task Zip_KeepsLeadingZero()
        {
            Assert.Equal("01234", (await Normalized(Request(zip: "01234"))).ZipCode);
        }

        [Fact]
        public async Task Repository_GetsTheNormalizedRequest()
        {
            Assert.Equal(
                new NormalizedPatientIdentity("MARYANN", "VANDERBERG", Dob, "12345"),
                await Normalized(Request(first: "Dr. Mary-Ann", last: "Van Der Berg Jr.", zip: "12345")));
        }

        private static async Task<NormalizedPatientIdentity> Normalized(PatientLookupRequest request)
        {
            var repository = new FakeRepository(Array.Empty<PatientCandidate>());
            var service = new PatientLookupService(
                repository, new FakeSessions(), new FakeAudit(), Options.Create(new PatientMatchOptions()), DatabaseOptions);

            await service.LookupAsync(request);

            return repository.LastRequested!;
        }

        // ---- Session created by every lookup (REMS [Session] table, valid 5 minutes) --------

        [Fact]
        public async Task EveryLookup_CreatesASession_AndAuditsIt_EvenNoMatch()
        {
            var sessions = new FakeSessions();
            var (service, audit) = Create(sessions);

            var response = await service.LookupAsync(Request());

            Assert.Empty(response.Patients);
            Assert.Equal(1, sessions.Created);
            Assert.StartsWith("SessionId=900;", audit.Entries[0].CurrentValue);
            Assert.Equal(5, sessions.LastValidMinutes);
        }

        [Fact]
        public async Task RejectedRequest_CreatesNoSession()
        {
            var sessions = new FakeSessions();
            var (service, _) = Create(sessions, Exact(50));

            await Assert.ThrowsAsync<ValidationException>(() => service.LookupAsync(Request(first: "John3")));

            Assert.Equal(0, sessions.Created);
        }

        private sealed class FakeSessions : ILookupSessionRepository
        {
            public DateTime ExpiresAt { get; } = new(2026, 10, 23, 12, 5, 0);

            public int Created { get; private set; }

            public int LastValidMinutes { get; private set; }

            public Task<LookupSession> CreateAsync(
                DatabaseContext database, long userId, int validMinutes, CancellationToken cancellationToken = default)
            {
                Created++;
                LastValidMinutes = validMinutes;
                return Task.FromResult(new LookupSession(900, ExpiresAt));
            }
        }

        private sealed class FakeRepository : IPatientLookupRepository
        {
            private readonly IReadOnlyList<PatientCandidate> _candidates;

            public FakeRepository(IReadOnlyList<PatientCandidate> candidates) => _candidates = candidates;

            public int Calls { get; private set; }

            public NormalizedPatientIdentity? LastRequested { get; private set; }

            public Task<IReadOnlyList<PatientCandidate>> FindCandidatesAsync(
                DatabaseContext database, NormalizedPatientIdentity requested, CancellationToken cancellationToken = default)
            {
                Calls++;
                LastRequested = requested;
                return Task.FromResult(_candidates);
            }
        }

        private sealed class FakeAudit : IAuditTrailService
        {
            public List<AuditEntry> Entries { get; } = new();

            public Task AddAuditTrailAsync(
                DatabaseContext context, IReadOnlyCollection<AuditEntry> entries, long currentUserId,
                CancellationToken cancellationToken = default)
            {
                Entries.AddRange(entries);
                return Task.CompletedTask;
            }

            public Task InsertErrorLogAsync(
                DatabaseContext? context, ErrorLog errorLog, long currentUserId,
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
