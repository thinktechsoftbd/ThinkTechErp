using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Common;

/// <summary>
/// A change to one stock_balance row. Quantity is signed, in base units. UnitCost is the cost per base
/// unit, required for stock coming in (stock going out uses the average cost unless given). ValueChange
/// is for value-only moves (MovementType CostAdjustment, Quantity 0): added to the row's value.
/// </summary>
internal sealed record StockMove(
    int WarehouseId,
    int ProductId,
    long? BatchId,
    decimal Quantity,
    string MovementType,
    long? SourceLineId,
    decimal? UnitCost = null,
    decimal ValueChange = 0,
    IReadOnlyList<long>? SerialNumberIds = null,
    long? ReversesMovementId = null,
    string? Remarks = null);

internal sealed record AppliedMove(long MovementId, decimal UnitCost, decimal TotalCost);

/// <summary>
/// Posts stock movements for one document inside its transaction: keeps stock_balance (quantity and
/// moving weighted average cost) in step with the append-only stock_movement ledger. Rows are locked
/// in (warehouse, product, batch) order so concurrent posts can't deadlock.
/// </summary>
internal sealed class StockLedger(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    int organizationId,
    int userId,
    DateOnly movementDate,
    string documentType,
    long documentId,
    string documentNo,
    bool allowNegativeStock)
{
    private sealed record BalanceRow(long StockBalanceId, decimal Quantity, decimal AverageCost);

    /// <summary>Applies the moves in lock order; results are returned in the input's order.</summary>
    public async Task<IReadOnlyList<AppliedMove>> ApplyAsync(IReadOnlyList<StockMove> moves, CancellationToken cancellationToken)
    {
        var results = new AppliedMove[moves.Count];
        var order = Enumerable.Range(0, moves.Count)
            .OrderBy(i => moves[i].WarehouseId).ThenBy(i => moves[i].ProductId).ThenBy(i => moves[i].BatchId ?? 0);
        foreach (var index in order)
            results[index] = await ApplyOneAsync(moves[index], cancellationToken);
        return results;
    }

    private async Task<AppliedMove> ApplyOneAsync(StockMove move, CancellationToken cancellationToken)
    {
        var key = new { Org = organizationId, move.WarehouseId, move.ProductId, move.BatchId };
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.stock_balance (organization_id, warehouse_id, product_id, batch_id)
            VALUES (@Org, @WarehouseId, @ProductId, @BatchId)
            ON CONFLICT ON CONSTRAINT ux_stock_balance DO NOTHING
            """, key, transaction, cancellationToken: cancellationToken));
        var balance = await connection.QuerySingleAsync<BalanceRow>(new CommandDefinition("""
            SELECT stock_balance_id AS StockBalanceId, quantity AS Quantity, average_cost AS AverageCost
            FROM public.stock_balance
            WHERE warehouse_id = @WarehouseId AND product_id = @ProductId AND batch_id IS NOT DISTINCT FROM @BatchId
            FOR UPDATE
            """, key, transaction, cancellationToken: cancellationToken));

        var quantity = balance.Quantity;
        var average = balance.AverageCost;
        decimal unitCost, totalCost, newQuantity = quantity + move.Quantity, newAverage;

        if (move.Quantity == 0)
        {
            // Value only: spread over what is on hand (nothing to spread over: the value is not kept).
            unitCost = 0;
            totalCost = Round4(move.ValueChange);
            newAverage = quantity > 0 ? Math.Max(0, average + move.ValueChange / quantity) : average;
        }
        else if (move.Quantity > 0)
        {
            unitCost = move.UnitCost ?? average;
            totalCost = Round4(move.Quantity * unitCost);
            newAverage = quantity <= 0 ? unitCost : (quantity * average + move.Quantity * unitCost) / newQuantity;
        }
        else
        {
            if (newQuantity < 0 && !allowNegativeStock)
            {
                var label = await connection.QuerySingleAsync<string>(new CommandDefinition("""
                    SELECT p.product_code || ' ' || p.product_name || ' in ' || w.warehouse_name
                           || coalesce(' (batch ' || b.batch_no || ')', '')
                    FROM public.product p
                    JOIN public.warehouse w ON w.warehouse_id = @WarehouseId
                    LEFT JOIN public.batch b ON b.batch_id = @BatchId
                    WHERE p.product_id = @ProductId
                    """, key, transaction, cancellationToken: cancellationToken));
                throw new BusinessRuleException(
                    $"Not enough stock of {label}: {quantity:0.####} on hand, {-move.Quantity:0.####} needed.");
            }

            // A reversal takes stock out at the cost it came in at; anything else at the average.
            unitCost = move.UnitCost ?? average;
            totalCost = Round4(move.Quantity * unitCost);
            newAverage = move.UnitCost is not null && newQuantity > 0
                ? Math.Max(0, (quantity * average + move.Quantity * unitCost) / newQuantity)
                : average;
        }

        newAverage = Math.Round(newAverage, 6, MidpointRounding.AwayFromZero);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.stock_balance
            SET quantity = @Quantity, average_cost = @AverageCost, last_movement_at = {SecurityDb.NowUtc}
            WHERE stock_balance_id = @Id
            """, new { Id = balance.StockBalanceId, Quantity = newQuantity, AverageCost = newAverage },
            transaction, cancellationToken: cancellationToken));

        var movementId = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.stock_movement (
                organization_id, movement_date, warehouse_id, product_id, batch_id, movement_type, quantity,
                unit_cost, total_cost, balance_quantity, balance_average_cost, source_document_type,
                source_document_id, source_line_id, source_document_no, reverses_stock_movement_id, remarks,
                created_at, created_by)
            VALUES (
                @Org, @MovementDate, @WarehouseId, @ProductId, @BatchId, @MovementType, @Quantity,
                @UnitCost, @TotalCost, @BalanceQuantity, @BalanceAverageCost, @DocumentType,
                @DocumentId, @SourceLineId, @DocumentNo, @ReversesMovementId, @Remarks,
                {SecurityDb.NowUtc}, @UserId)
            RETURNING stock_movement_id
            """, new
            {
                Org = organizationId,
                MovementDate = movementDate,
                move.WarehouseId,
                move.ProductId,
                move.BatchId,
                move.MovementType,
                move.Quantity,
                UnitCost = Math.Round(unitCost, 6, MidpointRounding.AwayFromZero),
                TotalCost = totalCost,
                BalanceQuantity = newQuantity,
                BalanceAverageCost = newAverage,
                DocumentType = documentType,
                DocumentId = documentId,
                move.SourceLineId,
                DocumentNo = documentNo,
                move.ReversesMovementId,
                move.Remarks,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

        if (move.SerialNumberIds is { Count: > 0 })
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.stock_movement_serial (stock_movement_id, serial_number_id)
                SELECT @MovementId, unnest(@SerialIds)
                """, new { MovementId = movementId, SerialIds = move.SerialNumberIds.ToArray() },
                transaction, cancellationToken: cancellationToken));

        return new AppliedMove(movementId, unitCost, totalCost);
    }

    private sealed record PostedMovement(
        long StockMovementId, int WarehouseId, int ProductId, long? BatchId, string MovementType,
        decimal Quantity, decimal UnitCost, decimal TotalCost, long? SourceLineId);

    /// <summary>
    /// Cancels everything this document posted (optionally only some movement types): adds an
    /// opposite movement for each one not yet reversed. Serial statuses are the caller's to restore.
    /// </summary>
    public async Task ReverseAsync(IReadOnlyCollection<string>? movementTypes, CancellationToken cancellationToken)
    {
        var posted = (await connection.QueryAsync<PostedMovement>(new CommandDefinition("""
            SELECT m.stock_movement_id AS StockMovementId, m.warehouse_id AS WarehouseId, m.product_id AS ProductId,
                   m.batch_id AS BatchId, m.movement_type AS MovementType, m.quantity AS Quantity,
                   m.unit_cost AS UnitCost, m.total_cost AS TotalCost, m.source_line_id AS SourceLineId
            FROM public.stock_movement m
            WHERE m.organization_id = @Org AND m.source_document_type = @DocumentType
              AND m.source_document_id = @DocumentId AND m.reverses_stock_movement_id IS NULL
              AND (CAST(@Types AS text[]) IS NULL OR m.movement_type = ANY(@Types))
              AND NOT EXISTS (SELECT 1 FROM public.stock_movement r
                              WHERE r.reverses_stock_movement_id = m.stock_movement_id)
            ORDER BY m.stock_movement_id
            """, new
            {
                Org = organizationId,
                DocumentType = documentType,
                DocumentId = documentId,
                Types = movementTypes?.ToArray()
            }, transaction, cancellationToken: cancellationToken))).AsList();

        var serials = (await connection.QueryAsync<(long MovementId, long SerialId)>(new CommandDefinition("""
            SELECT stock_movement_id, serial_number_id FROM public.stock_movement_serial
            WHERE stock_movement_id = ANY(@Ids)
            """, new { Ids = posted.Select(p => p.StockMovementId).ToArray() }, transaction,
            cancellationToken: cancellationToken))).ToLookup(s => s.MovementId, s => s.SerialId);

        var moves = posted.Select(p => p.Quantity == 0
            ? new StockMove(p.WarehouseId, p.ProductId, p.BatchId, 0, p.MovementType, p.SourceLineId,
                ValueChange: -p.TotalCost, ReversesMovementId: p.StockMovementId, Remarks: "Cancelled")
            : new StockMove(p.WarehouseId, p.ProductId, p.BatchId, -p.Quantity, p.MovementType, p.SourceLineId,
                UnitCost: p.UnitCost, SerialNumberIds: serials[p.StockMovementId].ToList(),
                ReversesMovementId: p.StockMovementId, Remarks: "Cancelled")).ToList();
        await ApplyAsync(moves, cancellationToken);
    }

    private static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
