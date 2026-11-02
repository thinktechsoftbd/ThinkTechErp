using Dapper;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Inventory;

public interface IInventorySetupRepository
{
    Task<PagedResult<Unit>> GetUnitsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Unit?> GetUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default);
    Task<int> CreateUnitAsync(int organizationId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateUnitAsync(int organizationId, int unitId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default);

    Task<PagedResult<Category>> GetCategoriesAsync(int organizationId, string? search, bool? isActive, int? parentId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Category?> GetCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default);
    /// <summary>The category and every category below it.</summary>
    Task<IReadOnlyList<int>> GetCategoryTreeIdsAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default);
    Task<int> CreateCategoryAsync(int organizationId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateCategoryAsync(int organizationId, int categoryId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default);

    Task<PagedResult<Brand>> GetBrandsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Brand?> GetBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default);
    Task<int> CreateBrandAsync(int organizationId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateBrandAsync(int organizationId, int brandId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default);

    Task<PagedResult<Warehouse>> GetWarehousesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Warehouse?> GetWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default);
    Task<int> CreateWarehouseAsync(int organizationId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateWarehouseAsync(int organizationId, int warehouseId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default);

    Task<InventorySetting> GetSettingAsync(int organizationId, CancellationToken cancellationToken = default);
    Task SaveSettingAsync(int organizationId, InventorySettingUpdateRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> CurrencyExistsAsync(long lookupDataId, CancellationToken cancellationToken = default);

    Task<InventoryOptions> GetOptionsAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaxGroupOption>> GetTaxGroupOptionsAsync(int organizationId, IReadOnlyCollection<int>? ids, CancellationToken cancellationToken = default);
}

public sealed class InventorySetupRepository(IDbConnectionFactory connectionFactory) : IInventorySetupRepository
{
    // ---- Units

    private const string UnitSelect = """
        SELECT u.unit_id AS Id, u.unit_code AS Code, u.unit_name AS Name, u.unit_name_local AS NameLocal,
               u.unit_type AS UnitType, u.iso_code AS IsoCode, u.allow_decimal AS AllowDecimal, u.is_active AS IsActive,
               (SELECT count(*) FROM inventory.product p WHERE p.base_unit_id = u.unit_id)::int AS ProductCount,
               u.created_at AT TIME ZONE 'utc' AS CreatedAt, u.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM inventory.unit u
        """;

    private const string UnitFilter = """
        WHERE u.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR u.unit_code ILIKE @Pattern OR u.unit_name ILIKE @Pattern
               OR u.unit_name_local ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR u.is_active = @IsActive)
        """;

    public async Task<PagedResult<Unit>> GetUnitsAsync(
        int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Unit>(connection,
            $"SELECT count(*) FROM inventory.unit u {UnitFilter}",
            $"{UnitSelect} {UnitFilter} ORDER BY u.unit_code, u.unit_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive },
            limit, offset, cancellationToken);
    }

    public async Task<Unit?> GetUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Unit>(new CommandDefinition(
            $"{UnitSelect} WHERE u.organization_id = @Org AND u.unit_id = @Id",
            new { Org = organizationId, Id = unitId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateUnitAsync(int organizationId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO inventory.unit (organization_id, unit_code, unit_name, unit_name_local, unit_type, iso_code,
                                        allow_decimal, is_active, created_at, created_by)
            VALUES (@Org, @Code, @Name, @NameLocal, @UnitType, @IsoCode, @AllowDecimal, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING unit_id
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateUnitAsync(int organizationId, int unitId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", unitId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.unit
            SET unit_code = @Code, unit_name = @Name, unit_name_local = @NameLocal, unit_type = @UnitType,
                iso_code = @IsoCode, allow_decimal = @AllowDecimal, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND unit_id = @Id
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM inventory.unit WHERE organization_id = @Org AND unit_id = @Id",
            new { Org = organizationId, Id = unitId }, cancellationToken: cancellationToken));
    }

    // ---- Categories

    // Every category of the organization with its path ("Fertilizer > Urea"), as a CTE named tree.
    private const string CategoryTree = """
        WITH RECURSIVE tree AS (
            SELECT c.category_id, c.category_name::text AS path, 0 AS depth
            FROM inventory.category c
            WHERE c.organization_id = @Org AND c.parent_category_id IS NULL
            UNION ALL
            SELECT c.category_id, tree.path || ' > ' || c.category_name, tree.depth + 1
            FROM inventory.category c
            JOIN tree ON c.parent_category_id = tree.category_id
            WHERE tree.depth < 20
        )
        """;

    private const string CategorySelect = """
        SELECT c.category_id AS Id, c.parent_category_id AS ParentCategoryId, parent.category_name AS ParentName,
               c.category_code AS Code, c.category_name AS Name, c.category_name_local AS NameLocal,
               coalesce(tree.path, c.category_name) AS Path, c.sort_order AS SortOrder, c.is_active AS IsActive,
               (SELECT count(*) FROM inventory.product p WHERE p.category_id = c.category_id)::int AS ProductCount,
               c.created_at AT TIME ZONE 'utc' AS CreatedAt, c.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM inventory.category c
        LEFT JOIN inventory.category parent ON parent.category_id = c.parent_category_id
        LEFT JOIN tree ON tree.category_id = c.category_id
        """;

    private const string CategoryFilter = """
        WHERE c.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR c.category_name ILIKE @Pattern OR c.category_code ILIKE @Pattern
               OR c.category_name_local ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR c.is_active = @IsActive)
          AND (CAST(@ParentId AS int) IS NULL OR c.parent_category_id = @ParentId)
        """;

    public async Task<PagedResult<Category>> GetCategoriesAsync(
        int organizationId, string? search, bool? isActive, int? parentId, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Category>(connection,
            $"SELECT count(*) FROM inventory.category c {CategoryFilter}",
            $"{CategoryTree} {CategorySelect} {CategoryFilter} ORDER BY Path, c.sort_order, c.category_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive, ParentId = parentId },
            limit, offset, cancellationToken);
    }

    public async Task<Category?> GetCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Category>(new CommandDefinition(
            $"{CategoryTree} {CategorySelect} WHERE c.organization_id = @Org AND c.category_id = @Id",
            new { Org = organizationId, Id = categoryId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<int>> GetCategoryTreeIdsAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<int>(new CommandDefinition("""
            WITH RECURSIVE below AS (
                SELECT category_id, 0 AS depth FROM inventory.category WHERE organization_id = @Org AND category_id = @Id
                UNION ALL
                SELECT c.category_id, below.depth + 1 FROM inventory.category c
                JOIN below ON c.parent_category_id = below.category_id WHERE below.depth < 20
            )
            SELECT category_id FROM below
            """, new { Org = organizationId, Id = categoryId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<int> CreateCategoryAsync(int organizationId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO inventory.category (organization_id, parent_category_id, category_code, category_name,
                                            category_name_local, sort_order, is_active, created_at, created_by)
            VALUES (@Org, @ParentCategoryId, @Code, @Name, @NameLocal, @SortOrder, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING category_id
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateCategoryAsync(int organizationId, int categoryId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", categoryId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.category
            SET parent_category_id = @ParentCategoryId, category_code = @Code, category_name = @Name,
                category_name_local = @NameLocal, sort_order = @SortOrder, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND category_id = @Id
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM inventory.category WHERE organization_id = @Org AND category_id = @Id",
            new { Org = organizationId, Id = categoryId }, cancellationToken: cancellationToken));
    }

    // ---- Brands

    private const string BrandSelect = """
        SELECT b.brand_id AS Id, b.brand_name AS Name, b.brand_name_local AS NameLocal, b.is_active AS IsActive,
               (SELECT count(*) FROM inventory.product p WHERE p.brand_id = b.brand_id)::int AS ProductCount,
               b.created_at AT TIME ZONE 'utc' AS CreatedAt, b.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM inventory.brand b
        """;

    private const string BrandFilter = """
        WHERE b.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR b.brand_name ILIKE @Pattern OR b.brand_name_local ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR b.is_active = @IsActive)
        """;

    public async Task<PagedResult<Brand>> GetBrandsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Brand>(connection,
            $"SELECT count(*) FROM inventory.brand b {BrandFilter}",
            $"{BrandSelect} {BrandFilter} ORDER BY b.brand_name, b.brand_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive },
            limit, offset, cancellationToken);
    }

    public async Task<Brand?> GetBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Brand>(new CommandDefinition(
            $"{BrandSelect} WHERE b.organization_id = @Org AND b.brand_id = @Id",
            new { Org = organizationId, Id = brandId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateBrandAsync(int organizationId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO inventory.brand (organization_id, brand_name, brand_name_local, is_active, created_at, created_by)
            VALUES (@Org, @Name, @NameLocal, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING brand_id
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateBrandAsync(int organizationId, int brandId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", brandId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.brand
            SET brand_name = @Name, brand_name_local = @NameLocal, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND brand_id = @Id
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM inventory.brand WHERE organization_id = @Org AND brand_id = @Id",
            new { Org = organizationId, Id = brandId }, cancellationToken: cancellationToken));
    }

    // ---- Warehouses

    private const string WarehouseSelect = """
        SELECT w.warehouse_id AS Id, w.warehouse_code AS Code, w.warehouse_name AS Name,
               w.warehouse_name_local AS NameLocal, w.warehouse_type AS WarehouseType, w.address AS Address,
               w.phone AS Phone, w.is_active AS IsActive,
               coalesce(s.default_warehouse_id = w.warehouse_id, false) AS IsDefault,
               coalesce((SELECT round(sum(b.quantity * b.average_cost), 4) FROM inventory.stock_balance b
                         WHERE b.warehouse_id = w.warehouse_id), 0) AS StockValue,
               w.created_at AT TIME ZONE 'utc' AS CreatedAt, w.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM inventory.warehouse w
        LEFT JOIN inventory.inventory_setting s ON s.organization_id = w.organization_id
        """;

    private const string WarehouseFilter = """
        WHERE w.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR w.warehouse_code ILIKE @Pattern OR w.warehouse_name ILIKE @Pattern
               OR w.warehouse_name_local ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR w.is_active = @IsActive)
        """;

    public async Task<PagedResult<Warehouse>> GetWarehousesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Warehouse>(connection,
            $"SELECT count(*) FROM inventory.warehouse w {WarehouseFilter}",
            $"{WarehouseSelect} {WarehouseFilter} ORDER BY w.warehouse_name, w.warehouse_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive },
            limit, offset, cancellationToken);
    }

    public async Task<Warehouse?> GetWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Warehouse>(new CommandDefinition(
            $"{WarehouseSelect} WHERE w.organization_id = @Org AND w.warehouse_id = @Id",
            new { Org = organizationId, Id = warehouseId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateWarehouseAsync(int organizationId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO inventory.warehouse (organization_id, warehouse_code, warehouse_name, warehouse_name_local,
                                             warehouse_type, address, phone, is_active, created_at, created_by)
            VALUES (@Org, @Code, @Name, @NameLocal, @WarehouseType, @Address, @Phone, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING warehouse_id
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateWarehouseAsync(int organizationId, int warehouseId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", warehouseId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.warehouse
            SET warehouse_code = @Code, warehouse_name = @Name, warehouse_name_local = @NameLocal,
                warehouse_type = @WarehouseType, address = @Address, phone = @Phone, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND warehouse_id = @Id
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        // Empty balance rows (quantity 0, never moved) would block the delete; anything with movements stays.
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition("""
            DELETE FROM inventory.stock_balance b
            WHERE b.warehouse_id = @Id AND b.quantity = 0 AND b.reserved_quantity = 0
              AND NOT EXISTS (SELECT 1 FROM inventory.stock_movement m WHERE m.warehouse_id = @Id);
            DELETE FROM inventory.warehouse WHERE organization_id = @Org AND warehouse_id = @Id;
            """, new { Org = organizationId, Id = warehouseId }, cancellationToken: cancellationToken));
    }

    // ---- Settings

    public async Task<InventorySetting> GetSettingAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<InventorySetting>(new CommandDefinition("""
            SELECT true AS IsConfigured, s.base_currency_lookup_data_id AS BaseCurrencyLookupDataId,
                   d.lookup_data_name AS BaseCurrency, s.fiscal_year_start_month AS FiscalYearStartMonth,
                   s.prices_include_tax AS PricesIncludeTax, s.allow_negative_stock AS AllowNegativeStock,
                   s.require_approval AS RequireApproval, s.expiry_alert_days AS ExpiryAlertDays,
                   s.default_warehouse_id AS DefaultWarehouseId, w.warehouse_name AS DefaultWarehouseName,
                   coalesce(s.updated_at, s.created_at) AT TIME ZONE 'utc' AS UpdatedAt
            FROM inventory.inventory_setting s
            LEFT JOIN public.lookup_data d ON d.lookup_data_id = s.base_currency_lookup_data_id
            LEFT JOIN inventory.warehouse w ON w.warehouse_id = s.default_warehouse_id
            WHERE s.organization_id = @Org
            """, new { Org = organizationId }, cancellationToken: cancellationToken))
            ?? await DefaultSettingAsync(connection, cancellationToken);
    }

    // Unsaved settings: BDT if the Currency list has it, else its first value.
    private static async Task<InventorySetting> DefaultSettingAsync(System.Data.IDbConnection connection, CancellationToken cancellationToken)
    {
        var currency = await connection.QueryFirstOrDefaultAsync<ThinkTechErpDomain.Models.Common.OptionItem>(new CommandDefinition("""
            SELECT d.lookup_data_id AS Id, d.lookup_data_name AS Name
            FROM public.lookup_data d JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
            WHERE lower(t.lookup_table_name) = 'currency' AND d.is_active
            ORDER BY d.lookup_data_name <> 'BDT', d.lookup_data_id
            LIMIT 1
            """, cancellationToken: cancellationToken));
        return new InventorySetting
        {
            IsConfigured = false,
            BaseCurrencyLookupDataId = currency?.Id,
            BaseCurrency = currency?.Name
        };
    }

    public async Task SaveSettingAsync(int organizationId, InventorySettingUpdateRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO inventory.inventory_setting (organization_id, base_currency_lookup_data_id, fiscal_year_start_month,
                prices_include_tax, allow_negative_stock, require_approval, expiry_alert_days, default_warehouse_id,
                created_at, created_by)
            VALUES (@Org, @BaseCurrencyLookupDataId, @FiscalYearStartMonth, @PricesIncludeTax, @AllowNegativeStock,
                    @RequireApproval, @ExpiryAlertDays, @DefaultWarehouseId, {SecurityDb.NowUtc}, @UserId)
            ON CONFLICT ON CONSTRAINT pk_inventory_setting DO UPDATE
            SET base_currency_lookup_data_id = EXCLUDED.base_currency_lookup_data_id,
                fiscal_year_start_month = EXCLUDED.fiscal_year_start_month,
                prices_include_tax = EXCLUDED.prices_include_tax,
                allow_negative_stock = EXCLUDED.allow_negative_stock,
                require_approval = EXCLUDED.require_approval,
                expiry_alert_days = EXCLUDED.expiry_alert_days,
                default_warehouse_id = EXCLUDED.default_warehouse_id,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> CurrencyExistsAsync(long lookupDataId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.lookup_data d JOIN public.lookup_table t USING (lookup_table_id)
                           WHERE d.lookup_data_id = @Id AND lower(t.lookup_table_name) = 'currency')
            """, new { Id = lookupDataId }, cancellationToken: cancellationToken));
    }

    // ---- Options

    public async Task<InventoryOptions> GetOptionsAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = new { Org = organizationId, Pattern = (string?)null, IsActive = (bool?)null, ParentId = (int?)null };
        var units = (await connection.QueryAsync<Unit>(new CommandDefinition(
            $"{UnitSelect} {UnitFilter} ORDER BY u.unit_code", parameters, cancellationToken: cancellationToken))).AsList();
        var categories = (await connection.QueryAsync<Category>(new CommandDefinition(
            $"{CategoryTree} {CategorySelect} {CategoryFilter} ORDER BY Path", parameters, cancellationToken: cancellationToken))).AsList();
        var brands = (await connection.QueryAsync<Brand>(new CommandDefinition(
            $"{BrandSelect} {BrandFilter} ORDER BY b.brand_name", parameters, cancellationToken: cancellationToken))).AsList();
        var warehouses = (await connection.QueryAsync<Warehouse>(new CommandDefinition(
            $"{WarehouseSelect} {WarehouseFilter} ORDER BY w.warehouse_name", parameters, cancellationToken: cancellationToken))).AsList();
        return new InventoryOptions
        {
            Units = units,
            Categories = categories,
            Brands = brands,
            Warehouses = warehouses,
            TaxGroups = await GetTaxGroupOptionsAsync(organizationId, null, cancellationToken)
        };
    }

    public async Task<IReadOnlyList<TaxGroupOption>> GetTaxGroupOptionsAsync(
        int organizationId, IReadOnlyCollection<int>? ids, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var groups = (await connection.QueryAsync<TaxGroupOption>(new CommandDefinition("""
            SELECT tax_group_id AS Id, tax_group_name AS Name, is_active AS IsActive
            FROM public.tax_group
            WHERE organization_id = @Org AND (CAST(@Ids AS int[]) IS NULL OR tax_group_id = ANY(@Ids))
            ORDER BY tax_group_name
            """, new { Org = organizationId, Ids = ids?.ToArray() }, cancellationToken: cancellationToken))).AsList();
        var rateRows = await RatesAsync(connection, organizationId, ids, cancellationToken);
        var byGroup = rateRows.ToLookup(r => r.GroupId, r => r.Rate);
        foreach (var group in groups)
            group.Rates = byGroup[group.Id].ToList();
        return groups;
    }

    private static async Task<IReadOnlyList<(int GroupId, TaxGroupRateItem Rate)>> RatesAsync(
        System.Data.IDbConnection connection, int organizationId, IReadOnlyCollection<int>? ids, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<int, TaxGroupRateItem, (int, TaxGroupRateItem)>(new CommandDefinition("""
            SELECT gr.tax_group_id AS GroupId, r.tax_rate_id AS TaxRateId, r.tax_code AS TaxCode, r.tax_name AS TaxName,
                   r.tax_type AS TaxType, r.rate_percent AS RatePercent, r.is_recoverable AS IsRecoverable,
                   gr.is_compound AS IsCompound, gr.sequence_no AS SequenceNo
            FROM public.tax_group_rate gr
            JOIN public.tax_rate r ON r.tax_rate_id = gr.tax_rate_id
            JOIN public.tax_group g ON g.tax_group_id = gr.tax_group_id
            WHERE g.organization_id = @Org AND (CAST(@Ids AS int[]) IS NULL OR g.tax_group_id = ANY(@Ids))
            ORDER BY gr.tax_group_id, gr.sequence_no, r.tax_code
            """, new { Org = organizationId, Ids = ids?.ToArray() }, cancellationToken: cancellationToken),
            (groupId, rate) => (groupId, rate), splitOn: "TaxRateId");
        return rows.AsList();
    }

    private static DynamicParameters Params(int organizationId, object request, int userId)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("Org", organizationId);
        parameters.Add("UserId", userId);
        return parameters;
    }
}
