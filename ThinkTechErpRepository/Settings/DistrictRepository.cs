using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Settings;

// public.district: a division's districts, each with the shipping zone its areas use by default.
public sealed class DistrictRepository(IDbConnectionFactory connectionFactory) : IDistrictRepository
{
    private const string DistrictSelect = """
        SELECT d.id AS Id,
               d.object_id AS ObjectId,
               d.division_id AS DivisionId,
               v.name AS DivisionName,
               d.name AS Name,
               d.name_bn AS NameBn,
               d.shipping_zone_id AS ShippingZoneId,
               z.name AS ShippingZoneName,
               d.is_active AS IsActive,
               (SELECT count(*)::int FROM public.area a WHERE a.district_id = d.id) AS AreaCount,
               d.created_at AT TIME ZONE 'utc' AS CreatedAt,
               d.created_by AS CreatedBy,
               d.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               d.updated_by AS UpdatedBy
        FROM public.district d
        JOIN public.division v ON v.id = d.division_id
        LEFT JOIN public.shipping_zone z ON z.id = d.shipping_zone_id
        """;

    private const string DistrictFilter = """
        WHERE (CAST(@DivisionId AS smallint) IS NULL OR d.division_id = @DivisionId)
          AND (CAST(@ShippingZoneId AS smallint) IS NULL OR d.shipping_zone_id = @ShippingZoneId)
          AND (CAST(@Pattern AS text) IS NULL OR d.name ILIKE @Pattern OR d.name_bn ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR d.is_active = @IsActive)
        """;

    public async Task<PagedResult<District>> GetDistrictsAsync(
        short? divisionId, short? shippingZoneId, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            DivisionId = divisionId,
            ShippingZoneId = shippingZoneId,
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            IsActive = isActive,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.district d {DistrictFilter};
            {DistrictSelect} {DistrictFilter} ORDER BY v.name, d.name, d.id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<District>()).AsList();
        return new PagedResult<District> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<District?> GetDistrictAsync(short districtId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<District>(new CommandDefinition(
            $"{DistrictSelect} WHERE d.id = @DistrictId", new { DistrictId = districtId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DistrictNameExistsAsync(
        short divisionId, string name, short? excludeDistrictId, CancellationToken cancellationToken = default)
    {
        // district_division_id_name_key is case-sensitive; this check isn't.
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.district
                WHERE division_id = @DivisionId
                  AND lower(name) = lower(@Name)
                  AND (CAST(@ExcludeId AS smallint) IS NULL OR id <> @ExcludeId))
            """, new { DivisionId = divisionId, Name = name, ExcludeId = excludeDistrictId },
            cancellationToken: cancellationToken));
    }

    public async Task<short> CreateDistrictAsync(
        DistrictUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<short>(new CommandDefinition($"""
            INSERT INTO public.district
                (division_id, name, name_bn, shipping_zone_id, is_active, created_at, created_by)
            VALUES
                (@DivisionId, @Name, @NameBn, @ShippingZoneId, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, new
        {
            request.DivisionId,
            request.Name,
            request.NameBn,
            request.ShippingZoneId,
            request.IsActive,
            UserId = userId
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateDistrictAsync(
        short districtId, DistrictUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.district
            SET division_id = @DivisionId,
                name = @Name,
                name_bn = @NameBn,
                shipping_zone_id = @ShippingZoneId,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE id = @DistrictId
            """, new
        {
            DistrictId = districtId,
            request.DivisionId,
            request.Name,
            request.NameBn,
            request.ShippingZoneId,
            request.IsActive,
            UserId = userId
        }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteDistrictAsync(short districtId, CancellationToken cancellationToken = default)
    {
        // Areas, warehouses, supplier addresses and e-commerce orders keep it (NO ACTION foreign keys).
        await using var connection = connectionFactory.CreateConnection();
        try
        {
            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.district WHERE id = @DistrictId", new { DistrictId = districtId },
                cancellationToken: cancellationToken));
            return deleted > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.ForeignKeyViolation
                                                               or PostgresErrorCodes.RestrictViolation)
        {
            return DeleteOutcome.InUse;
        }
    }

    public async Task<IReadOnlyList<DivisionOption>> GetDivisionOptionsAsync(CancellationToken cancellationToken = default)
    {
        // Inactive ones too, flagged, so editing a district of an inactive division keeps its value.
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DivisionOption>(new CommandDefinition("""
            SELECT id AS Id, name AS Name, is_active AS IsActive
            FROM public.division
            ORDER BY name, id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ShippingZoneOption>> GetShippingZoneOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ShippingZoneOption>(new CommandDefinition("""
            SELECT id AS Id, code AS Code, name AS Name, is_active AS IsActive
            FROM public.shipping_zone
            ORDER BY id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
