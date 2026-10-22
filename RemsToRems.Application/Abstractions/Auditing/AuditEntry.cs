namespace RemsToRems.Application.Abstractions.Auditing;

/// <summary>
/// One field-level change written to the [AuditTrail] table.
/// CreatedBy/UpdatedBy/Status are supplied by <see cref="IAuditTrailService"/>.
/// </summary>
public sealed record AuditEntry
{
    public required string TransactionType { get; init; }

    public required string DbTableName { get; init; }

    public int RecordId { get; init; }

    public string? DbFieldName { get; init; }

    public string? PreviousValue { get; init; }

    public string? CurrentValue { get; init; }
}
