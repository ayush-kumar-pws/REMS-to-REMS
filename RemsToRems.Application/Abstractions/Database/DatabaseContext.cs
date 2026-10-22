namespace RemsToRems.Application.Abstractions.Database
{
    public sealed record DatabaseContext(
        string ApplicationName,
        string Environment,
        string Organization);
}
