using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Inventory;

public interface IProductRepository
{
    Task<PagedResult<Product>> GetProductsAsync(int organizationId, ProductQuery query, CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default);
    Task<int> CreateProductAsync(int organizationId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateProductAsync(int organizationId, int productId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default);
    Task<bool> HasMovementsAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Products matching a code, name or barcode, for document lines.</summary>
    Task<IReadOnlyList<ProductLookupItem>> SearchAsync(int organizationId, string? search, bool stockOnly, int? warehouseId, int limit, CancellationToken cancellationToken = default);

    /// <summary>The products a document's lines use, by id (only this organization's).</summary>
    Task<IReadOnlyDictionary<int, ProductLookupItem>> GetForLinesAsync(int organizationId, IEnumerable<int> productIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchStockItem>> GetBatchStockAsync(int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SerialStockItem>> GetSerialStockAsync(int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default);
}

public sealed class ProductQuery
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public string? ProductType { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

/// <summary>Reads and writes jsonb product attributes as a string dictionary.</summary>
internal sealed class JsonDictionaryTypeHandler : SqlMapper.TypeHandler<Dictionary<string, string>>
{
    public override void SetValue(IDbDataParameter parameter, Dictionary<string, string>? value)
    {
        parameter.Value = JsonSerializer.Serialize(value ?? []);
        if (parameter is NpgsqlParameter npgsqlParameter)
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
    }

    public override Dictionary<string, string> Parse(object value)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(value.ToString() ?? "{}") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed class ProductRepository(IDbConnectionFactory connectionFactory) : IProductRepository
{
    static ProductRepository() => SqlMapper.AddTypeHandler(new JsonDictionaryTypeHandler());

    private const string ProductSelect = """
        SELECT p.product_id AS Id, p.object_id AS ObjectId, p.product_code AS Code, p.product_name AS Name,
               p.product_name_local AS NameLocal, p.product_type AS ProductType,
               p.category_id AS CategoryId, c.category_name AS CategoryName, p.brand_id AS BrandId, br.brand_name AS BrandName,
               p.base_unit_id AS BaseUnitId, u.unit_code AS BaseUnitCode, u.allow_decimal AS BaseUnitAllowsDecimal,
               p.tracking_type AS TrackingType, p.has_expiry AS HasExpiry, p.shelf_life_days AS ShelfLifeDays,
               p.generic_name AS GenericName, p.registration_no AS RegistrationNo, p.hs_code AS HsCode,
               p.country_of_origin AS CountryOfOrigin, p.purchase_tax_group_id AS PurchaseTaxGroupId,
               tg.tax_group_name AS PurchaseTaxGroupName, p.sale_tax_group_id AS SaleTaxGroupId,
               p.last_purchase_cost AS LastPurchaseCost, p.sale_price AS SalePrice, p.mrp AS Mrp,
               p.reorder_level AS ReorderLevel, p.reorder_quantity AS ReorderQuantity, p.attributes::text AS Attributes,
               p.image_url AS ImageUrl, p.is_active AS IsActive,
               coalesce((SELECT sum(b.quantity) FROM public.stock_balance b WHERE b.product_id = p.product_id), 0) AS StockQuantity,
               p.created_at AT TIME ZONE 'utc' AS CreatedAt, p.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.product p
        JOIN public.unit u ON u.unit_id = p.base_unit_id
        LEFT JOIN public.category c ON c.category_id = p.category_id
        LEFT JOIN public.brand br ON br.brand_id = p.brand_id
        LEFT JOIN public.tax_group tg ON tg.tax_group_id = p.purchase_tax_group_id
        """;

    private const string ProductFilter = """
        WHERE p.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
               OR p.product_name_local ILIKE @Pattern OR p.generic_name ILIKE @Pattern
               OR EXISTS (SELECT 1 FROM public.product_barcode pb WHERE pb.product_id = p.product_id AND pb.barcode = @Search))
          AND (CAST(@IsActive AS boolean) IS NULL OR p.is_active = @IsActive)
          AND (CAST(@CategoryId AS int) IS NULL OR p.category_id = @CategoryId)
          AND (CAST(@BrandId AS int) IS NULL OR p.brand_id = @BrandId)
          AND (CAST(@ProductType AS text) IS NULL OR p.product_type = @ProductType)
        """;

    public async Task<PagedResult<Product>> GetProductsAsync(int organizationId, ProductQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Product>(connection,
            $"SELECT count(*) FROM public.product p {ProductFilter}",
            $"{ProductSelect} {ProductFilter} ORDER BY p.product_name, p.product_id",
            new
            {
                Org = organizationId,
                query.Search,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.IsActive,
                query.CategoryId,
                query.BrandId,
                query.ProductType
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<Product?> GetProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var product = await connection.QuerySingleOrDefaultAsync<Product>(new CommandDefinition(
            $"{ProductSelect} WHERE p.organization_id = @Org AND p.product_id = @Id",
            new { Org = organizationId, Id = productId }, cancellationToken: cancellationToken));
        if (product is null)
            return null;

        product.Units = (await UnitsAsync(connection, [productId], cancellationToken))[productId].ToList();
        product.Barcodes = (await connection.QueryAsync<ProductBarcode>(new CommandDefinition("""
            SELECT pb.unit_id AS UnitId, u.unit_code AS UnitCode, pb.barcode AS Barcode
            FROM public.product_barcode pb LEFT JOIN public.unit u ON u.unit_id = pb.unit_id
            WHERE pb.product_id = @Id ORDER BY pb.product_barcode_id
            """, new { Id = productId }, cancellationToken: cancellationToken))).AsList();
        return product;
    }

    private static async Task<ILookup<int, ProductUnit>> UnitsAsync(
        IDbConnection connection, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<int, ProductUnit, (int, ProductUnit)>(new CommandDefinition("""
            SELECT pu.product_id AS ProductId, pu.unit_id AS UnitId, u.unit_code AS UnitCode, u.unit_name AS UnitName,
                   pu.conversion_factor AS ConversionFactor, pu.sale_price AS SalePrice,
                   pu.is_purchase_default AS IsPurchaseDefault, pu.is_sale_default AS IsSaleDefault
            FROM public.product_unit pu
            JOIN public.unit u ON u.unit_id = pu.unit_id
            WHERE pu.product_id = ANY(@Ids)
            ORDER BY pu.product_id, pu.conversion_factor, u.unit_code
            """, new { Ids = productIds.ToArray() }, cancellationToken: cancellationToken),
            (productId, unit) => (productId, unit), splitOn: "UnitId");
        return rows.ToLookup(r => r.Item1, r => r.Item2);
    }

    public async Task<int> CreateProductAsync(int organizationId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            // No code typed: the next of P-00001, P-00002, ..., skipping numbers already typed by hand.
            string code;
            var attempts = 0;
            do
            {
                code = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.Product, cancellationToken);
            } while (++attempts < 50 && await connection.QuerySingleAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM public.product WHERE organization_id = @Org AND lower(product_code) = lower(@Code))
                """, new { Org = organizationId, Code = code }, transaction, cancellationToken: cancellationToken)));
            request.Code = code;
        }

        var productId = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.product (
                organization_id, product_code, product_name, product_name_local, product_type, category_id, brand_id,
                base_unit_id, tracking_type, has_expiry, shelf_life_days, generic_name, registration_no, hs_code,
                country_of_origin, purchase_tax_group_id, sale_tax_group_id, sale_price, mrp, reorder_level,
                reorder_quantity, attributes, image_url, is_active, created_at, created_by)
            VALUES (
                @Org, @Code, @Name, @NameLocal, @ProductType, @CategoryId, @BrandId,
                @BaseUnitId, @TrackingType, @HasExpiry, @ShelfLifeDays, @GenericName, @RegistrationNo, @HsCode,
                @CountryOfOrigin, @PurchaseTaxGroupId, @SaleTaxGroupId, @SalePrice, @Mrp, @ReorderLevel,
                @ReorderQuantity, CAST(@Attributes AS jsonb), @ImageUrl, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING product_id
            """, ProductParams(organizationId, request, userId), transaction, cancellationToken: cancellationToken));
        await WriteUnitsAndBarcodesAsync(connection, transaction, organizationId, productId, request, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return productId;
    }

    public async Task<bool> UpdateProductAsync(int organizationId, int productId, ProductUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = ProductParams(organizationId, request, userId);
        parameters.Add("Id", productId);
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.product
            SET product_code = COALESCE(@Code, product_code), product_name = @Name, product_name_local = @NameLocal, product_type = @ProductType,
                category_id = @CategoryId, brand_id = @BrandId, base_unit_id = @BaseUnitId, tracking_type = @TrackingType,
                has_expiry = @HasExpiry, shelf_life_days = @ShelfLifeDays, generic_name = @GenericName,
                registration_no = @RegistrationNo, hs_code = @HsCode, country_of_origin = @CountryOfOrigin,
                purchase_tax_group_id = @PurchaseTaxGroupId, sale_tax_group_id = @SaleTaxGroupId,
                sale_price = @SalePrice, mrp = @Mrp, reorder_level = @ReorderLevel, reorder_quantity = @ReorderQuantity,
                attributes = CAST(@Attributes AS jsonb), image_url = @ImageUrl, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND product_id = @Id
            """, parameters, transaction, cancellationToken: cancellationToken)) > 0;
        if (!updated)
            return false;

        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.product_unit WHERE product_id = @Id;
            DELETE FROM public.product_barcode WHERE product_id = @Id;
            """, new { Id = productId }, transaction, cancellationToken: cancellationToken));
        await WriteUnitsAndBarcodesAsync(connection, transaction, organizationId, productId, request, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task WriteUnitsAndBarcodesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int productId,
        ProductUpsertRequest request, int userId, CancellationToken cancellationToken)
    {
        var units = new List<object>
        {
            new
            {
                ProductId = productId,
                UnitId = request.BaseUnitId,
                ConversionFactor = 1m,
                SalePrice = (decimal?)null,
                IsPurchaseDefault = !request.Units.Any(u => u.IsPurchaseDefault),
                IsSaleDefault = !request.Units.Any(u => u.IsSaleDefault),
                UserId = userId
            }
        };
        units.AddRange(request.Units.Select(u => (object)new
        {
            ProductId = productId,
            u.UnitId,
            u.ConversionFactor,
            u.SalePrice,
            u.IsPurchaseDefault,
            u.IsSaleDefault,
            UserId = userId
        }));
        await connection.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO public.product_unit (product_id, unit_id, conversion_factor, sale_price, is_purchase_default,
                                                is_sale_default, created_at, created_by)
            VALUES (@ProductId, @UnitId, @ConversionFactor, @SalePrice, @IsPurchaseDefault, @IsSaleDefault,
                    {SecurityDb.NowUtc}, @UserId)
            """, units, transaction, cancellationToken: cancellationToken));

        if (request.Barcodes.Count > 0)
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO public.product_barcode (organization_id, product_id, unit_id, barcode, created_at, created_by)
                VALUES (@Org, @ProductId, @UnitId, @Barcode, {SecurityDb.NowUtc}, @UserId)
                """, request.Barcodes.Select(b => new
                {
                    Org = organizationId,
                    ProductId = productId,
                    b.UnitId,
                    Barcode = b.Barcode.Trim(),
                    UserId = userId
                }), transaction, cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteProductAsync(int organizationId, int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Empty balance rows of a product that never moved don't count as use.
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.stock_balance b
            WHERE b.product_id = @Id AND b.quantity = 0 AND b.reserved_quantity = 0
              AND NOT EXISTS (SELECT 1 FROM public.stock_movement m WHERE m.product_id = @Id)
            """, new { Id = productId }, transaction, cancellationToken: cancellationToken));
        var outcome = await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.product WHERE organization_id = @Org AND product_id = @Id",
            new { Org = organizationId, Id = productId }, transaction, cancellationToken: cancellationToken));
        if (outcome == DeleteOutcome.Deleted)
            await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    public async Task<bool> HasMovementsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.stock_movement WHERE product_id = @Id)",
            new { Id = productId }, cancellationToken: cancellationToken));
    }

    private const string LookupSelect = """
        SELECT p.product_id AS Id, p.product_code AS Code, p.product_name AS Name, p.product_name_local AS NameLocal,
               p.product_type AS ProductType, p.tracking_type AS TrackingType, p.has_expiry AS HasExpiry,
               p.base_unit_id AS BaseUnitId, u.unit_code AS BaseUnitCode, u.allow_decimal AS AllowDecimal,
               p.purchase_tax_group_id AS PurchaseTaxGroupId, p.sale_tax_group_id AS SaleTaxGroupId,
               p.last_purchase_cost AS LastPurchaseCost, p.sale_price AS SalePrice, p.mrp AS Mrp,
               coalesce((SELECT sum(b.quantity) FROM public.stock_balance b
                         WHERE b.product_id = p.product_id
                           AND (CAST(@WarehouseId AS int) IS NULL OR b.warehouse_id = @WarehouseId)), 0) AS StockQuantity,
               (SELECT pb.barcode FROM public.product_barcode pb
                WHERE pb.product_id = p.product_id AND pb.barcode = @Search LIMIT 1) AS MatchedBarcode,
               (SELECT pb.unit_id FROM public.product_barcode pb
                WHERE pb.product_id = p.product_id AND pb.barcode = @Search LIMIT 1) AS MatchedUnitId
        FROM public.product p
        JOIN public.unit u ON u.unit_id = p.base_unit_id
        """;

    public async Task<IReadOnlyList<ProductLookupItem>> SearchAsync(
        int organizationId, string? search, bool stockOnly, int? warehouseId, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ProductLookupItem>(new CommandDefinition($"""
            {LookupSelect}
            WHERE p.organization_id = @Org AND p.is_active
              AND (NOT @StockOnly OR p.product_type = 'Stock')
              AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
                   OR p.product_name_local ILIKE @Pattern OR p.generic_name ILIKE @Pattern
                   OR EXISTS (SELECT 1 FROM public.product_barcode pb WHERE pb.product_id = p.product_id AND pb.barcode = @Search))
            ORDER BY (lower(p.product_code) = lower(coalesce(@Search, ''))) DESC,
                     (EXISTS (SELECT 1 FROM public.product_barcode pb WHERE pb.product_id = p.product_id AND pb.barcode = @Search)) DESC,
                     p.product_name
            LIMIT @Limit
            """, new
            {
                Org = organizationId,
                Search = search,
                Pattern = RepositoryHelpers.Pattern(search),
                StockOnly = stockOnly,
                WarehouseId = warehouseId,
                Limit = limit
            }, cancellationToken: cancellationToken))).AsList();
        await AttachUnitsAsync(connection, items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyDictionary<int, ProductLookupItem>> GetForLinesAsync(
        int organizationId, IEnumerable<int> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, ProductLookupItem>();

        await using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ProductLookupItem>(new CommandDefinition(
            $"{LookupSelect} WHERE p.organization_id = @Org AND p.product_id = ANY(@Ids)",
            new { Org = organizationId, Ids = ids, Search = (string?)null, WarehouseId = (int?)null },
            cancellationToken: cancellationToken))).AsList();
        await AttachUnitsAsync(connection, items, cancellationToken);
        return items.ToDictionary(i => i.Id);
    }

    private static async Task AttachUnitsAsync(IDbConnection connection, List<ProductLookupItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;
        var units = await UnitsAsync(connection, items.Select(i => i.Id).ToList(), cancellationToken);
        foreach (var item in items)
            item.Units = units[item.Id].ToList();
    }

    public async Task<IReadOnlyList<BatchStockItem>> GetBatchStockAsync(
        int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<BatchStockItem>(new CommandDefinition("""
            SELECT b.batch_id AS BatchId, b.batch_no AS BatchNo, b.manufacture_date AS ManufactureDate,
                   b.expiry_date AS ExpiryDate, sb.quantity AS Quantity, sb.average_cost AS AverageCost
            FROM public.stock_balance sb
            JOIN public.batch b ON b.batch_id = sb.batch_id
            WHERE sb.organization_id = @Org AND sb.product_id = @ProductId AND sb.warehouse_id = @WarehouseId
              AND sb.quantity > 0
            ORDER BY b.expiry_date NULLS LAST, b.batch_id
            """, new { Org = organizationId, ProductId = productId, WarehouseId = warehouseId },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<SerialStockItem>> GetSerialStockAsync(
        int organizationId, int productId, int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<SerialStockItem>(new CommandDefinition("""
            SELECT serial_number_id AS SerialNumberId, serial_no AS SerialNo, batch_id AS BatchId
            FROM public.serial_number
            WHERE organization_id = @Org AND product_id = @ProductId AND warehouse_id = @WarehouseId AND status = 'InStock'
            ORDER BY serial_no
            """, new { Org = organizationId, ProductId = productId, WarehouseId = warehouseId },
            cancellationToken: cancellationToken))).AsList();
    }

    private static DynamicParameters ProductParams(int organizationId, ProductUpsertRequest request, int userId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Org", organizationId);
        parameters.Add("UserId", userId);
        parameters.Add("Code", request.Code);
        parameters.Add("Name", request.Name);
        parameters.Add("NameLocal", request.NameLocal);
        parameters.Add("ProductType", request.ProductType);
        parameters.Add("CategoryId", request.CategoryId);
        parameters.Add("BrandId", request.BrandId);
        parameters.Add("BaseUnitId", request.BaseUnitId);
        parameters.Add("TrackingType", request.TrackingType);
        parameters.Add("HasExpiry", request.HasExpiry);
        parameters.Add("ShelfLifeDays", request.ShelfLifeDays);
        parameters.Add("GenericName", request.GenericName);
        parameters.Add("RegistrationNo", request.RegistrationNo);
        parameters.Add("HsCode", request.HsCode);
        parameters.Add("CountryOfOrigin", request.CountryOfOrigin);
        parameters.Add("PurchaseTaxGroupId", request.PurchaseTaxGroupId);
        parameters.Add("SaleTaxGroupId", request.SaleTaxGroupId);
        parameters.Add("SalePrice", request.SalePrice);
        parameters.Add("Mrp", request.Mrp);
        parameters.Add("ReorderLevel", request.ReorderLevel);
        parameters.Add("ReorderQuantity", request.ReorderQuantity);
        parameters.Add("Attributes", JsonSerializer.Serialize(request.Attributes ?? []), DbType.String);
        parameters.Add("ImageUrl", request.ImageUrl);
        parameters.Add("IsActive", request.IsActive);
        return parameters;
    }
}
