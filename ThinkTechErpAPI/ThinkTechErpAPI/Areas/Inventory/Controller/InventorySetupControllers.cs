using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Inventory;

namespace ThinkTechErpAPI.Areas.Inventory.Controller;

[Route("api/inventory/units")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryUnitsController(IInventorySetupService setup) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Unit>>> GetUnits(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetUnitsAsync(OrganizationId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{unitId:int}")]
    public async Task<ActionResult<Unit>> GetUnit(int unitId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetUnitAsync(OrganizationId, unitId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Unit>> CreateUnit([FromBody] UnitUpsertRequest request, CancellationToken cancellationToken)
    {
        var unit = await setup.CreateUnitAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetUnit), new { unitId = unit.Id }, unit);
    }

    [HttpPut("{unitId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Unit>> UpdateUnit(int unitId, [FromBody] UnitUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateUnitAsync(OrganizationId, unitId, request, UserId, cancellationToken));

    [HttpDelete("{unitId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteUnit(int unitId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteUnitAsync(OrganizationId, unitId, cancellationToken),
            "Products or documents use this unit. Deactivate it instead.");
}

[Route("api/inventory/categories")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryCategoriesController(IInventorySetupService setup) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Category>>> GetCategories(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive, [FromQuery] int? parentId,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetCategoriesAsync(OrganizationId, search, isActive, parentId, limit, offset, cancellationToken));

    [HttpGet("{categoryId:int}")]
    public async Task<ActionResult<Category>> GetCategory(int categoryId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetCategoryAsync(OrganizationId, categoryId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Category>> CreateCategory([FromBody] CategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var category = await setup.CreateCategoryAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetCategory), new { categoryId = category.Id }, category);
    }

    [HttpPut("{categoryId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Category>> UpdateCategory(int categoryId, [FromBody] CategoryUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateCategoryAsync(OrganizationId, categoryId, request, UserId, cancellationToken));

    [HttpDelete("{categoryId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteCategory(int categoryId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteCategoryAsync(OrganizationId, categoryId, cancellationToken),
            "The category has sub-categories or products. Move them first, or deactivate it.");
}

[Route("api/inventory/brands")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryBrandsController(IInventorySetupService setup) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Brand>>> GetBrands(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetBrandsAsync(OrganizationId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{brandId:int}")]
    public async Task<ActionResult<Brand>> GetBrand(int brandId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetBrandAsync(OrganizationId, brandId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Brand>> CreateBrand([FromBody] BrandUpsertRequest request, CancellationToken cancellationToken)
    {
        var brand = await setup.CreateBrandAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetBrand), new { brandId = brand.Id }, brand);
    }

    [HttpPut("{brandId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Brand>> UpdateBrand(int brandId, [FromBody] BrandUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateBrandAsync(OrganizationId, brandId, request, UserId, cancellationToken));

    [HttpDelete("{brandId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteBrand(int brandId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteBrandAsync(OrganizationId, brandId, cancellationToken),
            "Products use this brand. Deactivate it instead.");
}

[Route("api/inventory/warehouses")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryWarehousesController(IInventorySetupService setup) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Warehouse>>> GetWarehouses(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetWarehousesAsync(OrganizationId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{warehouseId:int}")]
    public async Task<ActionResult<Warehouse>> GetWarehouse(int warehouseId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetWarehouseAsync(OrganizationId, warehouseId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Warehouse>> CreateWarehouse([FromBody] WarehouseUpsertRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await setup.CreateWarehouseAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetWarehouse), new { warehouseId = warehouse.Id }, warehouse);
    }

    [HttpPut("{warehouseId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Warehouse>> UpdateWarehouse(int warehouseId, [FromBody] WarehouseUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateWarehouseAsync(OrganizationId, warehouseId, request, UserId, cancellationToken));

    [HttpDelete("{warehouseId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteWarehouse(int warehouseId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteWarehouseAsync(OrganizationId, warehouseId, cancellationToken),
            "The warehouse has stock history or documents. Deactivate it instead.");
}

[Route("api/inventory/products")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryProductsController(IProductService products) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Product>>> GetProducts(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive, [FromQuery] int? categoryId,
        [FromQuery] int? brandId, [FromQuery] string? productType,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await products.GetProductsAsync(OrganizationId, new ProductQuery
        {
            Search = search,
            IsActive = isActive,
            CategoryId = categoryId,
            BrandId = brandId,
            ProductType = productType,
            Limit = limit,
            Offset = offset
        }, cancellationToken));

    [HttpGet("{productId:int}")]
    public async Task<ActionResult<Product>> GetProduct(int productId, CancellationToken cancellationToken)
        => OkOrNotFound(await products.GetProductAsync(OrganizationId, productId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Product>> CreateProduct([FromBody] ProductUpsertRequest request, CancellationToken cancellationToken)
    {
        var product = await products.CreateProductAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetProduct), new { productId = product.Id }, product);
    }

    [HttpPut("{productId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<Product>> UpdateProduct(int productId, [FromBody] ProductUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await products.UpdateProductAsync(OrganizationId, productId, request, UserId, cancellationToken));

    [HttpDelete("{productId:int}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteProduct(int productId, CancellationToken cancellationToken)
        => DeleteResult(await products.DeleteProductAsync(OrganizationId, productId, cancellationToken),
            "The product has stock or appears on documents. Deactivate it instead.");
}

/// <summary>Settings and pickers the inventory and purchase screens share.</summary>
[Route("api/inventory")]
[Authorize(Policy = InventoryPolicies.Lookup)]
public sealed class InventoryLookupsController(IInventorySetupService setup, IProductService products) : OrganizationControllerBase
{
    /// <summary>Units, categories, brands, warehouses and tax groups, for forms.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<InventoryOptions>> GetOptions(CancellationToken cancellationToken)
        => Ok(await setup.GetOptionsAsync(OrganizationId, cancellationToken));

    /// <summary>
    /// Products by code, name or barcode, with their units and stock, for document lines. With ids
    /// (comma separated, e.g. a draft's lines) it returns exactly those products instead.
    /// </summary>
    [HttpGet("products/lookup")]
    public async Task<ActionResult<IReadOnlyList<ProductLookupItem>>> SearchProducts(
        [FromQuery, StringLength(100)] string? search, [FromQuery, StringLength(2000)] string? ids,
        [FromQuery] bool stockOnly = false, [FromQuery] int? warehouseId = null,
        [FromQuery, Range(1, 50)] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var productIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).Distinct().Take(500).ToList();
            return Ok(await products.GetForLinesAsync(OrganizationId, productIds, cancellationToken));
        }
        return Ok(await products.SearchAsync(OrganizationId, search, stockOnly, warehouseId, limit, cancellationToken));
    }

    [HttpGet("products/{productId:int}/batches")]
    public async Task<ActionResult<IReadOnlyList<BatchStockItem>>> GetBatches(
        int productId, [FromQuery, Range(1, int.MaxValue)] int warehouseId, CancellationToken cancellationToken)
        => Ok(await products.GetBatchStockAsync(OrganizationId, productId, warehouseId, cancellationToken));

    [HttpGet("products/{productId:int}/serials")]
    public async Task<ActionResult<IReadOnlyList<SerialStockItem>>> GetSerials(
        int productId, [FromQuery, Range(1, int.MaxValue)] int warehouseId, CancellationToken cancellationToken)
        => Ok(await products.GetSerialStockAsync(OrganizationId, productId, warehouseId, cancellationToken));

    [HttpGet("settings")]
    public async Task<ActionResult<InventorySetting>> GetSettings(CancellationToken cancellationToken)
        => Ok(await setup.GetSettingAsync(OrganizationId, cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<InventorySetting>> SaveSettings([FromBody] InventorySettingUpdateRequest request, CancellationToken cancellationToken)
        => Ok(await setup.SaveSettingAsync(OrganizationId, request, UserId, cancellationToken));
}
