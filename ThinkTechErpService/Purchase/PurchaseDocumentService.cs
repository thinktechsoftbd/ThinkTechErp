using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Purchase;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Purchase;

public interface IPurchaseDocumentService
{
    Task<PagedResult<PurchaseInvoice>> GetInvoicesAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice> CreateInvoiceAsync(int organizationId, PurchaseInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice?> UpdateInvoiceAsync(int organizationId, long id, PurchaseInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice?> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice?> CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenPurchaseInvoice>> GetOpenInvoicesAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseOrder>> GetOrdersAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<PurchaseOrder> CreateOrderAsync(int organizationId, PurchaseOrderUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> UpdateOrderAsync(int organizationId, long id, PurchaseOrderUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> ChangeOrderStatusAsync(int organizationId, long id, string action, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<GoodsReceipt>> GetReceiptsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<GoodsReceipt> CreateReceiptAsync(int organizationId, GoodsReceiptUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> UpdateReceiptAsync(int organizationId, long id, GoodsReceiptUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> PostReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseReturn>> GetReturnsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<PurchaseReturn> CreateReturnAsync(int organizationId, PurchaseReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<PurchaseReturn?> UpdateReturnAsync(int organizationId, long id, PurchaseReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<PurchaseReturn?> PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<PurchaseReturn?> CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<SupplierPayment>> GetPaymentsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SupplierPayment?> GetPaymentAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<SupplierPayment> CreatePaymentAsync(int organizationId, SupplierPaymentUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SupplierPayment?> CancelPaymentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<SupplierPayment?> SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default);
}

public sealed class PurchaseDocumentService(
    IPurchaseDocumentRepository documents,
    IPurchaseSetupRepository setup,
    IProductRepository products,
    IInventorySetupRepository inventorySetup,
    IPaymentSourceRepository paymentSources,
    IOwnershipRepository ownership) : IPurchaseDocumentService
{
    private sealed record Context(
        InventorySetting Settings,
        IReadOnlyDictionary<int, ProductLookupItem> Products,
        IReadOnlyDictionary<int, TaxGroupOption> TaxGroups);

    /// <summary>How a document's lines are priced and whether they bring stock in.</summary>
    private sealed record LineMode(bool HasTax, bool ReceivesStock, bool TakesStock);

    private sealed class BuiltLine(PurchaseLine line, decimal cost, string productType)
    {
        public PurchaseLine Line { get; } = line;
        /// <summary>Net of discount and recoverable tax, in the document currency.</summary>
        public decimal Cost { get; } = cost;
        public string ProductType { get; } = productType;
    }

    // =====================================================================
    // Shared building blocks
    // =====================================================================

    private async Task<Context> LoadAsync(int organizationId, IReadOnlyList<PurchaseLineRequest> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line.");
        var settings = await inventorySetup.GetSettingAsync(organizationId, cancellationToken);
        var productMap = await products.GetForLinesAsync(organizationId, lines.Select(l => l.ProductId), cancellationToken);
        var groupIds = lines.Where(l => l.TaxGroupId is not null).Select(l => l.TaxGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? new Dictionary<int, TaxGroupOption>()
            : (await inventorySetup.GetTaxGroupOptionsAsync(organizationId, groupIds, cancellationToken)).ToDictionary(g => g.Id);
        if (groups.Count != groupIds.Count)
            throw new BusinessRuleException("One of the tax groups doesn't exist.");
        return new Context(settings, productMap, groups);
    }

    /// <summary>The document's currency (default: the base currency) and its exchange rate to the base.</summary>
    private async Task<(long CurrencyId, decimal Rate)> CurrencyAsync(Context context, long? currencyId, decimal rate, CancellationToken cancellationToken)
    {
        var baseCurrency = context.Settings.BaseCurrencyLookupDataId
            ?? throw new BusinessRuleException("Set the base currency in Inventory Settings first.");
        var id = currencyId ?? baseCurrency;
        if (id != baseCurrency && !await setup.LookupValueExistsAsync(id, SettingsRules.CurrencyLookupTable, cancellationToken))
            throw new BusinessRuleException("Choose a currency from the Currency lookup list.");
        return (id, id == baseCurrency ? 1 : rate);
    }

    private static BuiltLine BuildLine(PurchaseLineRequest request, int index, Context context, LineMode mode)
    {
        var label = $"Line {index + 1}";
        if (!context.Products.TryGetValue(request.ProductId, out var product))
            throw new BusinessRuleException($"{label}: the product doesn't exist.");
        label = $"{label} ({product.Name})";
        var unit = product.Units.FirstOrDefault(u => u.UnitId == request.UnitId)
            ?? throw new BusinessRuleException($"{label}: the unit isn't one of the product's units.");
        if ((mode.ReceivesStock || mode.TakesStock) && product.ProductType == "Service")
            throw new BusinessRuleException($"{label}: services can't be received or returned as stock.");

        var baseQuantity = TaxCalculator.Round(request.Quantity * unit.ConversionFactor);
        var gross = TaxCalculator.Round(request.Quantity * request.UnitPrice);
        var discount = request.DiscountAmount is { } fixedDiscount
            ? TaxCalculator.Round(fixedDiscount)
            : TaxCalculator.Round(gross * request.DiscountPercent / 100m);
        if (discount > gross)
            throw new BusinessRuleException($"{label}: the discount is more than the line amount.");
        var net = gross - discount;

        TaxGroupOption? group = null;
        if (request.TaxGroupId is not null)
            group = context.TaxGroups[request.TaxGroupId.Value];
        var tax = mode.HasTax ? TaxCalculator.Compute(net, group, context.Settings.PricesIncludeTax) : TaxCalculator.Compute(net, null, false);
        var inclusive = mode.HasTax && context.Settings.PricesIncludeTax;

        var line = new PurchaseLine
        {
            LineNo = index + 1,
            ProductId = product.Id,
            ProductType = product.ProductType,
            TrackingType = product.TrackingType,
            Description = Text.Clean(request.Description),
            UnitId = unit.UnitId,
            ConversionFactor = unit.ConversionFactor,
            Quantity = request.Quantity,
            BaseQuantity = baseQuantity,
            UnitPrice = request.UnitPrice,
            DiscountPercent = request.DiscountAmount is null ? request.DiscountPercent : (gross > 0 ? Math.Round(discount / gross * 100, 4) : 0),
            DiscountAmount = discount,
            TaxGroupId = mode.HasTax ? request.TaxGroupId : null,
            TaxAmount = tax.TaxAmount,
            LineTotal = inclusive ? net : net + tax.TaxAmount,
            Taxes = tax.Items,
            PurchaseOrderLineId = request.PurchaseOrderLineId,
            GoodsReceiptLineId = request.GoodsReceiptLineId,
            PurchaseInvoiceLineId = request.PurchaseInvoiceLineId,
            RejectedQuantity = request.RejectedQuantity,
            Remarks = Text.Clean(request.Remarks)
        };

        var serialNos = request.SerialNos.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (product.ProductType == "Stock" && (mode.ReceivesStock || mode.TakesStock))
        {
            if (product.TrackingType == "Batch")
            {
                if (mode.ReceivesStock)
                {
                    line.BatchNo = Text.Clean(request.BatchNo)
                        ?? throw new BusinessRuleException($"{label}: enter the batch (lot) number.");
                    if (product.HasExpiry && request.ExpiryDate is null)
                        throw new BusinessRuleException($"{label}: enter the expiry date.");
                    line.ManufactureDate = request.ManufactureDate;
                    line.ExpiryDate = request.ExpiryDate;
                }
                else
                    line.BatchId = request.BatchId ?? throw new BusinessRuleException($"{label}: choose the batch being returned.");
            }

            if (product.TrackingType == "Serial")
            {
                if (baseQuantity != Math.Truncate(baseQuantity) || serialNos.Count != baseQuantity)
                    throw new BusinessRuleException($"{label}: enter one serial number per item ({baseQuantity:0.####}).");
                line.SerialNos = serialNos;
            }
        }

        return new BuiltLine(line, tax.TaxableAmount + tax.NonRecoverableTax, product.ProductType);
    }

    private static List<PurchaseCharge> BuildCharges(IReadOnlyList<PurchaseChargeRequest> requests, int? supplierId)
        => requests.Select(c =>
        {
            if (c.AllocationMethod is not ("Value" or "Quantity"))
                throw new BusinessRuleException("A charge is spread by Value or by Quantity.");
            return new PurchaseCharge
            {
                ChargeName = c.ChargeName.Trim(),
                Amount = TaxCalculator.Round(c.Amount),
                AllocationMethod = c.AllocationMethod,
                // On the supplier's bill (adds to what is owed), billed by someone else, or paid on the spot.
                PayeeSupplierId = c.OnSupplierBill && supplierId is not null ? supplierId : c.PayeeSupplierId,
                OnSupplierBill = c.OnSupplierBill && supplierId is not null
            };
        }).ToList();

    /// <summary>Spreads the charges over the stock lines and sets each line's landed cost per base unit.</summary>
    private static void AllocateLandedCost(IReadOnlyList<BuiltLine> lines, IReadOnlyList<PurchaseCharge> charges, decimal exchangeRate)
    {
        var stock = lines.Where(l => l.ProductType == "Stock" && l.Line.BaseQuantity > 0).ToList();
        foreach (var charge in charges)
        {
            var basis = stock.Select(l => charge.AllocationMethod == "Quantity" ? l.Line.BaseQuantity : l.Cost).ToList();
            var total = basis.Sum();
            if (total <= 0)
                continue;
            decimal allocated = 0;
            for (var i = 0; i < stock.Count; i++)
            {
                var share = i == stock.Count - 1 ? charge.Amount - allocated : TaxCalculator.Round(charge.Amount * basis[i] / total);
                stock[i].Line.ChargeAmount += share;
                allocated += share;
            }
        }

        foreach (var built in lines)
            built.Line.LandedUnitCost = built.Line.BaseQuantity > 0
                ? Math.Round((built.Cost + built.Line.ChargeAmount) * exchangeRate / built.Line.BaseQuantity, 6, MidpointRounding.AwayFromZero)
                : 0;
    }

    private async Task<Supplier> SupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken)
        => await setup.GetSupplierAsync(organizationId, supplierId, cancellationToken)
           ?? throw new BusinessRuleException("The supplier doesn't exist.");

    private async Task<PurchaseOrder?> OpenOrderAsync(int organizationId, long? orderId, int? supplierId, CancellationToken cancellationToken)
    {
        if (orderId is null)
            return null;
        var order = await documents.GetOrderAsync(organizationId, orderId.Value, cancellationToken)
            ?? throw new BusinessRuleException("The purchase order doesn't exist.");
        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw new BusinessRuleException("Only an approved purchase order can be received or billed.");
        if (order.SupplierId != supplierId)
            throw new BusinessRuleException("The purchase order is from a different supplier.");
        return order;
    }

    private static void CheckOrderLines(PurchaseOrder? order, IEnumerable<PurchaseLine> lines)
    {
        var orderLines = order?.Lines.ToDictionary(l => l.Id) ?? [];
        foreach (var line in lines.Where(l => l.PurchaseOrderLineId is not null))
        {
            if (!orderLines.TryGetValue(line.PurchaseOrderLineId!.Value, out var orderLine))
                throw new BusinessRuleException("A line refers to a purchase order line that isn't on this order.");
            if (orderLine.ProductId != line.ProductId)
                throw new BusinessRuleException("A line's product differs from its purchase order line.");
        }
    }

    private static void CheckPaymentMethod(string? method, string message)
    {
        if (method is not null && !PaymentMethod.OnTheSpot.Contains(method))
            throw new BusinessRuleException(message);
    }

    private async Task<TResult?> ActAsync<TResult>(Func<Task<TResult?>> load, Func<Task> action) where TResult : class
    {
        if (await load() is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(action);
        return await load();
    }

    // =====================================================================
    // Supplier invoices
    // =====================================================================

    public Task<PagedResult<PurchaseInvoice>> GetInvoicesAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetInvoicesAsync(organizationId, query, cancellationToken);
    }

    public Task<PurchaseInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetInvoiceAsync(organizationId, id, cancellationToken);

    public async Task<PurchaseInvoice> CreateInvoiceAsync(int organizationId, PurchaseInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await BuildInvoiceAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveInvoiceAsync(organizationId, invoice, userId, cancellationToken));
        return (await documents.GetInvoiceAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<PurchaseInvoice?> UpdateInvoiceAsync(int organizationId, long id, PurchaseInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetInvoiceAsync(organizationId, id, cancellationToken) is null)
            return null;
        var invoice = await BuildInvoiceAsync(organizationId, id, request, cancellationToken);
        invoice.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveInvoiceAsync(organizationId, invoice, userId, cancellationToken));
        return await documents.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    private async Task<PurchaseInvoice> BuildInvoiceAsync(int organizationId, long? id, PurchaseInvoiceUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.PurchaseType is not (PurchaseTypes.Cash or PurchaseTypes.Credit))
            throw new BusinessRuleException("Choose Cash or Credit.");
        var credit = request.PurchaseType == PurchaseTypes.Credit;
        if (credit && request.SupplierId is null)
            throw new BusinessRuleException("A credit purchase needs a supplier on file, so the due can be tracked.");

        // Required: the payment source line whose ledger head the invoice's payment posts to.
        var paymentSourceLineId = request.InvPaymentSourceDId;
        if (paymentSourceLineId is null || !await paymentSources.ActiveLineExistsAsync(paymentSourceLineId.Value, cancellationToken))
            throw new BusinessRuleException("Choose the payment source and its type.");
        var supplier = request.SupplierId is null ? null : await SupplierAsync(organizationId, request.SupplierId.Value, cancellationToken);
        if (supplier is null && Text.Clean(request.SupplierName) is null && !credit)
            request.SupplierName = "Walk-in supplier";

        var context = await LoadAsync(organizationId, request.Lines, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(context, request.CurrencyLookupDataId, request.ExchangeRate, cancellationToken);
        var warehouseId = request.WarehouseId ?? (request.ReceivesStock ? context.Settings.DefaultWarehouseId : null);
        if (request.ReceivesStock && warehouseId is null)
            throw new BusinessRuleException("Choose the warehouse the stock goes into.");
        await ownership.EnsureAsync(organizationId, "warehouse", [warehouseId], cancellationToken);
        var order = await OpenOrderAsync(organizationId, request.PurchaseOrderId, request.SupplierId, cancellationToken);

        var mode = new LineMode(HasTax: true, ReceivesStock: request.ReceivesStock, TakesStock: false);
        var built = request.Lines.Select((line, index) => BuildLine(line, index, context, mode)).ToList();
        if (!request.ReceivesStock && built.Any(b => b.Line.GoodsReceiptLineId is null))
            throw new BusinessRuleException("Billing received goods: every line must come from a goods receipt.");
        if (request.ReceivesStock && built.Any(b => b.Line.GoodsReceiptLineId is not null))
            throw new BusinessRuleException("A direct purchase receives its own stock; bill goods receipts on a separate invoice.");
        CheckOrderLines(order, built.Select(b => b.Line));

        var charges = BuildCharges(request.Charges, request.SupplierId);
        await ownership.EnsureAsync(organizationId, "supplier", charges.Select(c => c.PayeeSupplierId), cancellationToken);
        AllocateLandedCost(built, charges, rate);

        var lines = built.Select(b => b.Line).ToList();
        var chargeOnBill = charges.Where(c => c.OnSupplierBill).Sum(c => c.Amount);
        var total = TaxCalculator.Round(lines.Sum(l => l.LineTotal) + chargeOnBill + request.RoundOffAmount);
        if (total < 0)
            throw new BusinessRuleException("The invoice total can't be negative.");

        decimal paid;
        string? method = Text.Clean(request.PaymentMethod);
        if (credit)
        {
            paid = TaxCalculator.Round(request.PaidAmount);
            if (paid > total)
                throw new BusinessRuleException("The amount paid now is more than the invoice total.");
            if (paid > 0 && method is null)
                throw new BusinessRuleException("Choose how the amount paid now was paid.");
            if (paid == 0)
                method = null;
        }
        else
        {
            paid = total;
            method ??= PaymentMethod.Cash;
        }
        CheckPaymentMethod(method, "Pay on the spot by cash, bank, mobile wallet or card. Record a cheque as a supplier payment.");

        var supplierInvoiceNo = Text.Clean(request.SupplierInvoiceNo);
        if (supplier is not null && supplierInvoiceNo is not null &&
            await documents.SupplierInvoiceNoExistsAsync(organizationId, supplier.Id, supplierInvoiceNo, id, cancellationToken))
            throw new BusinessConflictException("This supplier invoice number is already entered for the supplier.");

        return new PurchaseInvoice
        {
            InvoiceDate = request.InvoiceDate,
            PurchaseType = request.PurchaseType,
            DueDate = credit ? request.DueDate ?? request.InvoiceDate.AddDays(supplier?.PaymentTermsDays ?? 0) : null,
            SupplierId = supplier?.Id,
            SupplierName = supplier is null ? Text.Clean(request.SupplierName) : null,
            SupplierMobile = Text.Clean(request.SupplierMobile),
            ReceivesStock = request.ReceivesStock,
            WarehouseId = warehouseId,
            PurchaseOrderId = order?.Id,
            SupplierInvoiceNo = supplierInvoiceNo,
            SupplierInvoiceDate = request.SupplierInvoiceDate,
            TaxInvoiceNo = Text.Clean(request.TaxInvoiceNo),
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            SubtotalAmount = lines.Sum(l => TaxCalculator.Round(l.Quantity * l.UnitPrice)),
            DiscountAmount = lines.Sum(l => l.DiscountAmount),
            TaxAmount = lines.Sum(l => l.TaxAmount),
            ChargeAmount = chargeOnBill,
            RoundOffAmount = request.RoundOffAmount,
            TotalAmount = total,
            PaidAmount = paid,
            PaymentMethod = method,
            PaymentProvider = paid > 0 ? Text.Clean(request.PaymentProvider) : null,
            PaymentReferenceNo = paid > 0 ? Text.Clean(request.PaymentReferenceNo) : null,
            InvPaymentSourceDId = paymentSourceLineId,
            Remarks = Text.Clean(request.Remarks),
            Lines = lines,
            Charges = charges
        };
    }

    public Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteInvoiceAsync(organizationId, id, cancellationToken);

    public Task<PurchaseInvoice?> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetInvoiceAsync(organizationId, id, cancellationToken),
            () => documents.PostInvoiceAsync(organizationId, id, userId, cancellationToken));

    public Task<PurchaseInvoice?> CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetInvoiceAsync(organizationId, id, cancellationToken),
            () => documents.CancelInvoiceAsync(organizationId, id, userId, cancellationToken));

    public Task<IReadOnlyList<OpenPurchaseInvoice>> GetOpenInvoicesAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default)
        => documents.GetOpenInvoicesAsync(organizationId, supplierId, cancellationToken);

    // =====================================================================
    // Purchase orders
    // =====================================================================

    public Task<PagedResult<PurchaseOrder>> GetOrdersAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetOrdersAsync(organizationId, query, cancellationToken);
    }

    public Task<PurchaseOrder?> GetOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetOrderAsync(organizationId, id, cancellationToken);

    public async Task<PurchaseOrder> CreateOrderAsync(int organizationId, PurchaseOrderUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var order = await BuildOrderAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveOrderAsync(organizationId, order, userId, cancellationToken));
        return (await documents.GetOrderAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<PurchaseOrder?> UpdateOrderAsync(int organizationId, long id, PurchaseOrderUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetOrderAsync(organizationId, id, cancellationToken) is null)
            return null;
        var order = await BuildOrderAsync(organizationId, request, cancellationToken);
        order.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveOrderAsync(organizationId, order, userId, cancellationToken));
        return await documents.GetOrderAsync(organizationId, id, cancellationToken);
    }

    private async Task<PurchaseOrder> BuildOrderAsync(int organizationId, PurchaseOrderUpsertRequest request, CancellationToken cancellationToken)
    {
        var supplier = await SupplierAsync(organizationId, request.SupplierId, cancellationToken);
        await ownership.EnsureAsync(organizationId, "warehouse", [request.WarehouseId], cancellationToken);
        var context = await LoadAsync(organizationId, request.Lines, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(context, request.CurrencyLookupDataId ?? supplier.CurrencyLookupDataId,
            request.ExchangeRate, cancellationToken);
        var lines = request.Lines.Select((line, index) => BuildLine(line, index, context, new LineMode(true, false, false)).Line).ToList();
        return new PurchaseOrder
        {
            OrderDate = request.OrderDate,
            ExpectedDate = request.ExpectedDate,
            SupplierId = supplier.Id,
            WarehouseId = request.WarehouseId,
            SupplierReference = Text.Clean(request.SupplierReference),
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            PaymentTermsDays = request.PaymentTermsDays == 0 ? supplier.PaymentTermsDays : request.PaymentTermsDays,
            SubtotalAmount = lines.Sum(l => TaxCalculator.Round(l.Quantity * l.UnitPrice)),
            DiscountAmount = lines.Sum(l => l.DiscountAmount),
            TaxAmount = lines.Sum(l => l.TaxAmount),
            ChargeAmount = TaxCalculator.Round(request.ChargeAmount),
            TotalAmount = TaxCalculator.Round(lines.Sum(l => l.LineTotal) + request.ChargeAmount),
            TermsAndConditions = Text.Clean(request.TermsAndConditions),
            Remarks = Text.Clean(request.Remarks),
            Lines = lines
        };
    }

    public Task<DeleteOutcome> DeleteOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteOrderAsync(organizationId, id, cancellationToken);

    public Task<PurchaseOrder?> ChangeOrderStatusAsync(int organizationId, long id, string action, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetOrderAsync(organizationId, id, cancellationToken),
            () => documents.ChangeOrderStatusAsync(organizationId, id, action, userId, cancellationToken));

    // =====================================================================
    // Goods receipts
    // =====================================================================

    public Task<PagedResult<GoodsReceipt>> GetReceiptsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetReceiptsAsync(organizationId, query, cancellationToken);
    }

    public Task<GoodsReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetReceiptAsync(organizationId, id, cancellationToken);

    public async Task<GoodsReceipt> CreateReceiptAsync(int organizationId, GoodsReceiptUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var receipt = await BuildReceiptAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveReceiptAsync(organizationId, receipt, userId, cancellationToken));
        return (await documents.GetReceiptAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<GoodsReceipt?> UpdateReceiptAsync(int organizationId, long id, GoodsReceiptUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetReceiptAsync(organizationId, id, cancellationToken) is null)
            return null;
        var receipt = await BuildReceiptAsync(organizationId, request, cancellationToken);
        receipt.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveReceiptAsync(organizationId, receipt, userId, cancellationToken));
        return await documents.GetReceiptAsync(organizationId, id, cancellationToken);
    }

    private async Task<GoodsReceipt> BuildReceiptAsync(int organizationId, GoodsReceiptUpsertRequest request, CancellationToken cancellationToken)
    {
        var supplier = await SupplierAsync(organizationId, request.SupplierId, cancellationToken);
        await ownership.EnsureAsync(organizationId, "warehouse", [request.WarehouseId], cancellationToken);
        var context = await LoadAsync(organizationId, request.Lines, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(context, request.CurrencyLookupDataId ?? supplier.CurrencyLookupDataId,
            request.ExchangeRate, cancellationToken);
        var order = await OpenOrderAsync(organizationId, request.PurchaseOrderId, supplier.Id, cancellationToken);

        // Tax is on the bill, not the receipt: receipt lines are priced net of discount only.
        var built = request.Lines.Select((line, index) => BuildLine(line, index, context, new LineMode(false, true, false))).ToList();
        if (built.Any(b => b.ProductType != "Stock"))
            throw new BusinessRuleException("A goods receipt only holds stock products; bill services and non-stock items on the invoice.");
        CheckOrderLines(order, built.Select(b => b.Line));
        var charges = BuildCharges(request.Charges, supplier.Id);
        await ownership.EnsureAsync(organizationId, "supplier", charges.Select(c => c.PayeeSupplierId), cancellationToken);
        AllocateLandedCost(built, charges, rate);

        return new GoodsReceipt
        {
            ReceiptDate = request.ReceiptDate,
            SupplierId = supplier.Id,
            WarehouseId = request.WarehouseId,
            PurchaseOrderId = order?.Id,
            SupplierChallanNo = Text.Clean(request.SupplierChallanNo),
            SupplierChallanDate = request.SupplierChallanDate,
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            Remarks = Text.Clean(request.Remarks),
            Lines = built.Select(b => b.Line).ToList(),
            Charges = charges
        };
    }

    public Task<DeleteOutcome> DeleteReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteReceiptAsync(organizationId, id, cancellationToken);

    public Task<GoodsReceipt?> PostReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReceiptAsync(organizationId, id, cancellationToken),
            () => documents.PostReceiptAsync(organizationId, id, userId, cancellationToken));

    public Task<GoodsReceipt?> CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReceiptAsync(organizationId, id, cancellationToken),
            () => documents.CancelReceiptAsync(organizationId, id, userId, cancellationToken));

    // =====================================================================
    // Purchase returns
    // =====================================================================

    public Task<PagedResult<PurchaseReturn>> GetReturnsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetReturnsAsync(organizationId, query, cancellationToken);
    }

    public Task<PurchaseReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetReturnAsync(organizationId, id, cancellationToken);

    public async Task<PurchaseReturn> CreateReturnAsync(int organizationId, PurchaseReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var (purchaseReturn, serials) = await BuildReturnAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveReturnAsync(organizationId, purchaseReturn, serials, userId, cancellationToken));
        return (await documents.GetReturnAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<PurchaseReturn?> UpdateReturnAsync(int organizationId, long id, PurchaseReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetReturnAsync(organizationId, id, cancellationToken) is null)
            return null;
        var (purchaseReturn, serials) = await BuildReturnAsync(organizationId, request, cancellationToken);
        purchaseReturn.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveReturnAsync(organizationId, purchaseReturn, serials, userId, cancellationToken));
        return await documents.GetReturnAsync(organizationId, id, cancellationToken);
    }

