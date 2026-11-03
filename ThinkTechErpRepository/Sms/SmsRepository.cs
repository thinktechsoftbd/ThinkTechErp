using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Sms;

public interface ISmsRepository
{
    Task<IReadOnlyList<SmsProvider>> GetProvidersAsync(bool? isActive, CancellationToken cancellationToken = default);
    Task<SmsProvider?> GetProviderAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Creates a provider; returns its id.</summary>
    Task<int> CreateProviderAsync(SmsProviderUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>Null ApiPassword/ApiKey keep the stored values; empty strings were turned into "remove" by the service.</summary>
    Task<bool> UpdateProviderAsync(int id, SmsProviderUpsertRequest request, bool clearPassword, bool clearKey, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteProviderAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<SmsPurchase>> GetPurchasesAsync(SmsListQuery query, CancellationToken cancellationToken = default);
    Task<SmsPurchase?> GetPurchaseAsync(long id, CancellationToken cancellationToken = default);
    Task<long> SavePurchaseAsync(long? id, SmsPurchase purchase, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePurchaseAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<SmsAllocation>> GetAllocationsAsync(SmsListQuery query, CancellationToken cancellationToken = default);
    Task<SmsAllocation?> GetAllocationAsync(long id, CancellationToken cancellationToken = default);
    Task<long> SaveAllocationAsync(long? id, SmsAllocationUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAllocationAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<SmsOrganizationBalance>> GetOrganizationBalancesAsync(string? search, bool onlyAllocated, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SmsOrganizationBalance?> GetOrganizationBalanceAsync(Guid objectId, CancellationToken cancellationToken = default);
    /// <summary>Adds SMS sent to the organization's usage, within what it has left; returns the new balance.</summary>
    Task<SmsOrganizationBalance> AddUsageAsync(Guid objectId, long smsCount, CancellationToken cancellationToken = default);
    /// <summary>Sets the organization's total usage (0 to what was allocated); returns the new balance.</summary>
    Task<SmsOrganizationBalance> SetUsageAsync(Guid objectId, long smsUsage, CancellationToken cancellationToken = default);
    /// <summary>Whether the integration client's acting user created the organization (it may use its SMS).</summary>
    Task<bool> IsOrganizationCreatedByAsync(Guid objectId, int userId, CancellationToken cancellationToken = default);

    Task<SmsOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrencyCodeAsync(int currencyId, string currencyLookupTable, CancellationToken cancellationToken = default);
}

public sealed class SmsRepository(IDbConnectionFactory connectionFactory) : ISmsRepository
{
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // =====================================================================
    // Providers
    // =====================================================================

    private const string ProviderSelect = """
        SELECT p.sms_provider_id AS Id, p.object_id AS ObjectId, p.provider_name AS Name, p.provider_code AS Code,
               p.api_base_url AS ApiBaseUrl, p.api_username AS ApiUsername,
               (p.api_password IS NOT NULL AND p.api_password <> '') AS HasApiPassword,
               (p.api_key IS NOT NULL AND p.api_key <> '') AS HasApiKey, p.is_active AS IsActive,
               coalesce(pur.purchased, 0) AS PurchasedSms, coalesce(alc.allocated, 0) AS AllocatedSms,
               coalesce(pur.purchased, 0) - coalesce(alc.allocated, 0) AS AvailableSms,
               p.created_at AT TIME ZONE 'utc' AS CreatedAt, p.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.sms_provider p
        LEFT JOIN (SELECT sms_provider_id, sum(sms_quantity)::bigint AS purchased FROM public.sms_purchase GROUP BY sms_provider_id) pur
               ON pur.sms_provider_id = p.sms_provider_id
        LEFT JOIN (SELECT sms_provider_id, sum(allocated_sms)::bigint AS allocated FROM public.sms_allocation GROUP BY sms_provider_id) alc
               ON alc.sms_provider_id = p.sms_provider_id
        """;

    public async Task<IReadOnlyList<SmsProvider>> GetProvidersAsync(bool? isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<SmsProvider>(new CommandDefinition($"""
            {ProviderSelect}
            WHERE CAST(@IsActive AS boolean) IS NULL OR p.is_active = @IsActive
            ORDER BY p.is_active DESC, p.provider_name
            """, new { IsActive = isActive }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<SmsProvider?> GetProviderAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SmsProvider>(new CommandDefinition(
            $"{ProviderSelect} WHERE p.sms_provider_id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateProviderAsync(SmsProviderUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.sms_provider (provider_name, provider_code, api_base_url, api_username, api_password, api_key,
                                          is_active, created_at, created_by)
            VALUES (@Name, @Code, @ApiBaseUrl, @ApiUsername, @ApiPassword, @ApiKey, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING sms_provider_id
            """, new
            {
                request.Name,
                request.Code,
                request.ApiBaseUrl,
                request.ApiUsername,
                request.ApiPassword,
                request.ApiKey,
                request.IsActive,
                UserId = userId
            }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateProviderAsync(
        int id, SmsProviderUpsertRequest request, bool clearPassword, bool clearKey, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        // A secret not sent keeps its stored value; one sent empty is removed.
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.sms_provider
            SET provider_name = @Name, provider_code = @Code, api_base_url = @ApiBaseUrl, api_username = @ApiUsername,
                api_password = CASE WHEN @ClearPassword THEN NULL ELSE coalesce(@ApiPassword, api_password) END,
                api_key = CASE WHEN @ClearKey THEN NULL ELSE coalesce(@ApiKey, api_key) END,
                is_active = @IsActive, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE sms_provider_id = @Id
            """, new
            {
                Id = id,
                request.Name,
                request.Code,
                request.ApiBaseUrl,
                request.ApiUsername,
                request.ApiPassword,
                request.ApiKey,
                ClearPassword = clearPassword,
                ClearKey = clearKey,
                request.IsActive,
                UserId = userId
            }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteProviderAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.sms_provider WHERE sms_provider_id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    /// <summary>Locks the providers (in id order) and returns what each one has bought and allocated.</summary>
    private static async Task<Dictionary<int, (long Purchased, long Allocated, bool IsActive, string Name)>> LockProvidersAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<int> providerIds, CancellationToken cancellationToken)
    {
        var ids = providerIds.Distinct().OrderBy(id => id).ToArray();
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT 1 FROM public.sms_provider WHERE sms_provider_id = ANY(@Ids) ORDER BY sms_provider_id FOR UPDATE",
            new { Ids = ids }, transaction, cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<(int Id, long Purchased, long Allocated, bool IsActive, string Name)>(new CommandDefinition("""
            SELECT p.sms_provider_id,
                   coalesce((SELECT sum(sms_quantity) FROM public.sms_purchase WHERE sms_provider_id = p.sms_provider_id), 0)::bigint,
                   coalesce((SELECT sum(allocated_sms) FROM public.sms_allocation WHERE sms_provider_id = p.sms_provider_id), 0)::bigint,
                   p.is_active, p.provider_name
            FROM public.sms_provider p WHERE p.sms_provider_id = ANY(@Ids)
            """, new { Ids = ids }, transaction, cancellationToken: cancellationToken));
        var result = rows.ToDictionary(r => r.Id, r => (r.Purchased, r.Allocated, r.IsActive, r.Name));
        if (result.Count != ids.Length)
            throw new BusinessRuleException("The SMS provider doesn't exist.");
        return result;
    }

    // =====================================================================
    // Purchases
    // =====================================================================

    private sealed class PurchaseRef
    {
        public int ProviderId { get; set; }
        public long Quantity { get; set; }
    }

    private const string PurchaseRefSql =
        "SELECT sms_provider_id AS ProviderId, sms_quantity AS Quantity FROM public.sms_purchase WHERE sms_purchase_id = @Id";

    private const string PurchaseSelect = """
        SELECT s.sms_purchase_id AS Id, s.object_id AS ObjectId, s.sms_provider_id AS ProviderId,
               p.provider_name AS ProviderName, p.provider_code AS ProviderCode, s.purchase_date AS PurchaseDate,
               s.invoice_number AS InvoiceNumber, s.reference_number AS ReferenceNumber, s.sms_quantity AS SmsQuantity,
               s.currency_id AS CurrencyId, cur.lookup_data_name AS Currency, s.unit_price AS UnitPrice,
               s.total_amount AS TotalAmount, s.local_exchange_rate AS LocalExchangeRate,
               s.local_total_amount AS LocalTotalAmount, s.remarks AS Remarks,
               s.created_at AT TIME ZONE 'utc' AS CreatedAt, s.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.sms_purchase s
        JOIN public.sms_provider p ON p.sms_provider_id = s.sms_provider_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = s.currency_id
        """;

    private const string PurchaseFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR s.invoice_number ILIKE @Pattern OR s.reference_number ILIKE @Pattern
               OR p.provider_name ILIKE @Pattern OR s.remarks ILIKE @Pattern)
          AND (CAST(@ProviderId AS int) IS NULL OR s.sms_provider_id = @ProviderId)
          AND (CAST(@From AS date) IS NULL OR s.purchase_date >= @From)
          AND (CAST(@To AS date) IS NULL OR s.purchase_date <= @To)
        """;

    private static object ListParams(SmsListQuery query) => new
    {
        Pattern = RepositoryHelpers.Pattern(query.Search),
        query.ProviderId,
        query.OrganizationObjectId,
        query.From,
        query.To
    };

    public async Task<PagedResult<SmsPurchase>> GetPurchasesAsync(SmsListQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<SmsPurchase>(connection,
            $"SELECT count(*) FROM public.sms_purchase s JOIN public.sms_provider p ON p.sms_provider_id = s.sms_provider_id {PurchaseFilter}",
            $"{PurchaseSelect} {PurchaseFilter} ORDER BY s.purchase_date DESC, s.sms_purchase_id DESC",
            ListParams(query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<SmsPurchase?> GetPurchaseAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SmsPurchase>(new CommandDefinition(
            $"{PurchaseSelect} WHERE s.sms_purchase_id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<long> SavePurchaseAsync(long? id, SmsPurchase purchase, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new
        {
            Id = id,
            purchase.ProviderId,
            purchase.PurchaseDate,
            purchase.InvoiceNumber,
            purchase.ReferenceNumber,
            purchase.SmsQuantity,
            purchase.CurrencyId,
            purchase.UnitPrice,
            purchase.TotalAmount,
            purchase.LocalExchangeRate,
            purchase.LocalTotalAmount,
            purchase.Remarks,
            UserId = userId
        };

        if (id is null)
        {
            await LockProvidersAsync(connection, transaction, [purchase.ProviderId], cancellationToken);
            var newId = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.sms_purchase (sms_provider_id, purchase_date, invoice_number, reference_number, sms_quantity,
                                              currency_id, unit_price, total_amount, local_exchange_rate, local_total_amount,
                                              remarks, created_at, created_by)
                VALUES (@ProviderId, @PurchaseDate, @InvoiceNumber, @ReferenceNumber, @SmsQuantity, @CurrencyId, @UnitPrice,
                        @TotalAmount, @LocalExchangeRate, @LocalTotalAmount, @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING sms_purchase_id
                """, parameters, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return newId;
        }

        var old = await connection.QuerySingleOrDefaultAsync<PurchaseRef>(new CommandDefinition(
            PurchaseRefSql, new { Id = id }, transaction, cancellationToken: cancellationToken))
            ?? throw new BusinessRuleException("The SMS purchase doesn't exist.");
        var providers = await LockProvidersAsync(connection, transaction, [old.ProviderId, purchase.ProviderId], cancellationToken);
        // Fewer SMS (or another provider) must still cover what was allocated from the old provider.
        var oldProvider = providers[old.ProviderId];
        var remaining = oldProvider.Purchased - old.Quantity + (old.ProviderId == purchase.ProviderId ? purchase.SmsQuantity : 0);
        if (remaining < oldProvider.Allocated)
            throw new BusinessRuleException(
                $"{oldProvider.Name} has {oldProvider.Allocated:N0} SMS allocated; this change would leave only {remaining:N0} bought.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.sms_purchase
            SET sms_provider_id = @ProviderId, purchase_date = @PurchaseDate, invoice_number = @InvoiceNumber,
                reference_number = @ReferenceNumber, sms_quantity = @SmsQuantity, currency_id = @CurrencyId,
                unit_price = @UnitPrice, total_amount = @TotalAmount, local_exchange_rate = @LocalExchangeRate,
                local_total_amount = @LocalTotalAmount, remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE sms_purchase_id = @Id
            """, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id.Value;
    }

    public async Task<DeleteOutcome> DeletePurchaseAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var old = await connection.QuerySingleOrDefaultAsync<PurchaseRef>(new CommandDefinition(
            PurchaseRefSql, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (old is null)
            return DeleteOutcome.NotFound;
        var provider = (await LockProvidersAsync(connection, transaction, [old.ProviderId], cancellationToken))[old.ProviderId];
        if (provider.Purchased - old.Quantity < provider.Allocated)
            return DeleteOutcome.InUse;
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM public.sms_purchase WHERE sms_purchase_id = @Id", new { Id = id }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return DeleteOutcome.Deleted;
    }

    // =====================================================================
    // Allocations
    // =====================================================================

    private const string AllocationSelect = """
        SELECT a.sms_allocation_id AS Id, a.object_id AS ObjectId, a.sms_provider_id AS ProviderId,
               p.provider_name AS ProviderName, p.provider_code AS ProviderCode, a.organization_id AS OrganizationObjectId,
               o.organization_id AS OrganizationId, o.organization_name_en AS OrganizationName,
               o.organization_code AS OrganizationCode, a.allocated_sms AS AllocatedSms, a.allocation_date AS AllocationDate,
               a.remarks AS Remarks, a.created_at AT TIME ZONE 'utc' AS CreatedAt, a.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.sms_allocation a
        JOIN public.sms_provider p ON p.sms_provider_id = a.sms_provider_id
        JOIN public.organization o ON o.object_id = a.organization_id
        """;

    private const string AllocationFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR o.organization_name_en ILIKE @Pattern OR o.organization_code ILIKE @Pattern
               OR p.provider_name ILIKE @Pattern OR a.remarks ILIKE @Pattern)
          AND (CAST(@ProviderId AS int) IS NULL OR a.sms_provider_id = @ProviderId)
          AND (CAST(@OrganizationObjectId AS uuid) IS NULL OR a.organization_id = @OrganizationObjectId)
          AND (CAST(@From AS date) IS NULL OR a.allocation_date >= @From)
          AND (CAST(@To AS date) IS NULL OR a.allocation_date <= @To)
        """;

    public async Task<PagedResult<SmsAllocation>> GetAllocationsAsync(SmsListQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<SmsAllocation>(connection, $"""
            SELECT count(*) FROM public.sms_allocation a
            JOIN public.sms_provider p ON p.sms_provider_id = a.sms_provider_id
            JOIN public.organization o ON o.object_id = a.organization_id
            {AllocationFilter}
            """,
            $"{AllocationSelect} {AllocationFilter} ORDER BY a.allocation_date DESC, a.sms_allocation_id DESC",
            ListParams(query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<SmsAllocation?> GetAllocationAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SmsAllocation>(new CommandDefinition(
            $"{AllocationSelect} WHERE a.sms_allocation_id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    private sealed class AllocationRef
    {
        public int ProviderId { get; set; }
        public Guid OrganizationId { get; set; }
        public long Quantity { get; set; }
    }

    private const string AllocationRefSql = """
        SELECT sms_provider_id AS ProviderId, organization_id AS OrganizationId, allocated_sms AS Quantity
        FROM public.sms_allocation WHERE sms_allocation_id = @Id
        """;

    private sealed class LockedOrganization
    {
        public Guid ObjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsFinalized { get; set; }
        public long Allocated { get; set; }
        public long Usage { get; set; }
    }

    /// <summary>A finalized organization's allocations are locked: none is added, changed, moved or deleted.</summary>
    private static void EnsureNotFinalized(LockedOrganization organization)
    {
        if (organization.IsFinalized)
            throw new BusinessRuleException(
                $"{organization.Name} is finalized, so its SMS allocations are locked. Clear Finalized in Security → Organizations to change them.");
    }

    /// <summary>Locks the organizations (by Object Id, in order) and returns what each was allocated and used.</summary>
    private static async Task<Dictionary<Guid, LockedOrganization>> LockOrganizationsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<Guid> objectIds, CancellationToken cancellationToken)
    {
        var ids = objectIds.Distinct().OrderBy(id => id).ToArray();
        var rows = (await connection.QueryAsync<LockedOrganization>(new CommandDefinition("""
            SELECT o.object_id AS ObjectId, o.organization_name_en AS Name, o.is_active AS IsActive,
                   o.is_finalized AS IsFinalized, o.sms_usage AS Usage,
                   coalesce((SELECT sum(a.allocated_sms) FROM public.sms_allocation a WHERE a.organization_id = o.object_id), 0)::bigint AS Allocated
            FROM public.organization o
            WHERE o.object_id = ANY(@Ids)
            ORDER BY o.object_id
            FOR UPDATE OF o
            """, new { Ids = ids }, transaction, cancellationToken: cancellationToken))).ToDictionary(o => o.ObjectId);
        if (rows.Count != ids.Length)
            throw new BusinessRuleException("The organization doesn't exist.");
        return rows;
    }

    public async Task<long> SaveAllocationAsync(long? id, SmsAllocationUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        AllocationRef? old = null;
        if (id is not null)
            old = await connection.QuerySingleOrDefaultAsync<AllocationRef>(new CommandDefinition(
                AllocationRefSql, new { Id = id }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessRuleException("The SMS allocation doesn't exist.");

        // Providers first, then organizations, each in a fixed order, so concurrent changes can't deadlock.
        var providers = await LockProvidersAsync(connection, transaction,
            old is null ? [request.ProviderId] : [request.ProviderId, old.ProviderId], cancellationToken);
        var organizations = await LockOrganizationsAsync(connection, transaction,
            old is null ? [request.OrganizationObjectId] : [request.OrganizationObjectId, old.OrganizationId], cancellationToken);

        var provider = providers[request.ProviderId];
        var organization = organizations[request.OrganizationObjectId];
        // Neither the organization it goes to nor (when changed) the one it is on may be finalized.
        EnsureNotFinalized(organization);
        if (old is not null)
            EnsureNotFinalized(organizations[old.OrganizationId]);
        var sameProvider = old is not null && old.ProviderId == request.ProviderId;
        var sameOrganization = old is not null && old.OrganizationId == request.OrganizationObjectId;
        if (!provider.IsActive && !sameProvider)
            throw new BusinessRuleException($"{provider.Name} is inactive. Activate it to allocate its SMS.");
        if (!organization.IsActive && !sameOrganization)
            throw new BusinessRuleException($"{organization.Name} is inactive.");

        var available = provider.Purchased - provider.Allocated + (sameProvider ? old!.Quantity : 0);
        if (request.AllocatedSms > available)
            throw new BusinessRuleException($"{provider.Name} has only {available:N0} SMS left to allocate.");

        // Taking SMS back must leave the organization at least what it already used.
        if (old is not null)
        {
            var before = organizations[old.OrganizationId];
            var after = before.Allocated - old.Quantity + (sameOrganization ? request.AllocatedSms : 0);
            if (after < before.Usage)
                throw new BusinessRuleException(
                    $"{before.Name} has already used {before.Usage:N0} SMS; its allocations can't drop to {after:N0}.");
        }

        var parameters = new
        {
            Id = id,
            request.ProviderId,
            OrganizationId = request.OrganizationObjectId,
            request.AllocatedSms,
            request.AllocationDate,
            request.Remarks,
            UserId = userId
        };
        long savedId;
        if (id is null)
            savedId = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.sms_allocation (sms_provider_id, organization_id, allocated_sms, allocation_date, remarks,
                                                created_at, created_by)
                VALUES (@ProviderId, @OrganizationId, @AllocatedSms, @AllocationDate, @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING sms_allocation_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        else
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.sms_allocation
                SET sms_provider_id = @ProviderId, organization_id = @OrganizationId, allocated_sms = @AllocatedSms,
                    allocation_date = @AllocationDate, remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE sms_allocation_id = @Id
                """, parameters, transaction, cancellationToken: cancellationToken));
            savedId = id.Value;
        }

        await transaction.CommitAsync(cancellationToken);
        return savedId;
    }

    public async Task<DeleteOutcome> DeleteAllocationAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var old = await connection.QuerySingleOrDefaultAsync<AllocationRef>(new CommandDefinition(
            AllocationRefSql, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (old is null)
            return DeleteOutcome.NotFound;
        await LockProvidersAsync(connection, transaction, [old.ProviderId], cancellationToken);
        var organization = (await LockOrganizationsAsync(connection, transaction, [old.OrganizationId], cancellationToken))[old.OrganizationId];
        EnsureNotFinalized(organization);
        if (organization.Allocated - old.Quantity < organization.Usage)
            return DeleteOutcome.InUse;
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM public.sms_allocation WHERE sms_allocation_id = @Id", new { Id = id }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return DeleteOutcome.Deleted;
    }

    // =====================================================================
    // Organization balances and usage
    // =====================================================================

    private const string BalanceRows = """
        SELECT o.organization_id AS OrganizationId, o.object_id AS ObjectId, o.organization_name_en AS Name,
               o.organization_code AS Code, o.is_active AS IsActive, o.is_finalized AS IsFinalized,
               coalesce(a.allocated, 0) AS AllocatedSms, o.sms_usage AS SmsUsage,
               coalesce(a.allocated, 0) - o.sms_usage AS AvailableSms,
               coalesce(a.allocation_count, 0) AS AllocationCount, a.last_allocation_date AS LastAllocationDate
        FROM public.organization o
        LEFT JOIN (SELECT organization_id, sum(allocated_sms)::bigint AS allocated, count(*)::int AS allocation_count,
                          max(allocation_date) AS last_allocation_date
                   FROM public.sms_allocation GROUP BY organization_id) a ON a.organization_id = o.object_id
        """;

    private const string BalanceFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR o.organization_name_en ILIKE @Pattern OR o.organization_code ILIKE @Pattern)
          AND (NOT @OnlyAllocated OR a.allocated IS NOT NULL OR o.sms_usage > 0)
        """;

    public async Task<PagedResult<SmsOrganizationBalance>> GetOrganizationBalancesAsync(
        string? search, bool onlyAllocated, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<SmsOrganizationBalance>(connection, $"""
            SELECT count(*) FROM public.organization o
            LEFT JOIN (SELECT organization_id, sum(allocated_sms)::bigint AS allocated FROM public.sms_allocation GROUP BY organization_id) a
                   ON a.organization_id = o.object_id
            {BalanceFilter}
            """,
            $"{BalanceRows} {BalanceFilter} ORDER BY o.is_active DESC, o.organization_name_en",
            new { Pattern = RepositoryHelpers.Pattern(search), OnlyAllocated = onlyAllocated }, limit, offset, cancellationToken);
    }

    public async Task<SmsOrganizationBalance?> GetOrganizationBalanceAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SmsOrganizationBalance>(new CommandDefinition(
            $"{BalanceRows} WHERE o.object_id = @ObjectId", new { ObjectId = objectId }, cancellationToken: cancellationToken));
    }

    public async Task<SmsOrganizationBalance> AddUsageAsync(Guid objectId, long smsCount, CancellationToken cancellationToken = default)
    {
        await ChangeUsageAsync(objectId, organization =>
        {
            var available = organization.Allocated - organization.Usage;
            if (smsCount > available)
                throw new BusinessConflictException($"Not enough SMS: {organization.Name} has {Math.Max(0, available):N0} left, {smsCount:N0} were sent.");
            return organization.Usage + smsCount;
        }, cancellationToken);
        return (await GetOrganizationBalanceAsync(objectId, cancellationToken))!;
    }

    public async Task<SmsOrganizationBalance> SetUsageAsync(Guid objectId, long smsUsage, CancellationToken cancellationToken = default)
    {
        await ChangeUsageAsync(objectId, organization =>
        {
            if (smsUsage > organization.Allocated)
                throw new BusinessRuleException($"{organization.Name} was allocated {organization.Allocated:N0} SMS; usage can't be more.");
            return smsUsage;
        }, cancellationToken);
        return (await GetOrganizationBalanceAsync(objectId, cancellationToken))!;
    }

    private async Task ChangeUsageAsync(Guid objectId, Func<LockedOrganization, long> newUsage, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var organization = (await LockOrganizationsAsync(connection, transaction, [objectId], cancellationToken))[objectId];
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE public.organization SET sms_usage = @Usage WHERE object_id = @ObjectId",
            new { ObjectId = objectId, Usage = newUsage(organization) }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> IsOrganizationCreatedByAsync(Guid objectId, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.organization WHERE object_id = @ObjectId AND created_by = @UserId)",
            new { ObjectId = objectId, UserId = userId }, cancellationToken: cancellationToken));
    }

    // =====================================================================
    // Overview
    // =====================================================================

    public async Task<SmsOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var providers = (await connection.QueryAsync<SmsProvider>(new CommandDefinition(
            $"{ProviderSelect} ORDER BY p.is_active DESC, p.provider_name", cancellationToken: cancellationToken))).AsList();
        var totals = await connection.QuerySingleAsync<(long Used, int Organizations, decimal Spend)>(new CommandDefinition("""
            SELECT coalesce(sum(o.sms_usage), 0)::bigint,
                   (SELECT count(DISTINCT organization_id) FROM public.sms_allocation)::int,
                   coalesce((SELECT sum(local_total_amount) FROM public.sms_purchase), 0)
            FROM public.organization o
            """, cancellationToken: cancellationToken));
        var lowest = (await connection.QueryAsync<SmsOrganizationBalance>(new CommandDefinition($"""
            {BalanceRows}
            WHERE a.allocated IS NOT NULL
            ORDER BY coalesce(a.allocated, 0) - o.sms_usage, o.organization_name_en
            LIMIT 5
            """, cancellationToken: cancellationToken))).AsList();

        var purchased = providers.Sum(p => p.PurchasedSms);
        var allocated = providers.Sum(p => p.AllocatedSms);
        return new SmsOverview
        {
            ProviderCount = providers.Count,
            ActiveProviderCount = providers.Count(p => p.IsActive),
            PurchasedSms = purchased,
            AllocatedSms = allocated,
            UnallocatedSms = purchased - allocated,
            UsedSms = totals.Used,
            OrganizationAvailableSms = allocated - totals.Used,
            OrganizationCount = totals.Organizations,
            PurchaseSpend = totals.Spend,
            Providers = providers,
            LowestBalances = lowest
        };
    }

    public async Task<string?> GetCurrencyCodeAsync(int currencyId, string currencyLookupTable, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT d.lookup_data_name FROM public.lookup_data d JOIN public.lookup_table t USING (lookup_table_id)
            WHERE d.lookup_data_id = @Id AND lower(t.lookup_table_name) = lower(@Table)
            """, new { Id = currencyId, Table = currencyLookupTable }, cancellationToken: cancellationToken));
    }
}
