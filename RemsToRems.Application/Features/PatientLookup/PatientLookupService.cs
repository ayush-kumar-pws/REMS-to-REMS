using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;

namespace RemsToRems.Application.Features.PatientLookup
{
    public sealed class PatientLookupService : IPatientLookupService
    {
        private const string AuditTableName = "User";
        private const string AuditFieldName = "PatientLookup";

        // No authentication yet; recorded as the system user by the audit service.
        private const long AnonymousUserId = 1;

        private readonly IPatientLookupRepository _repository;
        private readonly IAuditTrailService _auditTrailService;

        public PatientLookupService(
            IPatientLookupRepository repository,
            IAuditTrailService auditTrailService)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _auditTrailService = auditTrailService ?? throw new ArgumentNullException(nameof(auditTrailService));
        }

        public async Task<PatientLookupResponse> LookupAsync(
            PatientLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            PatientLookupValidator.Validate(request);

            var database = request.ToDatabaseContext();
            var criteria = new PatientSearchCriteria(
                request.FirstName.Trim(),
                request.LastName.Trim(),
                request.DateOfBirth,
                request.Zip5.Trim());

            var matches = await _repository
                .FindMatchesAsync(database, criteria, cancellationToken)
                .ConfigureAwait(false);

            // Record that a lookup happened. Only the match count is stored, not the
            // search criteria, so patient details are not copied into the audit table.
            // If the audit write fails the lookup fails, so no lookup goes unaudited.
            await _auditTrailService.AddAuditTrailAsync(
                database,
                new[]
                {
                    new AuditEntry
                    {
                        TransactionType = AuditTransactionType.Lookup,
                        DbTableName = AuditTableName,
                        DbFieldName = AuditFieldName,
                        PreviousValue = "-",
                        CurrentValue = $"{matches.Count} match(es)"
                    }
                },
                AnonymousUserId,
                cancellationToken).ConfigureAwait(false);

            return new PatientLookupResponse
            {
                Matches = matches
            };
        }
    }
}
