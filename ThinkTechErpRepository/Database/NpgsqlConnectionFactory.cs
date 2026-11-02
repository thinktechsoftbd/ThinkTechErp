using Npgsql;

namespace ThinkTechErpRepository.Database;

// One data source (and so one connection pool) for every schema: all SQL names its schema.
public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory, IDisposable
{
    private readonly NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);

    public NpgsqlConnection CreateConnection() => dataSource.CreateConnection();

    public void Dispose() => dataSource.Dispose();
}
