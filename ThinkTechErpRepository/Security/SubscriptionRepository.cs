using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SubscriptionRepository(IDbConnectionFactory connectionFactory) : ISubscriptionRepository
{
    private const string SubscriptionSelect = """
        SELECT s.subscription_id AS Id,
               s.organization_id AS OrganizationId,
               o.organization_name_en AS OrganizationName,
               s.subscription_module_id AS SubscriptionModuleId,
               m.module_name AS ModuleName,
               s.subscription_type AS SubscriptionType,
               s.billing_period_type AS BillingPeriodType,
               s.billing_period_count AS BillingPeriodCount,
               s.subscription_start_date AS StartDate,
               s.subscription_end_date AS EndDate,
               s.currency_lookup_data_id AS CurrencyLookupDataId,
               c.lookup_data_name AS CurrencyName,
               s.local_exchange_rate AS LocalExchangeRate,
               s.subtotal_amount AS SubtotalAmount,
               s.discount_percentage AS DiscountPercentage,
               s.discount_amount AS DiscountAmount,
               s.vat_amount AS VatAmount,
               s.total_amount AS TotalAmount,
               s.status AS Status,
               s.created_at AT TIME ZONE 'utc' AS CreatedAt,
               s.created_by AS CreatedBy,
               s.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               s.updated_by AS UpdatedBy
        FROM security.subscription s
        JOIN security.organization o ON o.organization_id = s.organization_id
        JOIN security.subscription_module m ON m.subscription_module_id = s.subscription_module_id
        JOIN public.lookup_data c ON c.lookup_data_id = s.currency_lookup_data_id
        """;

    private const string SubscriptionFilterSql = """
        WHERE (CAST(@OrganizationId AS integer) IS NULL OR s.organization_id = @OrganizationId)
          AND (CAST(@SubscriptionModuleId AS integer) IS NULL OR s.subscription_module_id = @SubscriptionModuleId)
          AND (CAST(@Status AS text) IS NULL OR s.status = @Status)
          AND (CAST(@SubscriptionType AS text) IS NULL OR s.subscription_type = @SubscriptionType)
          AND (CAST(@Pattern AS text) IS NULL OR o.organization_name_en ILIKE @Pattern OR m.module_name ILIKE @Pattern)
        """;

    public async Task<PagedResult<Subscription>> GetSubscriptionsAsync(
        SubscriptionFilter filter, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            filter.OrganizationId,
            filter.SubscriptionModuleId,
            filter.Status,
            filter.SubscriptionType,
            Pattern = filter.Search is null ? null : SecurityDb.ContainsPattern(filter.Search),
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*)
            FROM security.subscription s
            JOIN security.organization o ON o.organization_id = s.organization_id
            JOIN security.subscription_module m ON m.subscription_module_id = s.subscription_module_id
            {SubscriptionFilterSql};
            {SubscriptionSelect} {SubscriptionFilterSql}
            ORDER BY s.subscription_start_date DESC, s.subscription_id DESC
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<Subscription>()).AsList();
        return new PagedResult<Subscription> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<Subscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<Subscription>(new CommandDefinition(
            $"{SubscriptionSelect} WHERE s.subscription_id = @SubscriptionId", new { SubscriptionId = subscriptionId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasOverlapAsync(
        int organizationId, int subscriptionModuleId, DateOnly startDate, DateOnly endDate,
        long? excludeSubscriptionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM security.subscription
                WHERE organization_id = @OrganizationId
                  AND subscription_module_id = @SubscriptionModuleId
                  AND status IN ('Pending', 'Active')
                  AND subscription_start_date <= @EndDate
                  AND subscription_end_date >= @StartDate
                  AND (CAST(@ExcludeId AS bigint) IS NULL OR subscription_id <> @ExcludeId))
            """, new
            {
                OrganizationId = organizationId,
                SubscriptionModuleId = subscriptionModuleId,
                StartDate = startDate,
                EndDate = endDate,
                ExcludeId = excludeSubscriptionId
            }, cancellationToken: cancellationToken));
    }

    public async Task<long> CreateSubscriptionAsync(
        SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("UserId", userId);

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO security.subscription (
                organization_id, subscription_module_id, subscription_type, billing_period_type, billing_period_count,
                subscription_start_date, subscription_end_date, currency_lookup_data_id, local_exchange_rate,
                subtotal_amount, discount_percentage, discount_amount, vat_amount, total_amount, status,
                created_at, created_by)
            VALUES (
                @OrganizationId, @SubscriptionModuleId, @SubscriptionType, @BillingPeriodType, @BillingPeriodCount,
                @StartDate, @EndDate, @CurrencyLookupDataId, @LocalExchangeRate,
                @SubtotalAmount, @DiscountPercentage, @DiscountAmount, @VatAmount,
                @SubtotalAmount - @DiscountAmount + @VatAmount, @Status,
                {SecurityDb.NowUtc}, @UserId)
            RETURNING subscription_id
            """, parameters, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateSubscriptionAsync(
        long subscriptionId, SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("SubscriptionId", subscriptionId);
        parameters.Add("UserId", userId);

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.subscription
            SET organization_id = @OrganizationId,
                subscription_module_id = @SubscriptionModuleId,
                subscription_type = @SubscriptionType,
                billing_period_type = @BillingPeriodType,
                billing_period_count = @BillingPeriodCount,
                subscription_start_date = @StartDate,
                subscription_end_date = @EndDate,
                currency_lookup_data_id = @CurrencyLookupDataId,
                local_exchange_rate = @LocalExchangeRate,
                subtotal_amount = @SubtotalAmount,
                discount_percentage = @DiscountPercentage,
                discount_amount = @DiscountAmount,
                vat_amount = @VatAmount,
                total_amount = @SubtotalAmount - @DiscountAmount + @VatAmount,
                status = @Status,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE subscription_id = @SubscriptionId
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> DeleteSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security.subscription WHERE subscription_id = @SubscriptionId",
            new { SubscriptionId = subscriptionId }, cancellationToken: cancellationToken)) > 0;
    }
}
