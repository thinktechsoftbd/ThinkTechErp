using Npgsql;

namespace ThinkTechErpRepository.Database;

public sealed class NpgsqlConnectionFactory(
    IReadOnlyDictionary<string, string> connectionStrings) : IDbConnectionFactory
{
    public NpgsqlConnection CreateConnection(string schema)
    {
        if (!connectionStrings.TryGetValue(schema, out var connectionString))
        {
            throw new ArgumentException($"No PostgreSQL connection is configured for schema '{schema}'.", nameof(schema));
        }

        return new NpgsqlConnection(connectionString);
    }
}
