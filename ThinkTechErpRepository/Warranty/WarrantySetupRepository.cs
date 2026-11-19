using Dapper;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Warranty;

public interface IWarrantySetupRepository
{
    /// <param name="sort">name, period, products, status or updated; null = period, then name.</param>
    Task<PagedResult<WarrantyPolicy>> GetPoliciesAsync(int organizationId, string? search, bool? isActive, string? sort, bool descending, int limit, int offset, CancellationToken cancellationToken = default);
    Task<WarrantyPolicy?> GetPolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WarrantyPolicyOption>> GetPolicyOptionsAsync(int organizationId, bool includeInactive, CancellationToken cancellationToken = default);
    Task<int> CreatePolicyAsync(int organizationId, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdatePolicyAsync(int organizationId, int id, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default);

    Task<PagedResult<ProductWarrantyItem>> GetProductsAsync(int organizationId, ProductWarrantyQuery query, CancellationToken cancellationToken = default);
    /// <summary>The product's warranty policy id; Exists is false when the product isn't this organization's.</summary>
    Task<(bool Exists, int? PolicyId)> GetProductPolicyAsync(int organizationId, int productId, CancellationToken cancellationToken = default);
    /// <summary>Sets (or clears) the policy of these products; returns how many were changed.</summary>
    Task<int> AssignPolicyAsync(int organizationId, IReadOnlyCollection<int> productIds, int? policyId, int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OptionItem>> GetDeviceTypesAsync(CancellationToken cancellationToken = default);
    Task<bool> DeviceTypeExistsAsync(long lookupDataId, CancellationToken cancellationToken = default);
    /// <summary>Active users who work in the organization, for the technician picker.</summary>
    Task<IReadOnlyList<OptionItem>> GetTechniciansAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed class WarrantySetupRepository(IDbConnectionFactory connectionFactory) : IWarrantySetupRepository
{
    private const string PolicySelect = """
        SELECT p.warranty_policy_id AS Id, p.policy_name AS Name, p.warranty_months AS WarrantyMonths,
               p.replacement_days AS ReplacementDays, p.covers_parts AS CoversParts, p.covers_labor AS CoversLabor,
               p.terms AS Terms, p.is_active AS IsActive,
               (SELECT count(*) FROM public.product pr WHERE pr.warranty_policy_id = p.warranty_policy_id)::int AS ProductCount,
               p.created_at AT TIME ZONE 'utc' AS CreatedAt, p.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.warranty_policy p
        """;

    private const string PolicyFilter = """
        WHERE p.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR p.policy_name ILIKE @Pattern OR p.terms ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR p.is_active = @IsActive)
        """;

    // The columns the policy list may be sorted by (never interpolate anything else into ORDER BY).
    private static readonly Dictionary<string, string> PolicySorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "lower(p.policy_name)",
        ["period"] = "p.warranty_months",
        ["products"] = "(SELECT count(*) FROM public.product pr WHERE pr.warranty_policy_id = p.warranty_policy_id)",
        ["status"] = "p.is_active",
        ["updated"] = "coalesce(p.updated_at, p.created_at)"
    };

    public async Task<PagedResult<WarrantyPolicy>> GetPoliciesAsync(
        int organizationId, string? search, bool? isActive, string? sort, bool descending, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var order = sort is not null && PolicySorts.TryGetValue(sort, out var column)
            ? $"{column} {(descending ? "DESC" : "ASC")}, lower(p.policy_name), p.warranty_policy_id"
            : "p.warranty_months, p.policy_name";
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<WarrantyPolicy>(connection,
            $"SELECT count(*) FROM public.warranty_policy p {PolicyFilter}",
            $"{PolicySelect} {PolicyFilter} ORDER BY {order}",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive },
            limit, offset, cancellationToken);
    }

    public async Task<WarrantyPolicy?> GetPolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<WarrantyPolicy>(new CommandDefinition(
            $"{PolicySelect} WHERE p.organization_id = @Org AND p.warranty_policy_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<WarrantyPolicyOption>> GetPolicyOptionsAsync(int organizationId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<WarrantyPolicyOption>(new CommandDefinition("""
            SELECT warranty_policy_id AS Id, policy_name AS Name, warranty_months AS WarrantyMonths,
                   replacement_days AS ReplacementDays, is_active AS IsActive
            FROM public.warranty_policy
            WHERE organization_id = @Org AND (@IncludeInactive OR is_active)
            ORDER BY warranty_months, policy_name
            """, new { Org = organizationId, IncludeInactive = includeInactive }, cancellationToken: cancellationToken))).AsList();
    }

    private static object PolicyParams(int organizationId, WarrantyPolicyUpsertRequest request, int userId, int id = 0) => new
    {
        Org = organizationId,
        Id = id,
        request.Name,
        request.WarrantyMonths,
        request.ReplacementDays,
        request.CoversParts,
        request.CoversLabor,
        request.Terms,
        request.IsActive,
        UserId = userId
    };

    public async Task<int> CreatePolicyAsync(int organizationId, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.warranty_policy (organization_id, policy_name, warranty_months, replacement_days, covers_parts,
                                                covers_labor, terms, is_active, created_at, created_by)
            VALUES (@Org, @Name, @WarrantyMonths, @ReplacementDays, @CoversParts, @CoversLabor, @Terms, @IsActive,
                    {SecurityDb.NowUtc}, @UserId)
            RETURNING warranty_policy_id
            """, PolicyParams(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdatePolicyAsync(int organizationId, int id, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.warranty_policy
            SET policy_name = @Name, warranty_months = @WarrantyMonths, replacement_days = @ReplacementDays,
                covers_parts = @CoversParts, covers_labor = @CoversLabor, terms = @Terms, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND warranty_policy_id = @Id
            """, PolicyParams(organizationId, request, userId, id), cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeletePolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.warranty_policy WHERE organization_id = @Org AND warranty_policy_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    // =====================================================================
    // Products and their warranty
    // =====================================================================

    private const string ProductFilter = """
        WHERE p.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
               OR b.brand_name ILIKE @Pattern OR c.category_name ILIKE @Pattern)
          AND (CAST(@CategoryId AS int) IS NULL OR p.category_id = @CategoryId)
          AND (CAST(@BrandId AS int) IS NULL OR p.brand_id = @BrandId)
          AND (CAST(@PolicyId AS int) IS NULL OR p.warranty_policy_id = @PolicyId)
          AND (CAST(@HasPolicy AS boolean) IS NULL OR (p.warranty_policy_id IS NOT NULL) = @HasPolicy)
        """;

    private const string ProductFrom = """
        FROM public.product p
        LEFT JOIN public.category c ON c.category_id = p.category_id
        LEFT JOIN public.brand b ON b.brand_id = p.brand_id
        LEFT JOIN public.warranty_policy wp ON wp.warranty_policy_id = p.warranty_policy_id
        """;

    public async Task<PagedResult<ProductWarrantyItem>> GetProductsAsync(int organizationId, ProductWarrantyQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<ProductWarrantyItem>(connection,
            $"SELECT count(*) {ProductFrom} {ProductFilter}",
            $"""
            SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                   p.product_type AS ProductType, p.tracking_type AS TrackingType, c.category_name AS CategoryName,
                   b.brand_name AS BrandName, p.warranty_policy_id AS WarrantyPolicyId, wp.policy_name AS WarrantyPolicyName,
                   p.is_active AS IsActive
            {ProductFrom} {ProductFilter}
            ORDER BY p.product_name, p.product_code
            """,
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.CategoryId,
                query.BrandId,
                PolicyId = query.WarrantyPolicyId,
                query.HasPolicy
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<(bool Exists, int? PolicyId)> GetProductPolicyAsync(int organizationId, int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<(int ProductId, int? PolicyId)>(new CommandDefinition(
            "SELECT product_id, warranty_policy_id FROM public.product WHERE organization_id = @Org AND product_id = @Id",
            new { Org = organizationId, Id = productId }, cancellationToken: cancellationToken));
        return (row.ProductId != 0, row.PolicyId);
    }

    public async Task<int> AssignPolicyAsync(
        int organizationId, IReadOnlyCollection<int> productIds, int? policyId, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "product", productIds.Select(id => (int?)id), cancellationToken);
        if (policyId is not null
            && !await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM public.warranty_policy WHERE organization_id = @Org AND warranty_policy_id = @Id)",
                new { Org = organizationId, Id = policyId }, transaction, cancellationToken: cancellationToken)))
            throw new BusinessRuleException("The warranty policy doesn't exist.");

        var changed = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.product SET warranty_policy_id = @PolicyId, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND product_id = ANY(@Ids) AND warranty_policy_id IS DISTINCT FROM @PolicyId
            """, new { Org = organizationId, Ids = productIds.Distinct().ToArray(), PolicyId = policyId, UserId = userId },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    // =====================================================================
    // Pickers
    // =====================================================================

    public async Task<IReadOnlyList<OptionItem>> GetDeviceTypesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<OptionItem>(new CommandDefinition("""
            SELECT d.lookup_data_id AS Id, d.lookup_data_name AS Name
            FROM public.lookup_data d JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
            WHERE lower(t.lookup_table_name) = lower(@Table) AND d.is_active
            ORDER BY d.lookup_data_id
            """, new { Table = WarrantyRules.DeviceTypeLookupTable }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> DeviceTypeExistsAsync(long lookupDataId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.lookup_data d JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
                           WHERE d.lookup_data_id = @Id AND lower(t.lookup_table_name) = lower(@Table))
            """, new { Id = lookupDataId, Table = WarrantyRules.DeviceTypeLookupTable }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<OptionItem>> GetTechniciansAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<OptionItem>(new CommandDefinition("""
            SELECT u.id AS Id, u.name AS Name, u.email AS Code
            FROM public."User" u
            WHERE u.status::text = 'ACTIVE'
              AND EXISTS (SELECT 1 FROM public.user_organization uo WHERE uo.user_id = u.id AND uo.organization_id = @Org)
            ORDER BY u.name
            """, new { Org = organizationId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM public."User" WHERE id = @Id)""", new { Id = userId }, cancellationToken: cancellationToken));
    }
}
