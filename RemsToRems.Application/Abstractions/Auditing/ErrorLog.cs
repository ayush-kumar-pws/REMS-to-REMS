namespace RemsToRems.Application.Abstractions.Auditing
{
    /// <summary>
    /// One row written to the [ErrorLog] table.
    /// CreatedBy/UpdatedBy/Status are supplied by <see cref="IAuditTrailService"/>.
    /// </summary>
    public sealed record ErrorLog
    {
        public string OriginUrl { get; init; } = string.Empty;

        public string ControllerName { get; init; } = string.Empty;

        public string MethodName { get; init; } = string.Empty;

        public string RequestJson { get; init; } = string.Empty;

        public string ErrorJson { get; init; } = string.Empty;
    }
}
