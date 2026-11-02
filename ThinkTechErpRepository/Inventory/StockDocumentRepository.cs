using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Inventory;

public sealed class StockDocumentQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? WarehouseId { get; set; }
    public string? Reason { get; set; }
    /// <summary>Adjustments: leave Opening stock out (it has its own list).</summary>
    public bool ExcludeOpening { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

public interface IStockDocumentRepository
{
    Task<PagedResult<StockAdjustment>> GetAdjustmentsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default);
    Task<StockAdjustment?> GetAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Creates (Id 0) or replaces a draft adjustment; returns its id.</summary>
    Task<long> SaveAdjustmentAsync(int organizationId, StockAdjustment adjustment, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<StockTransfer>> GetTransfersAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default);
    Task<StockTransfer?> GetTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<long> SaveTransferAsync(int organizationId, StockTransfer transfer, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task DispatchTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task ReceiveTransferAsync(int organizationId, long id, StockTransferReceiveRequest request, int userId, CancellationToken cancellationToken = default);
    Task CancelTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<StockCount>> GetCountsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default);
    Task<StockCount?> GetCountAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<long> CreateCountAsync(int organizationId, StockCountCreateRequest request, IReadOnlyCollection<int>? categoryIds, int userId, CancellationToken cancellationToken = default);
    Task UpdateCountAsync(int organizationId, long id, StockCountUpdateRequest request, int userId, CancellationToken cancellationToken = default);
    Task PostCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class StockDocumentRepository(IDbConnectionFactory connectionFactory) : IStockDocumentRepository
{
    private sealed class DocumentHeader
    {
        public long Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateOnly DocumentDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
    }

    private sealed class PostingLine
    {
        public long Id { get; set; }
        public int ProductId { get; set; }
        public string TrackingType { get; set; } = "None";
        public decimal BaseQuantity { get; set; }
        public long? BatchId { get; set; }
        public string? BatchNo { get; set; }
        public DateOnly? ManufactureDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public decimal? UnitCost { get; set; }
        public decimal? ReceivedBaseQuantity { get; set; }
    }

    // =====================================================================
    // Stock adjustments
    // =====================================================================

    private const string AdjustmentSelect = """
        SELECT a.stock_adjustment_id AS Id, a.adjustment_no AS AdjustmentNo, a.adjustment_date AS AdjustmentDate,
               a.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName, a.reason AS Reason,
               a.stock_count_id AS StockCountId, sc.count_no AS StockCountNo, a.status AS Status,
               a.total_value AS TotalValue,
               (SELECT count(*) FROM inventory.stock_adjustment_line l WHERE l.stock_adjustment_id = a.stock_adjustment_id)::int AS LineCount,
               a.posted_at AT TIME ZONE 'utc' AS PostedAt, a.posted_by AS PostedBy, a.remarks AS Remarks,
               a.created_at AT TIME ZONE 'utc' AS CreatedAt, a.created_by AS CreatedBy,
               a.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM inventory.stock_adjustment a
        JOIN inventory.warehouse w ON w.warehouse_id = a.warehouse_id
        LEFT JOIN inventory.stock_count sc ON sc.stock_count_id = a.stock_count_id
        """;

    private const string AdjustmentFilter = """
        WHERE a.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR a.adjustment_no ILIKE @Pattern OR a.remarks ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR a.status = @Status)
          AND (CAST(@WarehouseId AS int) IS NULL OR a.warehouse_id = @WarehouseId)
          AND (CAST(@Reason AS text) IS NULL OR a.reason = @Reason)
          AND (NOT @ExcludeOpening OR a.reason <> 'Opening')
          AND (CAST(@From AS date) IS NULL OR a.adjustment_date >= @From)
          AND (CAST(@To AS date) IS NULL OR a.adjustment_date <= @To)
        """;

    private static object QueryParams(int organizationId, StockDocumentQuery query) => new
    {
        Org = organizationId,
        Pattern = RepositoryHelpers.Pattern(query.Search),
        query.Status,
        query.WarehouseId,
        query.Reason,
        query.ExcludeOpening,
        query.From,
        query.To
    };

    public async Task<PagedResult<StockAdjustment>> GetAdjustmentsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<StockAdjustment>(connection,
            $"SELECT count(*) FROM inventory.stock_adjustment a {AdjustmentFilter}",
            $"{AdjustmentSelect} {AdjustmentFilter} ORDER BY a.adjustment_date DESC, a.stock_adjustment_id DESC",
            QueryParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    private const string StockLineSelect = """
        l.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
        p.tracking_type AS TrackingType, p.has_expiry AS HasExpiry, l.unit_id AS UnitId, u.unit_code AS UnitCode,
        bu.unit_code AS BaseUnitCode, l.conversion_factor AS ConversionFactor, l.quantity AS Quantity,
        l.base_quantity AS BaseQuantity, l.batch_id AS BatchId, coalesce(b.batch_no, l.batch_no) AS BatchNo,
        coalesce(b.manufacture_date, l.manufacture_date) AS ManufactureDate, coalesce(b.expiry_date, l.expiry_date) AS ExpiryDate,
        l.unit_cost AS UnitCost, l.remarks AS Remarks
        """;

    public async Task<StockAdjustment?> GetAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var adjustment = await connection.QuerySingleOrDefaultAsync<StockAdjustment>(new CommandDefinition(
            $"{AdjustmentSelect} WHERE a.organization_id = @Org AND a.stock_adjustment_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (adjustment is null)
            return null;

        adjustment.Lines = (await connection.QueryAsync<StockLine>(new CommandDefinition($"""
            SELECT l.stock_adjustment_line_id AS Id, l.line_no AS LineNo, {StockLineSelect}
            FROM inventory.stock_adjustment_line l
            JOIN inventory.product p ON p.product_id = l.product_id
            JOIN inventory.unit u ON u.unit_id = l.unit_id
            JOIN inventory.unit bu ON bu.unit_id = p.base_unit_id
            LEFT JOIN inventory.batch b ON b.batch_id = l.batch_id
            WHERE l.stock_adjustment_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        await AttachSerialsAsync(connection, "inventory.stock_adjustment_serial", "stock_adjustment_line_id",
            adjustment.Lines, cancellationToken);
        return adjustment;
    }

    private static async Task AttachSerialsAsync<TLine>(
        System.Data.IDbConnection connection, string table, string lineColumn, IReadOnlyList<TLine> lines,
        CancellationToken cancellationToken) where TLine : StockLine
    {
        if (lines.Count == 0)
            return;
        var serials = (await connection.QueryAsync<(long LineId, string SerialNo)>(new CommandDefinition(
            $"SELECT {lineColumn}, serial_no FROM {table} WHERE {lineColumn} = ANY(@Ids) ORDER BY serial_no",
            new { Ids = lines.Select(l => l.Id).ToArray() }, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialNo);
        foreach (var line in lines)
            line.SerialNos = serials[line.Id].ToList();
    }

    public async Task<long> SaveAdjustmentAsync(int organizationId, StockAdjustment adjustment, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = new
        {
            Org = organizationId,
            adjustment.AdjustmentDate,
            adjustment.WarehouseId,
            adjustment.Reason,
            adjustment.Remarks,
            UserId = userId
        };

        long id;
        if (adjustment.Id == 0)
        {
            var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.StockAdjustment, cancellationToken);
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO inventory.stock_adjustment (organization_id, adjustment_no, adjustment_date, warehouse_id, reason,
                                                        status, remarks, created_at, created_by)
                VALUES (@Org, @Number, @AdjustmentDate, @WarehouseId, @Reason, 'Draft', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING stock_adjustment_id
                """, new { header.Org, Number = number, header.AdjustmentDate, header.WarehouseId, header.Reason, header.Remarks, header.UserId },
                transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = adjustment.Id;
            await LockDraftAsync(connection, transaction, "inventory.stock_adjustment", "stock_adjustment_id", "adjustment_no",
                "adjustment_date", "warehouse_id", organizationId, id, "adjustment", cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE inventory.stock_adjustment
                SET adjustment_date = @AdjustmentDate, warehouse_id = @WarehouseId, reason = @Reason, remarks = @Remarks,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE stock_adjustment_id = @Id;
                DELETE FROM inventory.stock_adjustment_line WHERE stock_adjustment_id = @Id;
                """, new { header.AdjustmentDate, header.WarehouseId, header.Reason, header.Remarks, header.UserId, Id = id },
                transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in adjustment.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO inventory.stock_adjustment_line (stock_adjustment_id, line_no, product_id, unit_id, conversion_factor,
                    quantity, batch_id, batch_no, manufacture_date, expiry_date, unit_cost, remarks)
                VALUES (@DocumentId, @LineNo, @ProductId, @UnitId, @ConversionFactor, @Quantity, @BatchId, @BatchNo,
                        @ManufactureDate, @ExpiryDate, @UnitCost, @Remarks)
                RETURNING stock_adjustment_line_id
                """, new
                {
                    DocumentId = id,
                    line.LineNo,
                    line.ProductId,
                    line.UnitId,
                    line.ConversionFactor,
                    line.Quantity,
                    line.BatchId,
                    line.BatchNo,
                    line.ManufactureDate,
                    line.ExpiryDate,
                    line.UnitCost,
                    line.Remarks
                }, transaction, cancellationToken: cancellationToken));
            await InsertSerialsAsync(connection, transaction, "inventory.stock_adjustment_serial", "stock_adjustment_line_id",
                lineId, line.SerialNos, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private static async Task InsertSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string lineColumn, long lineId,
        IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken)
    {
        if (serialNos.Count == 0)
            return;
        await connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {table} ({lineColumn}, serial_no) SELECT @LineId, unnest(@SerialNos)",
            new { LineId = lineId, SerialNos = serialNos.ToArray() }, transaction, cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => await DeleteDraftAsync("inventory.stock_adjustment", "stock_adjustment_id", organizationId, id, cancellationToken);

    public async Task PostAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostAdjustmentCoreAsync(connection, transaction, organizationId, id, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task PostAdjustmentCoreAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, int userId,
        CancellationToken cancellationToken)
    {
        var header = await LockDraftAsync(connection, transaction, "inventory.stock_adjustment", "stock_adjustment_id",
            "adjustment_no", "adjustment_date", "warehouse_id", organizationId, id, "adjustment", cancellationToken);
        var reason = await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT reason FROM inventory.stock_adjustment WHERE stock_adjustment_id = @Id", new { Id = id }, transaction,
            cancellationToken: cancellationToken));
        var lines = await PostingLinesAsync(connection, transaction, """
            SELECT l.stock_adjustment_line_id AS Id, l.product_id AS ProductId, p.tracking_type AS TrackingType,
                   l.base_quantity AS BaseQuantity, l.batch_id AS BatchId, l.batch_no AS BatchNo,
                   l.manufacture_date AS ManufactureDate, l.expiry_date AS ExpiryDate, l.unit_cost AS UnitCost
            FROM inventory.stock_adjustment_line l JOIN inventory.product p ON p.product_id = l.product_id
            WHERE l.stock_adjustment_id = @Id ORDER BY l.line_no
            """, id, cancellationToken);
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line before posting.");

        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        var ledger = new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate,
            DocumentType.StockAdjustment, id, header.Number, settings.AllowNegativeStock);
        var serials = await SerialsByLineAsync(connection, transaction, "inventory.stock_adjustment_serial",
            "stock_adjustment_line_id", lines, cancellationToken);

        var moves = new List<StockMove>();
        foreach (var line in lines)
        {
            var incoming = line.BaseQuantity > 0;
            var batchId = await ResolveBatchAsync(connection, transaction, organizationId, userId, line, incoming, cancellationToken);
            IReadOnlyList<long> serialIds = [];
            if (line.TrackingType == "Serial")
            {
                var lineSerials = serials[line.Id].ToList();
                serialIds = incoming
                    ? await StockItems.ReceiveSerialsAsync(connection, transaction, organizationId, userId, line.ProductId,
                        batchId, header.WarehouseId, lineSerials, cancellationToken)
                    : await StockItems.TakeSerialsAsync(connection, transaction, userId, line.ProductId, header.WarehouseId,
                        lineSerials, "Scrapped", cancellationToken);
                await LinkSerialsAsync(connection, transaction, "inventory.stock_adjustment_serial", "stock_adjustment_line_id",
                    line.Id, lineSerials, serialIds, cancellationToken);
            }

            var type = reason == StockAdjustmentReasons.Opening ? "Opening" : incoming ? "AdjustmentIn" : "AdjustmentOut";
            moves.Add(new StockMove(header.WarehouseId, line.ProductId, batchId, line.BaseQuantity, type, line.Id,
                UnitCost: incoming ? line.UnitCost : null, SerialNumberIds: serialIds));
        }

        var applied = await ledger.ApplyAsync(moves, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.stock_adjustment
            SET status = 'Posted', total_value = @TotalValue, posted_at = {SecurityDb.NowUtc}, posted_by = @UserId,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE stock_adjustment_id = @Id
            """, new { Id = id, TotalValue = applied.Sum(a => a.TotalCost), UserId = userId }, transaction,
            cancellationToken: cancellationToken));
        // Record the cost each line moved at (stock removed takes the average cost).
        for (var i = 0; i < lines.Count; i++)
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE inventory.stock_adjustment_line SET unit_cost = @UnitCost, batch_id = @BatchId WHERE stock_adjustment_line_id = @Id",
                new { Id = lines[i].Id, UnitCost = Math.Round(applied[i].UnitCost, 6), moves[i].BatchId }, transaction,
                cancellationToken: cancellationToken));
    }

    public async Task CancelAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "inventory.stock_adjustment", "stock_adjustment_id",
            "adjustment_no", "adjustment_date", "warehouse_id", organizationId, id, cancellationToken)
            ?? throw new BusinessRuleException("The adjustment doesn't exist.");
        if (header.Status != DocumentStatus.Posted)
            throw new BusinessRuleException("Only a posted adjustment can be cancelled; delete a draft instead.");

        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await RestoreSerialsOnCancelAsync(connection, transaction, organizationId, userId, DocumentType.StockAdjustment, id,
            header.WarehouseId, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
            DocumentType.StockAdjustment, id, header.Number, settings.AllowNegativeStock).ReverseAsync(null, cancellationToken);
        await SetStatusAsync(connection, transaction, "inventory.stock_adjustment", "stock_adjustment_id", id,
            DocumentStatus.Cancelled, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // =====================================================================
    // Stock transfers
    // =====================================================================

    private const string TransferSelect = """
        SELECT t.stock_transfer_id AS Id, t.transfer_no AS TransferNo, t.transfer_date AS TransferDate,
               t.from_warehouse_id AS FromWarehouseId, fw.warehouse_name AS FromWarehouseName,
               t.to_warehouse_id AS ToWarehouseId, tw.warehouse_name AS ToWarehouseName, t.status AS Status,
               coalesce((SELECT round(sum(l.base_quantity * l.unit_cost), 4) FROM inventory.stock_transfer_line l
                         WHERE l.stock_transfer_id = t.stock_transfer_id), 0) AS TotalValue,
               (SELECT count(*) FROM inventory.stock_transfer_line l WHERE l.stock_transfer_id = t.stock_transfer_id)::int AS LineCount,
               t.dispatched_at AT TIME ZONE 'utc' AS DispatchedAt, t.dispatched_by AS DispatchedBy,
               t.received_at AT TIME ZONE 'utc' AS ReceivedAt, t.received_by AS ReceivedBy, t.remarks AS Remarks,
               t.created_at AT TIME ZONE 'utc' AS CreatedAt, t.created_by AS CreatedBy,
               t.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM inventory.stock_transfer t
        JOIN inventory.warehouse fw ON fw.warehouse_id = t.from_warehouse_id
        JOIN inventory.warehouse tw ON tw.warehouse_id = t.to_warehouse_id
        """;

    private const string TransferFilter = """
        WHERE t.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR t.transfer_no ILIKE @Pattern OR t.remarks ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR t.status = @Status)
          AND (CAST(@WarehouseId AS int) IS NULL OR t.from_warehouse_id = @WarehouseId OR t.to_warehouse_id = @WarehouseId)
          AND (CAST(@From AS date) IS NULL OR t.transfer_date >= @From)
          AND (CAST(@To AS date) IS NULL OR t.transfer_date <= @To)
        """;

    public async Task<PagedResult<StockTransfer>> GetTransfersAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<StockTransfer>(connection,
            $"SELECT count(*) FROM inventory.stock_transfer t {TransferFilter}",
            $"{TransferSelect} {TransferFilter} ORDER BY t.transfer_date DESC, t.stock_transfer_id DESC",
            QueryParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<StockTransfer?> GetTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var transfer = await connection.QuerySingleOrDefaultAsync<StockTransfer>(new CommandDefinition(
            $"{TransferSelect} WHERE t.organization_id = @Org AND t.stock_transfer_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (transfer is null)
            return null;

        transfer.Lines = (await connection.QueryAsync<StockTransferLine>(new CommandDefinition($"""
            SELECT l.stock_transfer_line_id AS Id, l.line_no AS LineNo,
                   l.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                   p.tracking_type AS TrackingType, p.has_expiry AS HasExpiry, l.unit_id AS UnitId, u.unit_code AS UnitCode,
                   bu.unit_code AS BaseUnitCode, l.conversion_factor AS ConversionFactor, l.quantity AS Quantity,
                   l.base_quantity AS BaseQuantity, l.batch_id AS BatchId, b.batch_no AS BatchNo,
                   b.manufacture_date AS ManufactureDate, b.expiry_date AS ExpiryDate, l.unit_cost AS UnitCost,
                   l.received_base_quantity AS ReceivedBaseQuantity, l.remarks AS Remarks
            FROM inventory.stock_transfer_line l
            JOIN inventory.product p ON p.product_id = l.product_id
            JOIN inventory.unit u ON u.unit_id = l.unit_id
            JOIN inventory.unit bu ON bu.unit_id = p.base_unit_id
            LEFT JOIN inventory.batch b ON b.batch_id = l.batch_id
            WHERE l.stock_transfer_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        await AttachSerialsAsync(connection, "inventory.stock_transfer_serial", "stock_transfer_line_id", transfer.Lines, cancellationToken);
        return transfer;
    }

    public async Task<long> SaveTransferAsync(int organizationId, StockTransfer transfer, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = new
        {
            Org = organizationId,
            transfer.TransferDate,
            transfer.FromWarehouseId,
            transfer.ToWarehouseId,
            transfer.Remarks,
            UserId = userId
        };

        long id;
        if (transfer.Id == 0)
        {
            var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.StockTransfer, cancellationToken);
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO inventory.stock_transfer (organization_id, transfer_no, transfer_date, from_warehouse_id,
                                                      to_warehouse_id, status, remarks, created_at, created_by)
                VALUES (@Org, @Number, @TransferDate, @FromWarehouseId, @ToWarehouseId, 'Draft', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING stock_transfer_id
                """, new { header.Org, Number = number, header.TransferDate, header.FromWarehouseId, header.ToWarehouseId, header.Remarks, header.UserId },
                transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = transfer.Id;
            await LockDraftAsync(connection, transaction, "inventory.stock_transfer", "stock_transfer_id", "transfer_no",
                "transfer_date", "from_warehouse_id", organizationId, id, "transfer", cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE inventory.stock_transfer
                SET transfer_date = @TransferDate, from_warehouse_id = @FromWarehouseId, to_warehouse_id = @ToWarehouseId,
                    remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE stock_transfer_id = @Id;
                DELETE FROM inventory.stock_transfer_line WHERE stock_transfer_id = @Id;
                """, new { header.TransferDate, header.FromWarehouseId, header.ToWarehouseId, header.Remarks, header.UserId, Id = id },
                transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in transfer.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO inventory.stock_transfer_line (stock_transfer_id, line_no, product_id, unit_id, conversion_factor,
                                                           quantity, batch_id, remarks)
                VALUES (@DocumentId, @LineNo, @ProductId, @UnitId, @ConversionFactor, @Quantity, @BatchId, @Remarks)
                RETURNING stock_transfer_line_id
                """, new { DocumentId = id, line.LineNo, line.ProductId, line.UnitId, line.ConversionFactor, line.Quantity, line.BatchId, line.Remarks },
                transaction, cancellationToken: cancellationToken));
            await InsertSerialsAsync(connection, transaction, "inventory.stock_transfer_serial", "stock_transfer_line_id",
                lineId, line.SerialNos, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<DeleteOutcome> DeleteTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => await DeleteDraftAsync("inventory.stock_transfer", "stock_transfer_id", organizationId, id, cancellationToken);

    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction, DocumentHeader Header, int ToWarehouseId)> LockTransferAsync(
        int organizationId, long id, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "inventory.stock_transfer", "stock_transfer_id", "transfer_no",
            "transfer_date", "from_warehouse_id", organizationId, id, cancellationToken)
            ?? throw new BusinessRuleException("The transfer doesn't exist.");
        var to = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT to_warehouse_id FROM inventory.stock_transfer WHERE stock_transfer_id = @Id", new { Id = id }, transaction,
            cancellationToken: cancellationToken));
        return (connection, transaction, header, to);
    }

    private const string TransferPostingLines = """
        SELECT l.stock_transfer_line_id AS Id, l.product_id AS ProductId, p.tracking_type AS TrackingType,
               l.base_quantity AS BaseQuantity, l.batch_id AS BatchId, l.unit_cost AS UnitCost,
               l.received_base_quantity AS ReceivedBaseQuantity
        FROM inventory.stock_transfer_line l JOIN inventory.product p ON p.product_id = l.product_id
        WHERE l.stock_transfer_id = @Id ORDER BY l.line_no
        """;

    public async Task DispatchTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var (connection, transaction, header, _) = await LockTransferAsync(organizationId, id, cancellationToken);
        await using (connection)
        await using (transaction)
        {
            if (header.Status != DocumentStatus.Draft)
                throw new BusinessRuleException("Only a draft transfer can be dispatched.");
            var lines = await PostingLinesAsync(connection, transaction, TransferPostingLines, id, cancellationToken);
            if (lines.Count == 0)
                throw new BusinessRuleException("Add at least one line before dispatching.");

            var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
            var serials = await SerialsByLineAsync(connection, transaction, "inventory.stock_transfer_serial",
                "stock_transfer_line_id", lines, cancellationToken);
            var moves = new List<StockMove>();
            foreach (var line in lines)
            {
                if (line.TrackingType == "Batch" && line.BatchId is null)
                    throw new BusinessRuleException("Choose the batch for every batch-tracked line.");
                IReadOnlyList<long> serialIds = [];
                if (line.TrackingType == "Serial")
                {
                    var lineSerials = serials[line.Id].ToList();
                    serialIds = await StockItems.TakeSerialsAsync(connection, transaction, userId, line.ProductId,
                        header.WarehouseId, lineSerials, "InTransit", cancellationToken);
                    await LinkSerialsAsync(connection, transaction, "inventory.stock_transfer_serial", "stock_transfer_line_id",
                        line.Id, lineSerials, serialIds, cancellationToken);
                }
                moves.Add(new StockMove(header.WarehouseId, line.ProductId, line.BatchId, -line.BaseQuantity, "TransferOut",
                    line.Id, SerialNumberIds: serialIds));
            }

            var applied = await new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate,
                DocumentType.StockTransfer, id, header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);
            for (var i = 0; i < lines.Count; i++)
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE inventory.stock_transfer_line SET unit_cost = @UnitCost WHERE stock_transfer_line_id = @Id",
                    new { Id = lines[i].Id, UnitCost = Math.Round(applied[i].UnitCost, 6) }, transaction,
                    cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE inventory.stock_transfer
                SET status = 'InTransit', dispatched_at = {SecurityDb.NowUtc}, dispatched_by = @UserId,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE stock_transfer_id = @Id
                """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task ReceiveTransferAsync(int organizationId, long id, StockTransferReceiveRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var (connection, transaction, header, toWarehouseId) = await LockTransferAsync(organizationId, id, cancellationToken);
        await using (connection)
        await using (transaction)
        {
            if (header.Status != "InTransit")
                throw new BusinessRuleException("Only a dispatched (in transit) transfer can be received.");
            var lines = await PostingLinesAsync(connection, transaction, TransferPostingLines, id, cancellationToken);
            var received = request.Lines.ToDictionary(l => l.LineId, l => l.ReceivedBaseQuantity);
            if (received.Keys.Any(lineId => lines.All(l => l.Id != lineId)))
                throw new BusinessRuleException("One of the received lines isn't on this transfer.");

            var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
            var serials = await SerialsByLineAsync(connection, transaction, "inventory.stock_transfer_serial",
                "stock_transfer_line_id", lines, cancellationToken, resolvedIds: true);
            var moves = new List<StockMove>();
            foreach (var line in lines)
            {
                var quantity = received.TryGetValue(line.Id, out var value) ? value : line.BaseQuantity;
                if (quantity > line.BaseQuantity)
                    throw new BusinessRuleException("A line can't receive more than was dispatched.");
                if (line.TrackingType == "Serial" && quantity != line.BaseQuantity)
                    throw new BusinessRuleException("Serial-tracked lines are received in full.");

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE inventory.stock_transfer_line SET received_base_quantity = @Quantity WHERE stock_transfer_line_id = @Id",
                    new { Id = line.Id, Quantity = quantity }, transaction, cancellationToken: cancellationToken));
                if (quantity == 0)
                    continue;

                var serialIds = serials[line.Id].Select(long.Parse).ToList();
                await StockItems.SetSerialsAsync(connection, transaction, userId, serialIds, "InStock", toWarehouseId, cancellationToken);
                moves.Add(new StockMove(toWarehouseId, line.ProductId, line.BatchId, quantity, "TransferIn", line.Id,
                    UnitCost: line.UnitCost ?? 0, SerialNumberIds: serialIds));
            }

            await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
                DocumentType.StockTransfer, id, header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE inventory.stock_transfer
                SET status = 'Received', received_at = {SecurityDb.NowUtc}, received_by = @UserId,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE stock_transfer_id = @Id
                """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task CancelTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var (connection, transaction, header, _) = await LockTransferAsync(organizationId, id, cancellationToken);
        await using (connection)
        await using (transaction)
        {
            if (header.Status is not ("InTransit" or "Received"))
                throw new BusinessRuleException(header.Status == DocumentStatus.Draft
                    ? "Delete a draft transfer instead of cancelling it."
                    : "This transfer is already cancelled.");

            var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
            var serialIds = await StockItems.MovedSerialsAsync(connection, transaction, organizationId,
                DocumentType.StockTransfer, id, cancellationToken);
            var ledger = new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
                DocumentType.StockTransfer, id, header.Number, settings.AllowNegativeStock);
            // Take the stock back out of the destination first, then return it to the source.
            await ledger.ReverseAsync(["TransferIn"], cancellationToken);
            await ledger.ReverseAsync(["TransferOut"], cancellationToken);
            await StockItems.RestoreSerialsAsync(connection, transaction, userId, serialIds, header.WarehouseId, cancellationToken);
            await SetStatusAsync(connection, transaction, "inventory.stock_transfer", "stock_transfer_id", id,
                DocumentStatus.Cancelled, userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    // =====================================================================
    // Stock counts
    // =====================================================================

    private const string CountSelect = """
        SELECT c.stock_count_id AS Id, c.count_no AS CountNo, c.count_date AS CountDate, c.warehouse_id AS WarehouseId,
               w.warehouse_name AS WarehouseName, c.category_id AS CategoryId, cat.category_name AS CategoryName,
               c.status AS Status,
               (SELECT count(*) FROM inventory.stock_count_line l WHERE l.stock_count_id = c.stock_count_id)::int AS LineCount,
               (SELECT count(*) FROM inventory.stock_count_line l
                WHERE l.stock_count_id = c.stock_count_id AND l.counted_quantity IS NOT NULL)::int AS CountedLineCount,
               coalesce((SELECT round(sum(l.difference_quantity * l.unit_cost), 4) FROM inventory.stock_count_line l
                         WHERE l.stock_count_id = c.stock_count_id), 0) AS DifferenceValue,
               a.stock_adjustment_id AS StockAdjustmentId, a.adjustment_no AS StockAdjustmentNo,
               c.started_at AT TIME ZONE 'utc' AS StartedAt, c.posted_at AT TIME ZONE 'utc' AS PostedAt,
               c.posted_by AS PostedBy, c.remarks AS Remarks,
               c.created_at AT TIME ZONE 'utc' AS CreatedAt, c.created_by AS CreatedBy
        FROM inventory.stock_count c
        JOIN inventory.warehouse w ON w.warehouse_id = c.warehouse_id
        LEFT JOIN inventory.category cat ON cat.category_id = c.category_id
        LEFT JOIN inventory.stock_adjustment a ON a.stock_count_id = c.stock_count_id AND a.status = 'Posted'
        """;

    private const string CountFilter = """
        WHERE c.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR c.count_no ILIKE @Pattern OR c.remarks ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR c.status = @Status)
          AND (CAST(@WarehouseId AS int) IS NULL OR c.warehouse_id = @WarehouseId)
          AND (CAST(@From AS date) IS NULL OR c.count_date >= @From)
          AND (CAST(@To AS date) IS NULL OR c.count_date <= @To)
        """;

    public async Task<PagedResult<StockCount>> GetCountsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<StockCount>(connection,
            $"SELECT count(*) FROM inventory.stock_count c {CountFilter}",
            $"{CountSelect} {CountFilter} ORDER BY c.count_date DESC, c.stock_count_id DESC",
            QueryParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<StockCount?> GetCountAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var count = await connection.QuerySingleOrDefaultAsync<StockCount>(new CommandDefinition(
            $"{CountSelect} WHERE c.organization_id = @Org AND c.stock_count_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (count is null)
            return null;

        count.Lines = (await connection.QueryAsync<StockCountLine>(new CommandDefinition("""
            SELECT l.stock_count_line_id AS Id, l.product_id AS ProductId, p.product_code AS ProductCode,
                   p.product_name AS ProductName, u.unit_code AS BaseUnitCode, l.batch_id AS BatchId, b.batch_no AS BatchNo,
                   b.expiry_date AS ExpiryDate, l.system_quantity AS SystemQuantity, l.counted_quantity AS CountedQuantity,
                   l.difference_quantity AS DifferenceQuantity, l.unit_cost AS UnitCost, l.remarks AS Remarks
            FROM inventory.stock_count_line l
            JOIN inventory.product p ON p.product_id = l.product_id
            JOIN inventory.unit u ON u.unit_id = p.base_unit_id
            LEFT JOIN inventory.batch b ON b.batch_id = l.batch_id
            WHERE l.stock_count_id = @Id
            ORDER BY p.product_name, b.expiry_date NULLS LAST, l.stock_count_line_id
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return count;
    }

    public async Task<long> CreateCountAsync(
        int organizationId, StockCountCreateRequest request, IReadOnlyCollection<int>? categoryIds, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.StockCount, cancellationToken);
        var id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO inventory.stock_count (organization_id, count_no, count_date, warehouse_id, category_id, status,
                                               started_at, remarks, created_at, created_by)
            VALUES (@Org, @Number, @CountDate, @WarehouseId, @CategoryId, 'InProgress', {SecurityDb.NowUtc}, @Remarks,
                    {SecurityDb.NowUtc}, @UserId)
            RETURNING stock_count_id
            """, new { Org = organizationId, Number = number, request.CountDate, request.WarehouseId, request.CategoryId, request.Remarks, UserId = userId },
            transaction, cancellationToken: cancellationToken));

        // The expected quantities, frozen now. Serial-tracked products are counted by serial number
        // and adjusted on a stock adjustment instead, so they are left out.
        var lines = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO inventory.stock_count_line (stock_count_id, product_id, batch_id, system_quantity, unit_cost)
            SELECT @Id, b.product_id, b.batch_id, b.quantity, CASE WHEN b.average_cost > 0 THEN b.average_cost ELSE p.last_purchase_cost END
            FROM inventory.stock_balance b
            JOIN inventory.product p ON p.product_id = b.product_id
            WHERE b.organization_id = @Org AND b.warehouse_id = @WarehouseId AND b.quantity <> 0
              AND p.product_type = 'Stock' AND p.tracking_type <> 'Serial'
              AND (CAST(@CategoryIds AS int[]) IS NULL OR p.category_id = ANY(@CategoryIds));

            INSERT INTO inventory.stock_count_line (stock_count_id, product_id, batch_id, system_quantity, unit_cost)
            SELECT @Id, p.product_id, NULL, 0, p.last_purchase_cost
            FROM inventory.product p
            WHERE @IncludeZero AND p.organization_id = @Org AND p.is_active AND p.product_type = 'Stock'
              AND p.tracking_type = 'None'
              AND (CAST(@CategoryIds AS int[]) IS NULL OR p.category_id = ANY(@CategoryIds))
              AND NOT EXISTS (SELECT 1 FROM inventory.stock_balance b
                              WHERE b.product_id = p.product_id AND b.warehouse_id = @WarehouseId AND b.quantity <> 0);
            """, new
            {
                Id = id,
                Org = organizationId,
                request.WarehouseId,
                CategoryIds = categoryIds?.ToArray(),
                IncludeZero = request.IncludeZeroStock
            }, transaction, cancellationToken: cancellationToken));
        if (lines == 0)
            throw new BusinessRuleException("Nothing to count: the warehouse has no stock in this scope. Tick \"Include products with no stock\" to count them anyway.");

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task UpdateCountAsync(int organizationId, long id, StockCountUpdateRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "inventory.stock_count", "stock_count_id", "count_no", "count_date",
            "warehouse_id", organizationId, id, cancellationToken) ?? throw new BusinessRuleException("The stock count doesn't exist.");
        if (header.Status != "InProgress")
            throw new BusinessRuleException("Only a count in progress can be changed.");

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE inventory.stock_count_line SET counted_quantity = @CountedQuantity, remarks = @Remarks
            WHERE stock_count_line_id = @LineId AND stock_count_id = @Id
            """, request.Lines.Select(l => new { Id = id, l.LineId, l.CountedQuantity, l.Remarks }), transaction,
            cancellationToken: cancellationToken));
        if (updated != request.Lines.Count)
            throw new BusinessRuleException("One of the lines isn't on this count.");
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.stock_count SET remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE stock_count_id = @Id
            """, new { Id = id, request.Remarks, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PostCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "inventory.stock_count", "stock_count_id", "count_no", "count_date",
            "warehouse_id", organizationId, id, cancellationToken) ?? throw new BusinessRuleException("The stock count doesn't exist.");
        if (header.Status != "InProgress")
            throw new BusinessRuleException("Only a count in progress can be posted.");

        var differences = (await connection.QueryAsync<(long LineId, int ProductId, int BaseUnitId, long? BatchId, decimal Difference, decimal UnitCost)>(
            new CommandDefinition("""
                SELECT l.stock_count_line_id, l.product_id, p.base_unit_id, l.batch_id, l.difference_quantity, l.unit_cost
                FROM inventory.stock_count_line l JOIN inventory.product p ON p.product_id = l.product_id
                WHERE l.stock_count_id = @Id AND l.counted_quantity IS NOT NULL AND l.difference_quantity <> 0
                ORDER BY l.stock_count_line_id
                """, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

        if (differences.Count > 0)
        {
            var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.StockAdjustment, cancellationToken);
            var adjustmentId = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO inventory.stock_adjustment (organization_id, adjustment_no, adjustment_date, warehouse_id, reason,
                                                        stock_count_id, status, remarks, created_at, created_by)
                VALUES (@Org, @Number, @Date, @WarehouseId, 'CountDifference', @CountId, 'Draft', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING stock_adjustment_id
                """, new
                {
                    Org = organizationId,
                    Number = number,
                    Date = header.DocumentDate,
                    header.WarehouseId,
                    CountId = id,
                    Remarks = $"Stock count {header.Number}",
                    UserId = userId
                }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO inventory.stock_adjustment_line (stock_adjustment_id, line_no, product_id, unit_id, conversion_factor,
                                                             quantity, batch_id, unit_cost)
                VALUES (@AdjustmentId, @LineNo, @ProductId, @UnitId, 1, @Quantity, @BatchId, @UnitCost)
                """, differences.Select((d, index) => new
                {
                    AdjustmentId = adjustmentId,
                    LineNo = index + 1,
                    d.ProductId,
                    UnitId = d.BaseUnitId,
                    Quantity = d.Difference,
                    d.BatchId,
                    UnitCost = d.Difference > 0 ? d.UnitCost : (decimal?)null
                }), transaction, cancellationToken: cancellationToken));
            await PostAdjustmentCoreAsync(connection, transaction, organizationId, adjustmentId, userId, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE inventory.stock_count SET status = 'Posted', posted_at = {SecurityDb.NowUtc}, posted_by = @UserId,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE stock_count_id = @Id
            """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "inventory.stock_count", "stock_count_id", "count_no", "count_date",
            "warehouse_id", organizationId, id, cancellationToken) ?? throw new BusinessRuleException("The stock count doesn't exist.");
        if (header.Status != "InProgress")
            throw new BusinessRuleException(header.Status == DocumentStatus.Posted
                ? "A posted count can't be cancelled; cancel the stock adjustment it created instead."
                : "This count is already cancelled.");
        await SetStatusAsync(connection, transaction, "inventory.stock_count", "stock_count_id", id, DocumentStatus.Cancelled, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // =====================================================================
    // Shared helpers
    // =====================================================================

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<DocumentHeader?> LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, string numberColumn,
        string dateColumn, string warehouseColumn, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<DocumentHeader>(new CommandDefinition($"""
            SELECT {key} AS Id, {numberColumn} AS Number, {dateColumn} AS DocumentDate, status AS Status,
                   {warehouseColumn} AS WarehouseId
            FROM {table} WHERE organization_id = @Org AND {key} = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken));

    private static async Task<DocumentHeader> LockDraftAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, string numberColumn,
        string dateColumn, string warehouseColumn, int organizationId, long id, string label, CancellationToken cancellationToken)
    {
        var header = await LockAsync(connection, transaction, table, key, numberColumn, dateColumn, warehouseColumn,
            organizationId, id, cancellationToken) ?? throw new BusinessRuleException($"The {label} doesn't exist.");
        if (header.Status != DocumentStatus.Draft)
            throw new BusinessRuleException($"Only a draft {label} can be changed or posted; this one is {header.Status.ToLowerInvariant()}.");
        return header;
    }

    private async Task<DeleteOutcome> DeleteDraftAsync(string table, string key, int organizationId, long id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            $"SELECT status FROM {table} WHERE organization_id = @Org AND {key} = @Id", new { Org = organizationId, Id = id },
            cancellationToken: cancellationToken));
        if (status is null)
            return DeleteOutcome.NotFound;
        if (status != DocumentStatus.Draft)
            return DeleteOutcome.InUse;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            $"DELETE FROM {table} WHERE organization_id = @Org AND {key} = @Id AND status = 'Draft'",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    private static Task SetStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, long id, string status,
        int userId, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {table} SET status = @Status, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId WHERE {key} = @Id",
            new { Id = id, Status = status, UserId = userId }, transaction, cancellationToken: cancellationToken));

    private static async Task<List<PostingLine>> PostingLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, long id, CancellationToken cancellationToken)
        => (await connection.QueryAsync<PostingLine>(new CommandDefinition(sql, new { Id = id }, transaction,
            cancellationToken: cancellationToken))).AsList();

    /// <summary>Serial numbers per line (or, with resolvedIds, the serial_number ids as text).</summary>
    private static async Task<ILookup<long, string>> SerialsByLineAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string lineColumn,
        IReadOnlyCollection<PostingLine> lines, CancellationToken cancellationToken, bool resolvedIds = false)
        => (await connection.QueryAsync<(long LineId, string Value)>(new CommandDefinition(
            $"SELECT {lineColumn}, {(resolvedIds ? "serial_number_id::text" : "serial_no")} FROM {table} " +
            $"WHERE {lineColumn} = ANY(@Ids) {(resolvedIds ? "AND serial_number_id IS NOT NULL" : "")}",
            new { Ids = lines.Select(l => l.Id).ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.Value);

    private static async Task LinkSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string lineColumn, long lineId,
        IReadOnlyList<string> serialNos, IReadOnlyList<long> serialIds, CancellationToken cancellationToken)
    {
        // TakeSerials/ReceiveSerials return ids in no particular order: match them back by serial number.
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE {table} t SET serial_number_id = s.serial_number_id
            FROM inventory.serial_number s
            WHERE t.{lineColumn} = @LineId AND s.serial_number_id = ANY(@Ids) AND s.serial_no = t.serial_no
            """, new { LineId = lineId, Ids = serialIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        _ = serialNos;
    }

    private static async Task<long?> ResolveBatchAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, PostingLine line,
        bool incoming, CancellationToken cancellationToken)
    {
        if (line.TrackingType != "Batch")
            return null;
        if (line.BatchId is not null)
            return line.BatchId;
        if (!incoming || string.IsNullOrWhiteSpace(line.BatchNo))
            throw new BusinessRuleException("Choose the batch for every batch-tracked line.");
        return await StockItems.EnsureBatchAsync(connection, transaction, organizationId, userId, line.ProductId,
            line.BatchNo, line.ManufactureDate, line.ExpiryDate, cancellationToken);
    }

    /// <summary>On cancelling an adjustment: serials it brought in leave stock; serials it took out come back.</summary>
    private static async Task RestoreSerialsOnCancelAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, string documentType,
        long documentId, int warehouseId, CancellationToken cancellationToken)
    {
        var moved = (await connection.QueryAsync<(long SerialId, bool Incoming)>(new CommandDefinition("""
            SELECT s.serial_number_id, m.quantity > 0
            FROM inventory.stock_movement m
            JOIN inventory.stock_movement_serial s ON s.stock_movement_id = m.stock_movement_id
            WHERE m.organization_id = @Org AND m.source_document_type = @Type AND m.source_document_id = @Id
              AND m.reverses_stock_movement_id IS NULL
            """, new { Org = organizationId, Type = documentType, Id = documentId }, transaction,
            cancellationToken: cancellationToken))).AsList();
        await StockItems.SetSerialsAsync(connection, transaction, userId,
            moved.Where(m => m.Incoming).Select(m => m.SerialId).ToList(), "Scrapped", null, cancellationToken);
        await StockItems.RestoreSerialsAsync(connection, transaction, userId,
            moved.Where(m => !m.Incoming).Select(m => m.SerialId).ToList(), warehouseId, cancellationToken);
    }
}
