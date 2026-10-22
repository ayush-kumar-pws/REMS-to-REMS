namespace RemsToRems.Application.Abstractions.Database
{
    /// <summary>
    /// Implemented by request DTOs that identify the Survey/REMS database they target,
    /// so cross-cutting concerns such as error logging can resolve the same database.
    /// </summary>
    public interface IHasDatabaseContext
    {
        string ApplicationName { get; }

        string Environment { get; }

        string Organization { get; }
    }

    public static class DatabaseContextExtensions
    {
        public static DatabaseContext ToDatabaseContext(this IHasDatabaseContext source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return new DatabaseContext(
                source.ApplicationName,
                source.Environment,
                source.Organization);
        }
    }
}