    private async Task<(PurchaseReturn Return, IReadOnlyDictionary<int, IReadOnlyList<long>> Serials)> BuildReturnAsync(
        int organizationId, PurchaseReturnUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!PurchaseReturnReasons.All.Contains(request.Reason))
            throw new BusinessRuleException("Choose a reason: Damaged, Expired, WrongItem, Excess, QualityIssue or Other.");
        if (request.SettlementType is not ("AdjustDue" or "Refund"))
            throw new BusinessRuleException("Choose how the return is settled: AdjustDue or Refund.");
        if (request.SettlementType == "AdjustDue" && request.SupplierId is null)
            throw new BusinessRuleException("Deducting a return from dues needs a supplier on file; refund it instead.");
        var refundMethod = request.SettlementType == "Refund" ? Text.Clean(request.RefundMethod) ?? PaymentMethod.Cash : null;
        CheckPaymentMethod(refundMethod, "A refund is taken in cash, bank, mobile wallet or card.");

        var supplier = request.SupplierId is null ? null : await SupplierAsync(organizationId, request.SupplierId.Value, cancellationToken);
        await ownership.EnsureAsync(organizationId, "warehouse", [request.WarehouseId], cancellationToken);

        long? currencyHint = null;
        if (request.PurchaseInvoiceId is not null)
        {
            var invoice = await documents.GetInvoiceAsync(organizationId, request.PurchaseInvoiceId.Value, cancellationToken)
                ?? throw new BusinessRuleException("The invoice doesn't exist.");
            if (invoice.Status != DocumentStatus.Posted || invoice.SupplierId != request.SupplierId)
                throw new BusinessRuleException("Return against a posted invoice of the same supplier.");
            if (request.Lines.Any(l => l.PurchaseInvoiceLineId is not null && invoice.Lines.All(il => il.Id != l.PurchaseInvoiceLineId)))
                throw new BusinessRuleException("A line refers to an invoice line that isn't on this invoice.");
            currencyHint = invoice.CurrencyLookupDataId;
        }
        if (request.GoodsReceiptId is not null)
        {
            var receipt = await documents.GetReceiptAsync(organizationId, request.GoodsReceiptId.Value, cancellationToken)
                ?? throw new BusinessRuleException("The goods receipt doesn't exist.");
            if (receipt.Status != DocumentStatus.Posted || receipt.SupplierId != request.SupplierId)
                throw new BusinessRuleException("Return against a posted goods receipt of the same supplier.");
            if (request.Lines.Any(l => l.GoodsReceiptLineId is not null && receipt.Lines.All(rl => rl.Id != l.GoodsReceiptLineId)))
                throw new BusinessRuleException("A line refers to a receipt line that isn't on this goods receipt.");
            currencyHint ??= receipt.CurrencyLookupDataId;
        }

