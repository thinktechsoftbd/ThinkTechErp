using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Common;

/// <summary>Document numbers per organization and type, e.g. PINV-00001 (public.document_sequence).</summary>
internal static class DocumentNumbers
{
    public static async Task<string> NextAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, string documentType,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Org = organizationId,
            Type = documentType,
            Prefix = DocumentType.DefaultPrefix(documentType),
            Padding = DocumentType.DefaultPadding(documentType)
        };
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.document_sequence (organization_id, document_type, fiscal_year, prefix, number_padding)
            VALUES (@Org, @Type, 0, @Prefix, @Padding)
            ON CONFLICT ON CONSTRAINT ux_document_sequence DO NOTHING
            """, parameters, transaction, cancellationToken: cancellationToken));

        // The row lock serializes concurrent saves of the same type, so numbers never repeat.
        var (prefix, number, padding) = await connection.QuerySingleAsync<(string, long, short)>(new CommandDefinition($"""
            UPDATE public.document_sequence
            SET next_number = next_number + 1, updated_at = {SecurityDb.NowUtc}
            WHERE organization_id = @Org AND document_type = @Type AND fiscal_year = 0
            RETURNING prefix, next_number - 1, number_padding
            """, parameters, transaction, cancellationToken: cancellationToken));
        return prefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(padding, '0');
    }
}

/// <summary>
/// Checks that ids sent by a client belong to the caller's organization, so one organization can never
/// read or post against another's products, warehouses or suppliers.
/// </summary>
internal static class OrganizationGuard
{
    // Suppliers and customers share purchase.supplier; Side keeps only the parties on that side.
    private static readonly Dictionary<string, (string Table, string Key, string Label, string? Side)> Entities = new()
    {
        ["unit"] = ("inventory.unit", "unit_id", "unit", null),
        ["category"] = ("inventory.category", "category_id", "category", null),
        ["brand"] = ("inventory.brand", "brand_id", "brand", null),
        ["warehouse"] = ("inventory.warehouse", "warehouse_id", "warehouse", null),
        ["product"] = ("inventory.product", "product_id", "product", null),
        ["batch"] = ("inventory.batch", "batch_id", "batch", null),
        ["taxGroup"] = ("public.tax_group", "tax_group_id", "tax group", null),
        ["taxRate"] = ("public.tax_rate", "tax_rate_id", "tax rate", null),
        ["supplier"] = ("purchase.supplier", "supplier_id", "supplier", "is_supplier"),
        ["customer"] = ("purchase.supplier", "supplier_id", "customer", "is_customer"),
        ["party"] = ("purchase.supplier", "supplier_id", "supplier or customer", null)
    };

    public static async Task EnsureAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, string entity,
        IEnumerable<long?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
        if (distinct.Length == 0)
            return;

        var (table, key, label, side) = Entities[entity];
        var found = await connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*)::int FROM {table} WHERE organization_id = @Org AND {key} = ANY(@Ids){(side is null ? "" : " AND " + side)}",
            new { Org = organizationId, Ids = distinct }, transaction, cancellationToken: cancellationToken));
        if (found != distinct.Length)
            throw new BusinessRuleException(distinct.Length == 1
                ? $"The {label} doesn't exist."
                : $"One of the {label} choices doesn't exist.");
    }

    public static Task EnsureAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, string entity,
        IEnumerable<int?> ids, CancellationToken cancellationToken)
        => EnsureAsync(connection, transaction, organizationId, entity, ids.Select(id => (long?)id), cancellationToken);
}

/// <summary>Batches and serial numbers touched while posting.</summary>
internal static class StockItems
{
    /// <summary>The batch with this number, created on first use; an existing batch keeps its dates.</summary>
    public static async Task<long> EnsureBatchAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, int productId,
        string batchNo, DateOnly? manufactureDate, DateOnly? expiryDate, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO inventory.batch (organization_id, product_id, batch_no, manufacture_date, expiry_date,
                                         created_at, created_by)
            VALUES (@Org, @ProductId, @BatchNo, @ManufactureDate, @ExpiryDate, {SecurityDb.NowUtc}, @UserId)
            ON CONFLICT ON CONSTRAINT ux_batch_no DO UPDATE
                SET manufacture_date = coalesce(inventory.batch.manufacture_date, EXCLUDED.manufacture_date),
                    expiry_date = coalesce(inventory.batch.expiry_date, EXCLUDED.expiry_date)
            RETURNING batch_id
            """, new
            {
                Org = organizationId,
                ProductId = productId,
                BatchNo = batchNo.Trim(),
                ManufactureDate = manufactureDate,
                ExpiryDate = expiryDate,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

    /// <summary>Serial-tracked items arriving in a warehouse (new, or coming back into stock).</summary>
    public static async Task<IReadOnlyList<long>> ReceiveSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, int productId,
        long? batchId, int warehouseId, IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken)
    {
        if (serialNos.Count == 0)
            return [];

        var ids = (await connection.QueryAsync<long>(new CommandDefinition($"""
            INSERT INTO inventory.serial_number (organization_id, product_id, serial_no, batch_id, warehouse_id,
                                                 status, created_at, created_by)
            SELECT @Org, @ProductId, s, @BatchId, @WarehouseId, 'InStock', {SecurityDb.NowUtc}, @UserId
            FROM unnest(@SerialNos) AS s
            ON CONFLICT ON CONSTRAINT ux_serial_number DO UPDATE
                SET status = 'InStock', warehouse_id = EXCLUDED.warehouse_id, batch_id = EXCLUDED.batch_id,
                    updated_at = {SecurityDb.NowUtc}, updated_by = EXCLUDED.created_by
                WHERE inventory.serial_number.status <> 'InStock'
            RETURNING serial_number_id
            """, new
            {
                Org = organizationId,
                ProductId = productId,
                BatchId = batchId,
                WarehouseId = warehouseId,
                SerialNos = serialNos.ToArray(),
                UserId = userId
            }, transaction, cancellationToken: cancellationToken))).AsList();

        if (ids.Count != serialNos.Count)
            throw new BusinessRuleException("One of the serial numbers is already in stock.");
        return ids;
    }

    /// <summary>
    /// Serial-tracked items leaving a warehouse: they must be in stock there. newStatus is Sold,
    /// ReturnedToSupplier, Scrapped or InTransit.
    /// </summary>
    public static async Task<IReadOnlyList<long>> TakeSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, int productId, int warehouseId,
        IReadOnlyCollection<string> serialNos, string newStatus, CancellationToken cancellationToken)
    {
        if (serialNos.Count == 0)
            return [];

        var ids = (await connection.QueryAsync<long>(new CommandDefinition($"""
            UPDATE inventory.serial_number
            SET status = @Status, warehouse_id = NULL, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE product_id = @ProductId AND warehouse_id = @WarehouseId AND status = 'InStock'
              AND serial_no = ANY(@SerialNos)
            RETURNING serial_number_id
            """, new
            {
                Status = newStatus,
                UserId = userId,
                ProductId = productId,
                WarehouseId = warehouseId,
                SerialNos = serialNos.ToArray()
            }, transaction, cancellationToken: cancellationToken))).AsList();

        if (ids.Count != serialNos.Count)
            throw new BusinessRuleException("One of the serial numbers isn't in stock in this warehouse.");
        return ids;
    }

    /// <summary>Puts serial-tracked items back in a warehouse (cancelling a document that took them out).</summary>
    public static Task RestoreSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, IReadOnlyCollection<long> serialIds,
        int warehouseId, CancellationToken cancellationToken)
        => SetSerialsAsync(connection, transaction, userId, serialIds, "InStock", warehouseId, cancellationToken);

    public static async Task SetSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, IReadOnlyCollection<long> serialIds,
        string status, int? warehouseId, CancellationToken cancellationToken)
    {
        if (serialIds.Count == 0)
            return;
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.serial_number
            SET status = @Status, warehouse_id = @WarehouseId, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE serial_number_id = ANY(@Ids)
            """, new { Status = status, WarehouseId = warehouseId, UserId = userId, Ids = serialIds.ToArray() },
            transaction, cancellationToken: cancellationToken));
    }

    /// <summary>Serial-tracked items a document moved, by its movements (for restoring them on cancel).</summary>
    public static async Task<IReadOnlyList<long>> MovedSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, string documentType,
        long documentId, CancellationToken cancellationToken)
        => (await connection.QueryAsync<long>(new CommandDefinition("""
            SELECT DISTINCT s.serial_number_id
            FROM inventory.stock_movement m
            JOIN inventory.stock_movement_serial s ON s.stock_movement_id = m.stock_movement_id
            WHERE m.organization_id = @Org AND m.source_document_type = @Type AND m.source_document_id = @Id
              AND m.reverses_stock_movement_id IS NULL
            """, new { Org = organizationId, Type = documentType, Id = documentId }, transaction,
            cancellationToken: cancellationToken))).AsList();
}

/// <summary>The organization's inventory settings as posting needs them.</summary>
internal sealed record PostingSettings(bool AllowNegativeStock, bool PricesIncludeTax, long? BaseCurrencyLookupDataId);

internal static class PostingSettingsReader
{
    public static async Task<PostingSettings> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<PostingSettings>(new CommandDefinition("""
            SELECT allow_negative_stock AS AllowNegativeStock, prices_include_tax AS PricesIncludeTax,
                   base_currency_lookup_data_id AS BaseCurrencyLookupDataId
            FROM inventory.inventory_setting WHERE organization_id = @Org
            """, new { Org = organizationId }, transaction, cancellationToken: cancellationToken))
           ?? new PostingSettings(false, false, null);
}
