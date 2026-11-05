using System.Data.Common;
using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SubscriptionPlanRepository(IDbConnectionFactory connectionFactory) : ISubscriptionPlanRepository
{
    private const string PlanSelect = """
        SELECT p.id AS Id,
               p.object_id AS ObjectId,
               p.subscription_plan_name_en AS NameEn,
               p.subscription_plan_name_loc AS NameLocal,
               p.plan_short_note AS ShortNote,
               p.plan_regular_price AS RegularPrice,
               p.plan_discount_pct AS DiscountPercentage,
               p.plan_discount_price AS DiscountPrice,
               p.rf_currency_id AS CurrencyLookupDataId,
               coalesce(cur.lookup_data_name, '') AS CurrencyName,
               p.loc_exchange_rate AS LocalExchangeRate,
               p.billing_period_type AS BillingPeriodType,
               p.is_active AS IsActive,
               (SELECT count(*) FROM public.subscription_plan_feature_h f WHERE f.subscription_plan_id = p.id)::int AS FeatureCount,
               p.created_at AT TIME ZONE 'utc' AS CreatedAt,
               p.created_by AS CreatedBy,
               p.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               p.updated_by AS UpdatedBy
        FROM public.subscription_plan p
        LEFT JOIN public.lookup_data cur ON cur.lookup_data_id = p.rf_currency_id
        """;

    private const string PlanFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR p.subscription_plan_name_en ILIKE @Pattern
               OR p.subscription_plan_name_loc ILIKE @Pattern OR p.plan_short_note ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR p.is_active = @IsActive)
        """;

    public async Task<PagedResult<SubscriptionPlan>> GetPlansAsync(
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
            SELECT count(*) FROM public.subscription_plan p {PlanFilter};
            {PlanSelect} {PlanFilter}
            ORDER BY p.subscription_plan_name_en, p.id
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<SubscriptionPlan>()).AsList();
        await LoadFeaturesAsync(connection, items, cancellationToken);
        return new PagedResult<SubscriptionPlan>
        {
            Items = items,
            TotalCount = (int)totalCount,
            Limit = limit,
            Offset = offset
        };
    }

    public async Task<SubscriptionPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var plan = await connection.QuerySingleOrDefaultAsync<SubscriptionPlan>(new CommandDefinition(
            $"{PlanSelect} WHERE p.id = @PlanId", new { PlanId = planId }, cancellationToken: cancellationToken));
        if (plan is not null)
            await LoadFeaturesAsync(connection, [plan], cancellationToken);
        return plan;
    }

    public async Task<bool> PlanNameExistsAsync(string nameEn, int? excludePlanId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.subscription_plan
                           WHERE lower(subscription_plan_name_en) = lower(@NameEn)
                             AND (CAST(@ExcludePlanId AS int) IS NULL OR id <> @ExcludePlanId))
            """, new { NameEn = nameEn, ExcludePlanId = excludePlanId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreatePlanAsync(SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var planId = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.subscription_plan (
                subscription_plan_name_en, subscription_plan_name_loc, plan_short_note, plan_regular_price,
                plan_discount_pct, plan_discount_price, rf_currency_id, loc_exchange_rate, billing_period_type,
                is_active, created_at, created_by)
            VALUES (
                @NameEn, @NameLocal, @ShortNote, @RegularPrice,
                @DiscountPercentage, @DiscountPrice, @CurrencyLookupDataId, @LocalExchangeRate, @BillingPeriodType,
                @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, PlanParameters(request, userId), transaction, cancellationToken: cancellationToken));
        await SaveFeaturesAsync(connection, transaction, planId, request.Features, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    public async Task<bool> UpdatePlanAsync(
        int planId, SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = PlanParameters(request, userId);
        parameters.Add("PlanId", planId);
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.subscription_plan
            SET subscription_plan_name_en = @NameEn, subscription_plan_name_loc = @NameLocal,
                plan_short_note = @ShortNote, plan_regular_price = @RegularPrice,
                plan_discount_pct = @DiscountPercentage, plan_discount_price = @DiscountPrice,
                rf_currency_id = @CurrencyLookupDataId, loc_exchange_rate = @LocalExchangeRate,
                billing_period_type = @BillingPeriodType, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE id = @PlanId
            """, parameters, transaction, cancellationToken: cancellationToken));
        if (updated == 0)
            return false;

        await SaveFeaturesAsync(connection, transaction, planId, request.Features, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DeleteOutcome> DeletePlanAsync(int planId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        try
        {
            // Its features and their details go with it (ON DELETE CASCADE).
            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.subscription_plan WHERE id = @PlanId",
                new { PlanId = planId }, cancellationToken: cancellationToken));
            return deleted > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return DeleteOutcome.InUse;
        }
    }

    private static DynamicParameters PlanParameters(SubscriptionPlanUpsertRequest request, int userId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("NameEn", request.NameEn);
        parameters.Add("NameLocal", request.NameLocal);
        parameters.Add("ShortNote", request.ShortNote);
        parameters.Add("RegularPrice", request.RegularPrice);
        parameters.Add("DiscountPercentage", request.DiscountPercentage);
        parameters.Add("DiscountPrice", request.DiscountPrice ?? request.RegularPrice);
        parameters.Add("CurrencyLookupDataId", request.CurrencyLookupDataId);
        parameters.Add("LocalExchangeRate", request.LocalExchangeRate);
        parameters.Add("BillingPeriodType", request.BillingPeriodType);
        parameters.Add("IsActive", request.IsActive);
        parameters.Add("UserId", userId);
        return parameters;
    }

    /// <summary>
    /// Saved features and details (by id, within this plan) are updated in place, the others added; features and
    /// details no longer listed are removed (a feature's details go with it).
    /// </summary>
    private static async Task SaveFeaturesAsync(
        NpgsqlConnection connection, DbTransaction transaction, int planId,
        IReadOnlyList<SubscriptionPlanFeatureRequest> features, int userId, CancellationToken cancellationToken)
    {
        var keptFeatureIds = features.Where(f => f.Id is not null).Select(f => f.Id!.Value).ToArray();
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.subscription_plan_feature_h
            WHERE subscription_plan_id = @PlanId AND NOT (id = ANY(@Kept))
            """, new { PlanId = planId, Kept = keptFeatureIds }, transaction, cancellationToken: cancellationToken));

        foreach (var feature in features)
        {
            var featureParameters = new { PlanId = planId, feature.Id, feature.Title, feature.IsActive, UserId = userId };
            var existing = feature.Id is not null && await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.subscription_plan_feature_h
                SET feature_title = @Title, is_active = @IsActive, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE id = @Id AND subscription_plan_id = @PlanId
                """, featureParameters, transaction, cancellationToken: cancellationToken)) > 0;
            var featureId = existing
                ? feature.Id!.Value
                : await connection.QuerySingleAsync<int>(new CommandDefinition($"""
                    INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title, is_active, created_at, created_by)
                    VALUES (@PlanId, @Title, @IsActive, {SecurityDb.NowUtc}, @UserId)
                    RETURNING id
                    """, featureParameters, transaction, cancellationToken: cancellationToken));

            var keptDetailIds = existing ? feature.Details.Where(d => d.Id is not null).Select(d => d.Id!.Value).ToArray() : [];
            if (existing)
                await connection.ExecuteAsync(new CommandDefinition("""
                    DELETE FROM public.subscription_plan_feature_d
                    WHERE subscription_plan_feature_h_id = @FeatureId AND NOT (id = ANY(@Kept))
                    """, new { FeatureId = featureId, Kept = keptDetailIds }, transaction, cancellationToken: cancellationToken));

            foreach (var detail in feature.Details)
            {
                var detailParameters = new { FeatureId = featureId, detail.Id, detail.Description };
                var updated = existing && detail.Id is not null && await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE public.subscription_plan_feature_d SET feature_description = @Description
                    WHERE id = @Id AND subscription_plan_feature_h_id = @FeatureId
                    """, detailParameters, transaction, cancellationToken: cancellationToken)) > 0;
                if (!updated)
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
                        VALUES (@FeatureId, @Description)
                        """, detailParameters, transaction, cancellationToken: cancellationToken));
            }
        }
    }

    /// <summary>Fills each plan's features (in the order added) and their details.</summary>
    private static async Task LoadFeaturesAsync(
        NpgsqlConnection connection, IReadOnlyList<SubscriptionPlan> plans, CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
            return;

        var features = (await connection.QueryAsync<SubscriptionPlanFeature>(new CommandDefinition("""
            SELECT id AS Id, object_id AS ObjectId, subscription_plan_id AS SubscriptionPlanId,
                   feature_title AS Title, is_active AS IsActive
            FROM public.subscription_plan_feature_h
            WHERE subscription_plan_id = ANY(@PlanIds)
            ORDER BY subscription_plan_id, id
            """, new { PlanIds = plans.Select(p => p.Id).ToArray() }, cancellationToken: cancellationToken))).AsList();
        if (features.Count == 0)
            return;

        var details = (await connection.QueryAsync<SubscriptionPlanFeatureDetail>(new CommandDefinition("""
            SELECT id AS Id, object_id AS ObjectId, subscription_plan_feature_h_id AS FeatureId,
                   feature_description AS Description
            FROM public.subscription_plan_feature_d
            WHERE subscription_plan_feature_h_id = ANY(@FeatureIds)
            ORDER BY subscription_plan_feature_h_id, id
            """, new { FeatureIds = features.Select(f => f.Id).ToArray() }, cancellationToken: cancellationToken)))
            .ToLookup(d => d.FeatureId);

        var byPlan = features.ToLookup(f => f.SubscriptionPlanId);
        foreach (var feature in features)
            feature.Details = details[feature.Id].ToList();
        foreach (var plan in plans)
            plan.Features = byPlan[plan.Id].ToList();
    }
}
