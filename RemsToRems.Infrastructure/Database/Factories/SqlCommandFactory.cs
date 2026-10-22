using System.Data.Common;
using RemsToRems.Infrastructure.Database.Abstractions;

namespace RemsToRems.Infrastructure.Database.Factories
{
    internal sealed class SqlCommandFactory : IDbCommandFactory
    {
        public DbCommand Create(
            DbConnection connection,
            string commandText,
            params (string Name, object? Value)[] parameters)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

            var command = connection.CreateCommand();
            command.CommandText = commandText;

            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            return command;
        }
    }
}
