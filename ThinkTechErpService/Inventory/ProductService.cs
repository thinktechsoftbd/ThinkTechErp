using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Inventory;

public interface IProductService
{
    Task<PagedResult<Product>> GetProductsAsync(int organizationId, ProductQuery query, CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default);
    Task<Product> CreateProductAsync(int organizationId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Product?> UpdateProductAsync(int organizationId, int productId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductLookupItem>> SearchAsync(int organizationId, string? search, bool stockOnly, int? warehouseId, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatchStockItem>> GetBatchStockAsync(int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SerialStockItem>> GetSerialStockAsync(int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductLookupItem>> GetForLinesAsync(int organizationId, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default);
}

public sealed class ProductService(IProductRepository products, IOwnershipRepository ownership) : IProductService
{
    public Task<PagedResult<Product>> GetProductsAsync(int organizationId, ProductQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return products.GetProductsAsync(organizationId, query, cancellationToken);
    }

    public Task<Product?> GetProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default)
        => products.GetProductAsync(organizationId, productId, cancellationToken);

    public async Task<Product> CreateProductAsync(int organizationId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => products.CreateProductAsync(organizationId, request, userId, cancellationToken));
        return (await products.GetProductAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Product?> UpdateProductAsync(int organizationId, int productId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var current = await products.GetProductAsync(organizationId, productId, cancellationToken);
        if (current is null)
            return null;
        await ValidateAsync(organizationId, request, cancellationToken);

        // Stock is kept in the base unit, and batches and serials hang off the tracking type: once the
        // product has moved, changing either would make its history wrong.
        if ((current.BaseUnitId != request.BaseUnitId || current.TrackingType != request.TrackingType ||
             current.ProductType != request.ProductType) && await products.HasMovementsAsync(productId, cancellationToken))
            throw new BusinessRuleException("The base unit, product type and tracking can't change once the product has stock movements.");

        return await BusinessDbErrors.ExecuteAsync(() => products.UpdateProductAsync(organizationId, productId, request, userId, cancellationToken))
            ? await products.GetProductAsync(organizationId, productId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default)
        => products.DeleteProductAsync(organizationId, productId, cancellationToken);

    public Task<IReadOnlyList<ProductLookupItem>> SearchAsync(int organizationId, string? search, bool stockOnly, int? warehouseId, int limit, CancellationToken cancellationToken = default)
        => products.SearchAsync(organizationId, Text.Clean(search), stockOnly, warehouseId, limit, cancellationToken);

    public Task<IReadOnlyList<BatchStockItem>> GetBatchStockAsync(int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default)
        => products.GetBatchStockAsync(organizationId, productId, warehouseId, cancellationToken);

    public Task<IReadOnlyList<SerialStockItem>> GetSerialStockAsync(int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default)
        => products.GetSerialStockAsync(organizationId, productId, warehouseId, cancellationToken);

    public async Task<IReadOnlyList<ProductLookupItem>> GetForLinesAsync(int organizationId, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default)
        => (await products.GetForLinesAsync(organizationId, productIds, cancellationToken)).Values.ToList();

    private async Task ValidateAsync(int organizationId, ProductUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!InventoryRules.ProductTypes.Contains(request.ProductType))
            throw new BusinessRuleException("Product type must be Stock, NonStock or Service.");
        if (!InventoryRules.TrackingTypes.Contains(request.TrackingType))
            throw new BusinessRuleException("Tracking must be None, Batch or Serial.");
        if (request.ProductType != "Stock" && request.TrackingType != "None")
            throw new BusinessRuleException("Only stock products can be tracked by batch or serial number.");
        if (request.HasExpiry && request.TrackingType != "Batch")
            throw new BusinessRuleException("Turn on batch tracking to record expiry dates.");

        request.Code = request.Code.Trim();
        request.Name = request.Name.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
        request.GenericName = Text.Clean(request.GenericName);
        request.RegistrationNo = Text.Clean(request.RegistrationNo);
        request.HsCode = Text.Clean(request.HsCode);
        request.CountryOfOrigin = Text.Clean(request.CountryOfOrigin)?.ToUpperInvariant();
        request.ImageUrl = Text.Clean(request.ImageUrl);
        request.Attributes = request.Attributes?
            .Where(a => !string.IsNullOrWhiteSpace(a.Key) && !string.IsNullOrWhiteSpace(a.Value))
            .ToDictionary(a => a.Key.Trim(), a => a.Value.Trim());

        if (request.Units.Any(u => u.UnitId == request.BaseUnitId))
            throw new BusinessRuleException("The base unit is always included; list only the other packs.");
        if (request.Units.Select(u => u.UnitId).Distinct().Count() != request.Units.Count)
            throw new BusinessRuleException("A unit is listed twice for this product.");
        if (request.Units.Count(u => u.IsPurchaseDefault) > 1 || request.Units.Count(u => u.IsSaleDefault) > 1)
            throw new BusinessRuleException("Only one unit can be the default purchase unit, and one the default sale unit.");

        foreach (var barcode in request.Barcodes)
            barcode.Barcode = barcode.Barcode.Trim();
        if (request.Barcodes.Select(b => b.Barcode).Distinct(StringComparer.Ordinal).Count() != request.Barcodes.Count)
            throw new BusinessRuleException("A barcode is listed twice.");
        var productUnits = request.Units.Select(u => u.UnitId).Append(request.BaseUnitId).ToHashSet();
        if (request.Barcodes.Any(b => b.UnitId is not null && !productUnits.Contains(b.UnitId.Value)))
            throw new BusinessRuleException("A barcode's unit must be one of the product's units.");

        await ownership.EnsureAsync(organizationId, "unit", productUnits.Select(id => (int?)id), cancellationToken);
        await ownership.EnsureAsync(organizationId, "category", [request.CategoryId], cancellationToken);
        await ownership.EnsureAsync(organizationId, "brand", [request.BrandId], cancellationToken);
        await ownership.EnsureAsync(organizationId, "taxGroup", [request.PurchaseTaxGroupId, request.SaleTaxGroupId], cancellationToken);
    }
}
