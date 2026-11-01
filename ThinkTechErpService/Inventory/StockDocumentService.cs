using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Inventory;

public interface IStockDocumentService
{
    Task<PagedResult<StockAdjustment>> GetAdjustmentsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default);
    Task<StockAdjustment?> GetAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<StockAdjustment> CreateAdjustmentAsync(int organizationId, StockAdjustmentUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<StockAdjustment?> UpdateAdjustmentAsync(int organizationId, long id, StockAdjustmentUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<StockAdjustment?> PostAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<StockAdjustment?> CancelAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<StockTransfer>> GetTransfersAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default);
    Task<StockTransfer?> GetTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<StockTransfer> CreateTransferAsync(int organizationId, StockTransferUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<StockTransfer?> UpdateTransferAsync(int organizationId, long id, StockTransferUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<StockTransfer?> DispatchTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<StockTransfer?> ReceiveTransferAsync(int organizationId, long id, StockTransferReceiveRequest request, int userId, CancellationToken cancellationToken = default);
    Task<StockTransfer?> CancelTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<StockCount>> GetCountsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default);
    Task<StockCount?> GetCountAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<StockCount> CreateCountAsync(int organizationId, StockCountCreateRequest request, int userId, CancellationToken cancellationToken = default);
    Task<StockCount?> UpdateCountAsync(int organizationId, long id, StockCountUpdateRequest request, int userId, CancellationToken cancellationToken = default);
    Task<StockCount?> PostCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<StockCount?> CancelCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class StockDocumentService(
    IStockDocumentRepository documents,
    IProductRepository products,
    IInventorySetupRepository setup,
    IOwnershipRepository ownership) : IStockDocumentService
{
    // ---- Adjustments

    public Task<PagedResult<StockAdjustment>> GetAdjustmentsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetAdjustmentsAsync(organizationId, query, cancellationToken);
    }

    public Task<StockAdjustment?> GetAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetAdjustmentAsync(organizationId, id, cancellationToken);

    public async Task<StockAdjustment> CreateAdjustmentAsync(int organizationId, StockAdjustmentUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var adjustment = await BuildAdjustmentAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveAdjustmentAsync(organizationId, adjustment, userId, cancellationToken));
        return (await documents.GetAdjustmentAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<StockAdjustment?> UpdateAdjustmentAsync(int organizationId, long id, StockAdjustmentUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetAdjustmentAsync(organizationId, id, cancellationToken) is null)
            return null;
        var adjustment = await BuildAdjustmentAsync(organizationId, request, cancellationToken);
        adjustment.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveAdjustmentAsync(organizationId, adjustment, userId, cancellationToken));
        return await documents.GetAdjustmentAsync(organizationId, id, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteAdjustmentAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteAdjustmentAsync(organizationId, id, cancellationToken);

    public async Task<StockAdjustment?> PostAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetAdjustmentAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(() => documents.PostAdjustmentAsync(organizationId, id, userId, cancellationToken));
        return await documents.GetAdjustmentAsync(organizationId, id, cancellationToken);
    }

    public async Task<StockAdjustment?> CancelAdjustmentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetAdjustmentAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(() => documents.CancelAdjustmentAsync(organizationId, id, userId, cancellationToken));
        return await documents.GetAdjustmentAsync(organizationId, id, cancellationToken);
    }

    private async Task<StockAdjustment> BuildAdjustmentAsync(int organizationId, StockAdjustmentUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!StockAdjustmentReasons.All.Contains(request.Reason) || request.Reason == StockAdjustmentReasons.CountDifference)
            throw new BusinessRuleException("Choose a reason: Opening, Damage, Expired, Lost, InternalUse, Sample or Other.");
        await ownership.EnsureAsync(organizationId, "warehouse", [request.WarehouseId], cancellationToken);

        var opening = request.Reason == StockAdjustmentReasons.Opening;
        if (opening && request.Lines.Any(l => l.Quantity <= 0))
            throw new BusinessRuleException("Opening stock lines add stock: every quantity must be more than 0.");
        if (opening && request.Lines.Any(l => l.UnitCost is null))
            throw new BusinessRuleException("Enter the cost of every opening stock line.");

        return new StockAdjustment
        {
            AdjustmentDate = request.AdjustmentDate,
            WarehouseId = request.WarehouseId,
            Reason = request.Reason,
            Remarks = Text.Clean(request.Remarks),
            Lines = await BuildLinesAsync(organizationId, request.Lines, signed: true, cancellationToken)
        };
    }

    /// <summary>Checks lines against their products and converts quantities to base units.</summary>
    private async Task<List<StockLine>> BuildLinesAsync(
        int organizationId, IReadOnlyList<StockLineRequest> requests, bool signed, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            throw new BusinessRuleException("Add at least one line.");
        var byId = await products.GetForLinesAsync(organizationId, requests.Select(r => r.ProductId), cancellationToken);
        var lines = new List<StockLine>();
        foreach (var (request, index) in requests.Select((r, i) => (r, i)))
        {
            var label = $"Line {index + 1}";
            if (!byId.TryGetValue(request.ProductId, out var product))
                throw new BusinessRuleException($"{label}: the product doesn't exist.");
            label = $"{label} ({product.Name})";
            if (product.ProductType != "Stock")
                throw new BusinessRuleException($"{label}: only stock products move in stock documents.");
            var unit = product.Units.FirstOrDefault(u => u.UnitId == request.UnitId)
                ?? throw new BusinessRuleException($"{label}: the unit isn't one of the product's units.");
            if (request.Quantity == 0 || (!signed && request.Quantity < 0))
                throw new BusinessRuleException($"{label}: enter a quantity{(signed ? " (negative to remove stock)" : " more than 0")}.");

            var baseQuantity = TaxCalculator.Round(request.Quantity * unit.ConversionFactor);
            var incoming = request.Quantity > 0 && signed;
            var serialNos = request.SerialNos.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            var line = new StockLine
            {
                LineNo = index + 1,
                ProductId = product.Id,
                TrackingType = product.TrackingType,
                UnitId = unit.UnitId,
                ConversionFactor = unit.ConversionFactor,
                Quantity = request.Quantity,
                BaseQuantity = baseQuantity,
                UnitCost = request.UnitCost is null ? null : Math.Round(request.UnitCost.Value / unit.ConversionFactor, 6),
                Remarks = Text.Clean(request.Remarks)
            };

            if (product.TrackingType == "Batch")
            {
                if (request.BatchId is not null)
                    line.BatchId = request.BatchId;
                else if (incoming && Text.Clean(request.BatchNo) is { } batchNo)
                {
                    if (product.HasExpiry && request.ExpiryDate is null)
                        throw new BusinessRuleException($"{label}: enter the batch's expiry date.");
                    line.BatchNo = batchNo;
                    line.ManufactureDate = request.ManufactureDate;
                    line.ExpiryDate = request.ExpiryDate;
                }
                else
                    throw new BusinessRuleException(incoming
                        ? $"{label}: enter a batch number (or choose an existing batch)."
                        : $"{label}: choose the batch the stock comes from.");
            }

            if (product.TrackingType == "Serial")
            {
                if (baseQuantity != Math.Truncate(baseQuantity) || serialNos.Count != Math.Abs(baseQuantity))
                    throw new BusinessRuleException($"{label}: enter one serial number per item ({Math.Abs(baseQuantity):0.####}).");
                line.SerialNos = serialNos;
            }

            lines.Add(line);
        }

        var batches = lines.Where(l => l.BatchId is not null).Select(l => (l.BatchId!.Value, l.ProductId));
        if (!await ownership.BatchesBelongToAsync(organizationId, batches, cancellationToken))
            throw new BusinessRuleException("A chosen batch doesn't belong to its line's product.");
        return lines;
    }

    // ---- Transfers

    public Task<PagedResult<StockTransfer>> GetTransfersAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetTransfersAsync(organizationId, query, cancellationToken);
    }

    public Task<StockTransfer?> GetTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetTransferAsync(organizationId, id, cancellationToken);

    public async Task<StockTransfer> CreateTransferAsync(int organizationId, StockTransferUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var transfer = await BuildTransferAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveTransferAsync(organizationId, transfer, userId, cancellationToken));
        return (await documents.GetTransferAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<StockTransfer?> UpdateTransferAsync(int organizationId, long id, StockTransferUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetTransferAsync(organizationId, id, cancellationToken) is null)
            return null;
        var transfer = await BuildTransferAsync(organizationId, request, cancellationToken);
        transfer.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveTransferAsync(organizationId, transfer, userId, cancellationToken));
        return await documents.GetTransferAsync(organizationId, id, cancellationToken);
    }

    private async Task<StockTransfer> BuildTransferAsync(int organizationId, StockTransferUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.FromWarehouseId == request.ToWarehouseId)
            throw new BusinessRuleException("Choose two different warehouses.");
        await ownership.EnsureAsync(organizationId, "warehouse", [request.FromWarehouseId, request.ToWarehouseId], cancellationToken);
        var lines = await BuildLinesAsync(organizationId, request.Lines, signed: false, cancellationToken);
        return new StockTransfer
        {
            TransferDate = request.TransferDate,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            Remarks = Text.Clean(request.Remarks),
            Lines = lines.Select(l => new StockTransferLine
            {
                LineNo = l.LineNo,
                ProductId = l.ProductId,
                TrackingType = l.TrackingType,
                UnitId = l.UnitId,
                ConversionFactor = l.ConversionFactor,
                Quantity = l.Quantity,
                BaseQuantity = l.BaseQuantity,
                BatchId = l.BatchId,
                SerialNos = l.SerialNos,
                Remarks = l.Remarks
            }).ToList()
        };
    }

    public Task<DeleteOutcome> DeleteTransferAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteTransferAsync(organizationId, id, cancellationToken);

    public Task<StockTransfer?> DispatchTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => TransferActionAsync(organizationId, id, () => documents.DispatchTransferAsync(organizationId, id, userId, cancellationToken), cancellationToken);

    public Task<StockTransfer?> ReceiveTransferAsync(int organizationId, long id, StockTransferReceiveRequest request, int userId, CancellationToken cancellationToken = default)
        => TransferActionAsync(organizationId, id, () => documents.ReceiveTransferAsync(organizationId, id, request, userId, cancellationToken), cancellationToken);

    public Task<StockTransfer?> CancelTransferAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => TransferActionAsync(organizationId, id, () => documents.CancelTransferAsync(organizationId, id, userId, cancellationToken), cancellationToken);

    private async Task<StockTransfer?> TransferActionAsync(int organizationId, long id, Func<Task> action, CancellationToken cancellationToken)
    {
        if (await documents.GetTransferAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(action);
        return await documents.GetTransferAsync(organizationId, id, cancellationToken);
    }

    // ---- Counts

    public Task<PagedResult<StockCount>> GetCountsAsync(int organizationId, StockDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetCountsAsync(organizationId, query, cancellationToken);
    }

    public Task<StockCount?> GetCountAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetCountAsync(organizationId, id, cancellationToken);

    public async Task<StockCount> CreateCountAsync(int organizationId, StockCountCreateRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await ownership.EnsureAsync(organizationId, "warehouse", [request.WarehouseId], cancellationToken);
        IReadOnlyCollection<int>? categories = null;
        if (request.CategoryId is not null)
        {
            categories = await setup.GetCategoryTreeIdsAsync(organizationId, request.CategoryId.Value, cancellationToken);
            if (categories.Count == 0)
                throw new BusinessRuleException("The category doesn't exist.");
        }
        request.Remarks = Text.Clean(request.Remarks);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.CreateCountAsync(organizationId, request, categories, userId, cancellationToken));
        return (await documents.GetCountAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<StockCount?> UpdateCountAsync(int organizationId, long id, StockCountUpdateRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetCountAsync(organizationId, id, cancellationToken) is null)
            return null;
        request.Remarks = Text.Clean(request.Remarks);
        await BusinessDbErrors.ExecuteAsync(() => documents.UpdateCountAsync(organizationId, id, request, userId, cancellationToken));
        return await documents.GetCountAsync(organizationId, id, cancellationToken);
    }

    public async Task<StockCount?> PostCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetCountAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(() => documents.PostCountAsync(organizationId, id, userId, cancellationToken));
        return await documents.GetCountAsync(organizationId, id, cancellationToken);
    }

    public async Task<StockCount?> CancelCountAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetCountAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(() => documents.CancelCountAsync(organizationId, id, userId, cancellationToken));
        return await documents.GetCountAsync(organizationId, id, cancellationToken);
    }
}