        var context = await LoadAsync(organizationId, request.Lines, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(context, request.CurrencyLookupDataId ?? currencyHint, request.ExchangeRate, cancellationToken);
        var built = request.Lines.Select((line, index) => BuildLine(line, index, context, new LineMode(true, false, true))).ToList();
        var lines = built.Select(b => b.Line).ToList();
        if (!await ownership.BatchesBelongToAsync(organizationId, lines.Where(l => l.BatchId is not null).Select(l => (l.BatchId!.Value, l.ProductId)), cancellationToken))
            throw new BusinessRuleException("A chosen batch doesn't belong to its line's product.");

        var serials = new Dictionary<int, IReadOnlyList<long>>();
        foreach (var line in lines.Where(l => l.SerialNos.Count > 0))
        {
            var found = await documents.FindSerialsInStockAsync(organizationId, line.ProductId, request.WarehouseId, line.SerialNos, cancellationToken);
            var missing = line.SerialNos.Where(s => !found.ContainsKey(s)).ToList();
            if (missing.Count > 0)
                throw new BusinessRuleException($"Line {line.LineNo}: not in stock in this warehouse: {string.Join(", ", missing)}.");
            serials[line.LineNo] = line.SerialNos.Select(s => found[s]).ToList();
        }

        var purchaseReturn = new PurchaseReturn
        {
            ReturnDate = request.ReturnDate,
            SupplierId = supplier?.Id,
            SupplierName = supplier is null ? Text.Clean(request.SupplierName) ?? "Walk-in supplier" : null,
            WarehouseId = request.WarehouseId,
            GoodsReceiptId = request.GoodsReceiptId,
            PurchaseInvoiceId = request.PurchaseInvoiceId,
            Reason = request.Reason,
            SettlementType = request.SettlementType,
            RefundMethod = refundMethod,
            RefundProvider = refundMethod is null ? null : Text.Clean(request.RefundProvider),
            RefundReferenceNo = refundMethod is null ? null : Text.Clean(request.RefundReferenceNo),
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            SubtotalAmount = lines.Sum(l => TaxCalculator.Round(l.Quantity * l.UnitPrice)),
            DiscountAmount = lines.Sum(l => l.DiscountAmount),
            TaxAmount = lines.Sum(l => l.TaxAmount),
            TotalAmount = TaxCalculator.Round(lines.Sum(l => l.LineTotal)),
            Remarks = Text.Clean(request.Remarks),
            Lines = lines
        };
        return (purchaseReturn, serials);
    }

