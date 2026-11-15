using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Settings;

// public.shipping_zone: delivery zones (inside Dhaka, suburbs, outside Dhaka, ...) that districts and
// areas point to and shipping rates are priced by. Unlike division and district it has timestamptz
// created_at/updated_at and no created_by/updated_by columns.
public sealed class ShippingZoneRepository(IDbConnectionFactory connectionFactory) : IShippingZoneRepository
{
    private const string ShippingZoneSelect = """
        SELECT z.id AS Id,
               z.object_id AS ObjectId,
               z.code AS Code,
               z.name AS Name,
               z.min_delivery_days AS MinDeliveryDays,
               z.max_delivery_days AS MaxDeliveryDays,
               z.is_active AS IsActive,
               (SELECT count(*)::int FROM public.district d WHERE d.shipping_zone_id = z.id) AS DistrictCount,
               (SELECT count(*)::int FROM public.area a WHERE a.shipping_zone_id = z.id) AS AreaCount,
               z.created_at AS CreatedAt,
               z.updated_at AS UpdatedAt
        FROM public.shipping_zone z
        """;

    private const string ShippingZoneFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR z.code ILIKE @Pattern OR z.name ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR z.is_active = @IsActive)
        """;

    public async Task<PagedResult<ShippingZone>> GetShippingZonesAsync(
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
            SELECT count(*) FROM public.shipping_zone z {ShippingZoneFilter};
            {ShippingZoneSelect} {ShippingZoneFilter} ORDER BY z.id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<ShippingZone>()).AsList();
        return new PagedResult<ShippingZone> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<ShippingZone?> GetShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ShippingZone>(new CommandDefinition(
            $"{ShippingZoneSelect} WHERE z.id = @ShippingZoneId", new { ShippingZoneId = shippingZoneId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ShippingZoneCodeExistsAsync(
        string code, short? excludeShippingZoneId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.shipping_zone
                WHERE lower(code) = lower(@Code)
                  AND (CAST(@ExcludeId AS smallint) IS NULL OR id <> @ExcludeId))
            """, new { Code = code, ExcludeId = excludeShippingZoneId }, cancellationToken: cancellationToken));
    }

    public async Task<short> CreateShippingZoneAsync(
        ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<short>(new CommandDefinition("""
            INSERT INTO public.shipping_zone (code, name, min_delivery_days, max_delivery_days, is_active)
            VALUES (@Code, @Name, @MinDeliveryDays, @MaxDeliveryDays, @IsActive)
            RETURNING id
            """, new
        {
            request.Code,
            request.Name,
            request.MinDeliveryDays,
            request.MaxDeliveryDays,
            request.IsActive
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateShippingZoneAsync(
        short shippingZoneId, ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.shipping_zone
            SET code = @Code,
                name = @Name,
                min_delivery_days = @MinDeliveryDays,
                max_delivery_days = @MaxDeliveryDays,
                is_active = @IsActive,
                updated_at = now()
            WHERE id = @ShippingZoneId
            """, new
        {
            ShippingZoneId = shippingZoneId,
            request.Code,
            request.Name,
            request.MinDeliveryDays,
            request.MaxDeliveryDays,
            request.IsActive
        }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default)
    {
        // Districts, areas, shipping rates and e-commerce orders keep it (NO ACTION foreign keys).
        await using var connection = connectionFactory.CreateConnection();
        try
        {
            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.shipping_zone WHERE id = @ShippingZoneId", new { ShippingZoneId = shippingZoneId },
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
