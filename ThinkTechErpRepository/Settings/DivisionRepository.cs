using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Settings;

// public.division: the eight divisions of Bangladesh, the parents of public.district.
public sealed class DivisionRepository(IDbConnectionFactory connectionFactory) : IDivisionRepository
{
    private const string DivisionSelect = """
        SELECT v.id AS Id,
               v.object_id AS ObjectId,
               v.name AS Name,
               v.name_bn AS NameBn,
               v.is_active AS IsActive,
               (SELECT count(*)::int FROM public.district d WHERE d.division_id = v.id) AS DistrictCount,
               v.created_at AT TIME ZONE 'utc' AS CreatedAt,
               v.created_by AS CreatedBy,
               v.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               v.updated_by AS UpdatedBy
        FROM public.division v
        """;

    private const string DivisionFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR v.name ILIKE @Pattern OR v.name_bn ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR v.is_active = @IsActive)
        """;

    public async Task<PagedResult<Division>> GetDivisionsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            IsActive = isActive,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.division v {DivisionFilter};
            {DivisionSelect} {DivisionFilter} ORDER BY v.name, v.id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<Division>()).AsList();
        return new PagedResult<Division> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<Division?> GetDivisionAsync(short divisionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Division>(new CommandDefinition(
            $"{DivisionSelect} WHERE v.id = @DivisionId", new { DivisionId = divisionId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DivisionNameExistsAsync(
        string name, short? excludeDivisionId, CancellationToken cancellationToken = default)
    {
        // division_name_key is case-sensitive; this check isn't, so "Dhaka" and "dhaka" aren't both added.
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.division
                WHERE lower(name) = lower(@Name)
                  AND (CAST(@ExcludeId AS smallint) IS NULL OR id <> @ExcludeId))
            """, new { Name = name, ExcludeId = excludeDivisionId }, cancellationToken: cancellationToken));
    }

    public async Task<short> CreateDivisionAsync(
        DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<short>(new CommandDefinition($"""
            INSERT INTO public.division (name, name_bn, is_active, created_at, created_by)
            VALUES (@Name, @NameBn, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, new { request.Name, request.NameBn, request.IsActive, UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateDivisionAsync(
        short divisionId, DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.division
            SET name = @Name,
                name_bn = @NameBn,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE id = @DivisionId
            """, new { DivisionId = divisionId, request.Name, request.NameBn, request.IsActive, UserId = userId },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteDivisionAsync(short divisionId, CancellationToken cancellationToken = default)
    {
        // Its districts keep it (district_division_id_fkey is NO ACTION).
        await using var connection = connectionFactory.CreateConnection();
        try
        {
            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.division WHERE id = @DivisionId", new { DivisionId = divisionId },
                cancellationToken: cancellationToken));
            return deleted > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.ForeignKeyViolation
                                                               or PostgresErrorCodes.RestrictViolation)
        {
            return DeleteOutcome.InUse;
        }
    }
}
