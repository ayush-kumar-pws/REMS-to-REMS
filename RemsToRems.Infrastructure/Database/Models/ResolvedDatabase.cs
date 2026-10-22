namespace RemsToRems.Infrastructure.Database.Models
{
    internal sealed record ResolvedDatabase(
        string Organization,
        string Environment,
        string ConnectionString);
}
