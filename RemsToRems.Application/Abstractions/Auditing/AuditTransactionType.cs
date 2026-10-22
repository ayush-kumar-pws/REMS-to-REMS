namespace RemsToRems.Application.Abstractions.Auditing
{
    /// <summary>
    /// Values stored in [AuditTrail].[TransactionType]. The strings match SurveyApi's rows.
    /// </summary>
    public static class AuditTransactionType
    {
        public const string Insert = "INSERT";
        public const string Update = "UPDATE";
        public const string Delete = "DELETE";
        public const string Error = "ERROR";
        public const string Lookup = "LOOKUP";
    }
}
