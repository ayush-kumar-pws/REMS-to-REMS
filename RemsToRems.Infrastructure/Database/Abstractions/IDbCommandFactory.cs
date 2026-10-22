using System.Data.Common;

namespace RemsToRems.Infrastructure.Database.Abstractions
{
    /// <summary>
    /// Low-level command builder used by <see cref="IDbCommandExecutor"/>.
    /// Repositories and services should use the executor instead.
    /// </summary>
    internal interface IDbCommandFactory
    {
        DbCommand Create(
            DbConnection connection,
            string commandText,
            params (string Name, object? Value)[] parameters);
    }
}
