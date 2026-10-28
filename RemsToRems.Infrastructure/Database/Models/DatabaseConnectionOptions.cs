namespace RemsToRems.Infrastructure.Database.Models
{
    /// <summary>
    /// Direct connection strings from the "ConnectionStrings" section. When set, they are used as-is
    /// instead of resolving the database from Azure Blob (Survey) and the Survey [Client] table (REMS).
    /// Leave empty to use the normal resolution.
    /// </summary>
    public sealed class DatabaseConnectionOptions
    {
        public const string SectionName = "ConnectionStrings";

        /// <summary>REMS database: patient lookup, session and audit.</summary>
        public string? RemsDb { get; set; }

        /// <summary>Survey database: "REMS Patient" role ID.</summary>
        public string? SurveyDb { get; set; }
    }
}
