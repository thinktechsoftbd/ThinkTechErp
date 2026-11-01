using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Inventory;

public interface IInventorySetupService
{
    Task<PagedResult<Unit>> GetUnitsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Unit?> GetUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default);
    Task<Unit> CreateUnitAsync(int organizationId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Unit?> UpdateUnitAsync(int organizationId, int unitId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default);

    Task<PagedResult<Category>> GetCategoriesAsync(int organizationId, string? search, bool? isActive, int? parentId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Category?> GetCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default);
    Task<Category> CreateCategoryAsync(int organizationId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Category?> UpdateCategoryAsync(int organizationId, int categoryId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>?> GetCategoryTreeIdsAsync(int organizationId, int? categoryId, CancellationToken cancellationToken = default);

    Task<PagedResult<Brand>> GetBrandsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Brand?> GetBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default);
    Task<Brand> CreateBrandAsync(int organizationId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Brand?> UpdateBrandAsync(int organizationId, int brandId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default);

    Task<PagedResult<Warehouse>> GetWarehousesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Warehouse?> GetWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default);
    Task<Warehouse> CreateWarehouseAsync(int organizationId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Warehouse?> UpdateWarehouseAsync(int organizationId, int warehouseId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default);

    Task<InventorySetting> GetSettingAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<InventorySetting> SaveSettingAsync(int organizationId, InventorySettingUpdateRequest request, int userId, CancellationToken cancellationToken = default);

    Task<InventoryOptions> GetOptionsAsync(int organizationId, CancellationToken cancellationToken = default);
}

public sealed class InventorySetupService(IInventorySetupRepository repository) : IInventorySetupService
{
    // ---- Units

    public Task<PagedResult<Unit>> GetUnitsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetUnitsAsync(organizationId, Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<Unit?> GetUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default)
        => repository.GetUnitAsync(organizationId, unitId, cancellationToken);

    public async Task<Unit> CreateUnitAsync(int organizationId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateUnitAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetUnitAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Unit?> UpdateUnitAsync(int organizationId, int unitId, UnitUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateUnitAsync(organizationId, unitId, request, userId, cancellationToken))
            ? await repository.GetUnitAsync(organizationId, unitId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteUnitAsync(int organizationId, int unitId, CancellationToken cancellationToken = default)
        => repository.DeleteUnitAsync(organizationId, unitId, cancellationToken);

    private static void Normalize(UnitUpsertRequest request)
    {
        if (!InventoryRules.UnitTypes.Contains(request.UnitType))
            throw new BusinessRuleException($"Unit type must be one of: {string.Join(", ", InventoryRules.UnitTypes)}.");
        request.Code = request.Code.Trim();
        request.Name = request.Name.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
        request.IsoCode = Text.Clean(request.IsoCode)?.ToUpperInvariant();
    }

    // ---- Categories

    public Task<PagedResult<Category>> GetCategoriesAsync(int organizationId, string? search, bool? isActive, int? parentId, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetCategoriesAsync(organizationId, Text.Clean(search), isActive, parentId, limit, offset, cancellationToken);

    public Task<Category?> GetCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default)
        => repository.GetCategoryAsync(organizationId, categoryId, cancellationToken);

    public async Task<Category> CreateCategoryAsync(int organizationId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateCategoryAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetCategoryAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Category?> UpdateCategoryAsync(int organizationId, int categoryId, CategoryUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, categoryId, request, cancellationToken);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateCategoryAsync(organizationId, categoryId, request, userId, cancellationToken))
            ? await repository.GetCategoryAsync(organizationId, categoryId, cancellationToken)
            : null;
    }

    private async Task NormalizeAsync(int organizationId, int? categoryId, CategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        request.Code = Text.Clean(request.Code);
        request.Name = request.Name.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
        if (request.ParentCategoryId is null)
            return;
        if (await repository.GetCategoryAsync(organizationId, request.ParentCategoryId.Value, cancellationToken) is null)
            throw new BusinessRuleException("The parent category doesn't exist.");
        // A category can't move under itself or under one of its own sub-categories.
        if (categoryId is not null &&
            (await repository.GetCategoryTreeIdsAsync(organizationId, categoryId.Value, cancellationToken)).Contains(request.ParentCategoryId.Value))
            throw new BusinessRuleException("A category can't be placed under itself or one of its sub-categories.");
    }

    public Task<DeleteOutcome> DeleteCategoryAsync(int organizationId, int categoryId, CancellationToken cancellationToken = default)
        => repository.DeleteCategoryAsync(organizationId, categoryId, cancellationToken);

    public async Task<IReadOnlyList<int>?> GetCategoryTreeIdsAsync(int organizationId, int? categoryId, CancellationToken cancellationToken = default)
        => categoryId is null ? null : await repository.GetCategoryTreeIdsAsync(organizationId, categoryId.Value, cancellationToken);

    // ---- Brands

    public Task<PagedResult<Brand>> GetBrandsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetBrandsAsync(organizationId, Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<Brand?> GetBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default)
        => repository.GetBrandAsync(organizationId, brandId, cancellationToken);

    public async Task<Brand> CreateBrandAsync(int organizationId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateBrandAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetBrandAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Brand?> UpdateBrandAsync(int organizationId, int brandId, BrandUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateBrandAsync(organizationId, brandId, request, userId, cancellationToken))
            ? await repository.GetBrandAsync(organizationId, brandId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteBrandAsync(int organizationId, int brandId, CancellationToken cancellationToken = default)
        => repository.DeleteBrandAsync(organizationId, brandId, cancellationToken);

    private static void Normalize(BrandUpsertRequest request)
    {
        request.Name = request.Name.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
    }

    // ---- Warehouses

    public Task<PagedResult<Warehouse>> GetWarehousesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetWarehousesAsync(organizationId, Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<Warehouse?> GetWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default)
        => repository.GetWarehouseAsync(organizationId, warehouseId, cancellationToken);

    public async Task<Warehouse> CreateWarehouseAsync(int organizationId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateWarehouseAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetWarehouseAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Warehouse?> UpdateWarehouseAsync(int organizationId, int warehouseId, WarehouseUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateWarehouseAsync(organizationId, warehouseId, request, userId, cancellationToken))
            ? await repository.GetWarehouseAsync(organizationId, warehouseId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteWarehouseAsync(int organizationId, int warehouseId, CancellationToken cancellationToken = default)
        => repository.DeleteWarehouseAsync(organizationId, warehouseId, cancellationToken);

    private static void Normalize(WarehouseUpsertRequest request)
    {
        if (!InventoryRules.WarehouseTypes.Contains(request.WarehouseType))
            throw new BusinessRuleException("Warehouse type must be Store or Warehouse.");
        request.Code = request.Code.Trim();
        request.Name = request.Name.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
        request.Address = Text.Clean(request.Address);
        request.Phone = Text.Clean(request.Phone);
    }

    // ---- Settings and options

    public Task<InventorySetting> GetSettingAsync(int organizationId, CancellationToken cancellationToken = default)
        => repository.GetSettingAsync(organizationId, cancellationToken);

    public async Task<InventorySetting> SaveSettingAsync(int organizationId, InventorySettingUpdateRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (!await repository.CurrencyExistsAsync(request.BaseCurrencyLookupDataId, cancellationToken))
            throw new BusinessRuleException("Choose a currency from the Currency lookup list.");
        if (request.DefaultWarehouseId is not null &&
            await repository.GetWarehouseAsync(organizationId, request.DefaultWarehouseId.Value, cancellationToken) is null)
            throw new BusinessRuleException("The default warehouse doesn't exist.");

        await BusinessDbErrors.ExecuteAsync(() => repository.SaveSettingAsync(organizationId, request, userId, cancellationToken));
        return await repository.GetSettingAsync(organizationId, cancellationToken);
    }

    public Task<InventoryOptions> GetOptionsAsync(int organizationId, CancellationToken cancellationToken = default)
        => repository.GetOptionsAsync(organizationId, cancellationToken);
}
