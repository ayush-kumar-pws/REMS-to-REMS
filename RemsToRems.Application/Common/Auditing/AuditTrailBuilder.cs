using RemsToRems.Application.Abstractions.Auditing;

namespace RemsToRems.Application.Common.Auditing
{
    /// <summary>
    /// Builds field-level audit entries the same way SurveyApi's GenerateAuditTrail does:
    /// INSERT records every field, UPDATE records only changed fields, DELETE records the name.
    /// </summary>
    public static class AuditTrailBuilder
    {
        private const string NoValue = "-";

        public static IReadOnlyList<AuditEntry> ForInsert(
            string dbTableName,
            int recordId,
            IEnumerable<(string FieldName, string? Value)> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);

            return fields
                .Select(field => Create(AuditTransactionType.Insert, dbTableName, recordId, field.FieldName, NoValue, field.Value))
                .ToList();
        }

        public static IReadOnlyList<AuditEntry> ForUpdate(
            string dbTableName,
            int recordId,
            IEnumerable<(string FieldName, string? Value)> oldFields,
            IEnumerable<(string FieldName, string? Value)> newFields)
        {
            ArgumentNullException.ThrowIfNull(oldFields);
            ArgumentNullException.ThrowIfNull(newFields);

            var oldValues = oldFields.ToDictionary(field => field.FieldName, field => field.Value);

            return newFields
                .Where(field =>
                {
                    oldValues.TryGetValue(field.FieldName, out var oldValue);
                    return Normalize(field.Value) != Normalize(oldValue);
                })
                .Select(field => Create(
                    AuditTransactionType.Update,
                    dbTableName,
                    recordId,
                    field.FieldName,
                    oldValues.GetValueOrDefault(field.FieldName),
                    field.Value))
                .ToList();
        }

        public static IReadOnlyList<AuditEntry> ForDelete(
            string dbTableName,
            int recordId,
            string? name)
        {
            return new[] { Create(AuditTransactionType.Delete, dbTableName, recordId, "Name", name, NoValue) };
        }

        private static AuditEntry Create(
            string transactionType,
            string dbTableName,
            int recordId,
            string fieldName,
            string? previousValue,
            string? currentValue)
        {
            return new AuditEntry
            {
                TransactionType = transactionType,
                DbTableName = dbTableName,
                RecordId = recordId,
                DbFieldName = fieldName,
                PreviousValue = previousValue,
                CurrentValue = currentValue
            };
        }

        // Mirrors SurveyApi ToolsInterface.StringToCompare: ignore surrounding whitespace and line breaks/tabs.
        private static string Normalize(string? value)
        {
            return value is null
                ? string.Empty
                : value.Trim().Replace("\r", "").Replace("\n", "").Replace("\t", "");
        }
    }
}
