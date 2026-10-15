using Npgsql;

namespace ThinkTechErpRepository.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection(string schema);
}
