using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Settings;

// public.district and public.area live in the same database as the security schema. Every query
// names the schema, so the connection's search path doesn't matter.
public sealed class AreaRepository(IDbConnectionFactory connectionFactory) : IAreaRepository
{
    private const string AreaSelect = """
        SELECT a.id AS Id,
               a.object_id AS ObjectId,
               a.district_id AS DistrictId,
               d.name AS DistrictName,
               d.division_id AS DivisionId,
               v.name AS DivisionName,
               a.name AS Name,
               a.name_bn AS NameBn,
               a.area_type AS AreaType,
               a.shipping_zone_id AS ShippingZoneId,
               z.name AS ShippingZoneName,
               a.is_active AS IsActive,
               a.created_at AT TIME ZONE 'utc' AS CreatedAt,
               a.created_by AS CreatedBy,
               a.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               a.updated_by AS UpdatedBy
        FROM public.area a
        JOIN public.district d ON d.id = a.district_id
        JOIN public.division v ON v.id = d.division_id
        LEFT JOIN public.shipping_zone z ON z.id = a.shipping_zone_id
        """;

    private const string AreaFilter = """
        WHERE (CAST(@DistrictId AS smallint) IS NULL OR a.district_id = @DistrictId)
          AND (CAST(@AreaType AS text) IS NULL OR a.area_type = @AreaType)
          AND (CAST(@Pattern AS text) IS NULL OR a.name ILIKE @Pattern OR a.name_bn ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR a.is_active = @IsActive)
        """;

    private const string DistrictSelect = """
        SELECT d.id AS Id,
               d.name AS Name,
               v.name AS DivisionName,
               d.shipping_zone_id AS ShippingZoneId,
               z.name AS ShippingZoneName
        FROM public.district d
        JOIN public.division v ON v.id = d.division_id
        LEFT JOIN public.shipping_zone z ON z.id = d.shipping_zone_id
        """;

    public async Task<PagedResult<Area>> GetAreasAsync(
        short? districtId, string? areaType, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            DistrictId = districtId,
            AreaType = areaType,
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            IsActive = isActive,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.area a {AreaFilter};
            {AreaSelect} {AreaFilter} ORDER BY d.name, a.name, a.id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<Area>()).AsList();
        return new PagedResult<Area> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<Area?> GetAreaAsync(int areaId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Area>(new CommandDefinition(
            $"{AreaSelect} WHERE a.id = @AreaId", new { AreaId = areaId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> AreaNameExistsAsync(
        short districtId, string name, int? excludeAreaId, CancellationToken cancellationToken = default)
    {
        // public.area's unique key is (district_id, name), compared case-sensitively by the database;
        // this check is case-insensitive so "Savar" and "savar" aren't both added by mistake.
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.area
                WHERE district_id = @DistrictId
                  AND lower(name) = lower(@Name)
                  AND (CAST(@ExcludeId AS integer) IS NULL OR id <> @ExcludeId))
            """, new { DistrictId = districtId, Name = name, ExcludeId = excludeAreaId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreateAreaAsync(
        AreaUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.area
                (district_id, name, name_bn, area_type, shipping_zone_id, is_active, created_at, created_by)
            VALUES
                (@DistrictId, @Name, @NameBn, @AreaType, @ShippingZoneId, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, new
        {
            request.DistrictId,
            request.Name,
            request.NameBn,
            request.AreaType,
            request.ShippingZoneId,
            request.IsActive,
            UserId = userId
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAreaAsync(
        int areaId, AreaUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.area
            SET district_id = @DistrictId,
                name = @Name,
                name_bn = @NameBn,
                area_type = @AreaType,
                shipping_zone_id = @ShippingZoneId,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE id = @AreaId
            """, new
        {
            AreaId = areaId,
            request.DistrictId,
            request.Name,
            request.NameBn,
            request.AreaType,
            request.ShippingZoneId,
            request.IsActive,
            UserId = userId
        }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteAreaAsync(int areaId, CancellationToken cancellationToken = default)
    {
        // A supplier address or an e-commerce order that uses the area keeps it (NO ACTION foreign keys).
        await using var connection = connectionFactory.CreateConnection();
        try
        {
            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.area WHERE id = @AreaId", new { AreaId = areaId },
                cancellationToken: cancellationToken));
            return deleted > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.ForeignKeyViolation
                                                               or PostgresErrorCodes.RestrictViolation)
        {
            return DeleteOutcome.InUse;
        }
    }

    public async Task<PagedResult<DistrictOption>> GetDistrictsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.district d
            WHERE CAST(@Pattern AS text) IS NULL OR d.name ILIKE @Pattern OR d.name_bn ILIKE @Pattern;
            {DistrictSelect}
            WHERE CAST(@Pattern AS text) IS NULL OR d.name ILIKE @Pattern OR d.name_bn ILIKE @Pattern
            ORDER BY d.name, d.id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<DistrictOption>()).AsList();
        return new PagedResult<DistrictOption> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<IReadOnlyList<ShippingZoneOption>> GetShippingZonesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ShippingZoneOption>(new CommandDefinition("""
            SELECT id AS Id, code AS Code, name AS Name
            FROM public.shipping_zone
            WHERE is_active
            ORDER BY id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
