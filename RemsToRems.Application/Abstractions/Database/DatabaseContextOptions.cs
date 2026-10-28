namespace RemsToRems.Application.Abstractions.Database
{
    /// <summary>
    /// The Survey/REMS database this API works against, bound from the "DatabaseContext" section.
    /// The caller no longer sends it: every lookup, session, audit row and error log uses these values.
    /// </summary>
    public sealed class DatabaseContextOptions
    {
        public const string SectionName = "DatabaseContext";

        public string ApplicationName { get; set; } = "REMS";

        public string Environment { get; set; } = "dev";

        public string Organization { get; set; } = string.Empty;

        public DatabaseContext ToDatabaseContext() => new(ApplicationName, Environment, Organization);
    }
}