    public Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteReturnAsync(organizationId, id, cancellationToken);

    public Task<PurchaseReturn?> PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReturnAsync(organizationId, id, cancellationToken),
            () => documents.PostReturnAsync(organizationId, id, userId, cancellationToken));

    public Task<PurchaseReturn?> CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReturnAsync(organizationId, id, cancellationToken),
            () => documents.CancelReturnAsync(organizationId, id, userId, cancellationToken));

    // =====================================================================
    // Supplier payments
    // =====================================================================

    public Task<PagedResult<SupplierPayment>> GetPaymentsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetPaymentsAsync(organizationId, query, cancellationToken);
    }

    public Task<SupplierPayment?> GetPaymentAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetPaymentAsync(organizationId, id, cancellationToken);

    public async Task<SupplierPayment> CreatePaymentAsync(int organizationId, SupplierPaymentUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (request.PaymentType is not ("Payment" or "Refund"))
            throw new BusinessRuleException("Choose Payment (to the supplier) or Refund (from the supplier).");
        if (!PaymentMethod.All.Contains(request.PaymentMethod))
            throw new BusinessRuleException("Choose how the money was paid.");
        if (TaxCalculator.Round(request.Amount) <= 0)
            throw new BusinessRuleException("Enter the amount (more than 0).");
        if (request.TaxDeductedAmount < 0)
            throw new BusinessRuleException("The tax deducted can't be negative.");
        var supplier = await SupplierAsync(organizationId, request.SupplierId, cancellationToken);
        var settings = await inventorySetup.GetSettingAsync(organizationId, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(new Context(settings, new Dictionary<int, ProductLookupItem>(), new Dictionary<int, TaxGroupOption>()),
            request.CurrencyLookupDataId ?? supplier.CurrencyLookupDataId, request.ExchangeRate, cancellationToken);

        var settles = TaxCalculator.Round(request.Amount + request.TaxDeductedAmount);
        var allocations = new List<PaymentAllocationItem>();
        if (request.PaymentType == "Payment" && (request.Allocations.Count > 0 || request.AutoAllocate))
        {
            var open = (await documents.GetOpenInvoicesAsync(organizationId, supplier.Id, cancellationToken))
                .Where(i => i.CurrencyLookupDataId == currencyId).ToList();
            if (request.Allocations.Count > 0)
            {
                foreach (var allocation in request.Allocations.GroupBy(a => a.PurchaseInvoiceId))
                {
                    var invoice = open.FirstOrDefault(i => i.Id == allocation.Key)
                        ?? throw new BusinessRuleException("A payment can only settle this supplier's open invoices in the same currency.");
                    var amount = TaxCalculator.Round(allocation.Sum(a => a.Amount));
                    if (amount > invoice.DueAmount)
                        throw new BusinessRuleException($"Invoice {invoice.InvoiceNo} has only {invoice.DueAmount:0.##} left to pay.");
                    allocations.Add(new PaymentAllocationItem { PurchaseInvoiceId = invoice.Id, InvoiceNo = invoice.InvoiceNo, Amount = amount });
                }
            }
            else
            {
                // Oldest due first.
                var remaining = settles;
                foreach (var invoice in open)
                {
                    if (remaining <= 0)
                        break;
                    var amount = Math.Min(remaining, invoice.DueAmount);
                    allocations.Add(new PaymentAllocationItem { PurchaseInvoiceId = invoice.Id, InvoiceNo = invoice.InvoiceNo, Amount = amount });
                    remaining -= amount;
                }
            }
            if (allocations.Sum(a => a.Amount) > settles)
                throw new BusinessRuleException("The invoices settled add up to more than the payment.");
        }
        else if (request.Allocations.Count > 0)
            throw new BusinessRuleException("A refund from the supplier doesn't settle invoices.");

        var cheque = request.PaymentMethod == PaymentMethod.Cheque;
        var payment = new SupplierPayment
        {
            PaymentDate = request.PaymentDate,
            SupplierId = supplier.Id,
            PaymentType = request.PaymentType,
            PaymentMethod = request.PaymentMethod,
            PaymentProvider = Text.Clean(request.PaymentProvider),
            ReferenceNo = Text.Clean(request.ReferenceNo),
            ChequeDate = cheque ? request.ChequeDate ?? request.PaymentDate : null,
            ChequeStatus = cheque ? "Pending" : null,
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            Amount = TaxCalculator.Round(request.Amount),
            TaxDeductedAmount = TaxCalculator.Round(request.TaxDeductedAmount),
            Remarks = Text.Clean(request.Remarks),
            Allocations = allocations
        };
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.CreatePaymentAsync(organizationId, payment, userId, cancellationToken));
        return (await documents.GetPaymentAsync(organizationId, id, cancellationToken))!;
    }

    public Task<SupplierPayment?> CancelPaymentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetPaymentAsync(organizationId, id, cancellationToken),
            () => documents.CancelPaymentAsync(organizationId, id, userId, cancellationToken));

    public Task<SupplierPayment?> SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default)
    {
        if (chequeStatus is not ("Pending" or "Cleared" or "Bounced"))
            throw new BusinessRuleException("A cheque is Pending, Cleared or Bounced.");
        return ActAsync(() => documents.GetPaymentAsync(organizationId, id, cancellationToken),
            () => documents.SetChequeStatusAsync(organizationId, id, chequeStatus, userId, cancellationToken));
    }
}
