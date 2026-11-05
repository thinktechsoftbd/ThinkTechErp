using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Sales;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Sales;

public interface ISalesDocumentService
{
    Task<PagedResult<SalesInvoice>> GetInvoicesAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<SalesInvoice> CreateInvoiceAsync(int organizationId, SalesInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> UpdateInvoiceAsync(int organizationId, long id, SalesInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenSalesInvoice>> GetOpenInvoicesAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);

    Task<PagedResult<SalesReturn>> GetReturnsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SalesReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<SalesReturn> CreateReturnAsync(int organizationId, SalesReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SalesReturn?> UpdateReturnAsync(int organizationId, long id, SalesReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<SalesReturn?> PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<SalesReturn?> CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<CustomerReceipt>> GetReceiptsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default);
    Task<CustomerReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<CustomerReceipt> CreateReceiptAsync(int organizationId, CustomerReceiptUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<CustomerReceipt?> CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<CustomerReceipt?> SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default);
}

public sealed class SalesDocumentService(
    ISalesDocumentRepository documents,
    ISalesSetupRepository setup,
    IProductRepository products,
    IInventorySetupRepository inventorySetup,
    IOwnershipRepository ownership) : ISalesDocumentService
{
    private async Task<(long CurrencyId, decimal Rate)> CurrencyAsync(InventorySetting settings, long? currencyId, decimal rate, CancellationToken cancellationToken)
    {
        var baseCurrency = settings.BaseCurrencyLookupDataId
            ?? throw new BusinessRuleException("Set the base currency in Inventory Settings first.");
        var id = currencyId ?? baseCurrency;
        if (id != baseCurrency && !await setup.LookupValueExistsAsync(id, SettingsRules.CurrencyLookupTable, cancellationToken))
            throw new BusinessRuleException("Choose a currency from the Currency lookup list.");
        return (id, id == baseCurrency ? 1 : rate);
    }

    private async Task<Customer> CustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken)
        => await setup.GetCustomerAsync(organizationId, customerId, cancellationToken)
           ?? throw new BusinessRuleException("The customer doesn't exist.");

    private static async Task<TResult?> ActAsync<TResult>(Func<Task<TResult?>> load, Func<Task> action) where TResult : class
    {
        if (await load() is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(action);
        return await load();
    }

    // =====================================================================
    // Sales invoices
    // =====================================================================

    public Task<PagedResult<SalesInvoice>> GetInvoicesAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetInvoicesAsync(organizationId, query, cancellationToken);
    }

    public Task<SalesInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetInvoiceAsync(organizationId, id, cancellationToken);

    public async Task<SalesInvoice> CreateInvoiceAsync(int organizationId, SalesInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var (invoice, serials) = await BuildInvoiceAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveInvoiceAsync(organizationId, invoice, serials, userId, cancellationToken));
        return (await documents.GetInvoiceAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<SalesInvoice?> UpdateInvoiceAsync(int organizationId, long id, SalesInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetInvoiceAsync(organizationId, id, cancellationToken) is null)
            return null;
        var (invoice, serials) = await BuildInvoiceAsync(organizationId, request, cancellationToken);
        invoice.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveInvoiceAsync(organizationId, invoice, serials, userId, cancellationToken));
        return await documents.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    private async Task<(SalesInvoice Invoice, IReadOnlyDictionary<int, IReadOnlyList<long>> Serials)> BuildInvoiceAsync(
        int organizationId, SalesInvoiceUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.SaleType is not (SaleTypes.Cash or SaleTypes.Credit))
            throw new BusinessRuleException("Choose Cash or Credit.");
        var credit = request.SaleType == SaleTypes.Credit;
        if (credit && request.CustomerId is null)
            throw new BusinessRuleException("A credit sale needs a customer on file, so the due can be collected later.");
        // The sales screen lists every party; a supplier sold to becomes a customer too.
        if (request.CustomerId is int partyId)
            await setup.MakeCustomerAsync(organizationId, partyId, cancellationToken);
        var customer = request.CustomerId is null ? null : await CustomerAsync(organizationId, request.CustomerId.Value, cancellationToken);
        if (customer is { IsActive: false })
            throw new BusinessRuleException($"{customer.Name} is inactive. Activate the customer to sell to them.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Add at least one item.");

        var settings = await inventorySetup.GetSettingAsync(organizationId, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(settings, request.CurrencyLookupDataId, request.ExchangeRate, cancellationToken);
        var warehouseId = request.WarehouseId ?? settings.DefaultWarehouseId
            ?? throw new BusinessRuleException("Choose the warehouse the goods are sold from.");
        await ownership.EnsureAsync(organizationId, "warehouse", [warehouseId], cancellationToken);

        var productMap = await products.GetForLinesAsync(organizationId, request.Lines.Select(l => l.ProductId), cancellationToken);
        var groupIds = request.Lines.Where(l => l.TaxGroupId is not null).Select(l => l.TaxGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? new Dictionary<int, TaxGroupOption>()
            : (await inventorySetup.GetTaxGroupOptionsAsync(organizationId, groupIds, cancellationToken)).ToDictionary(g => g.Id);
        if (groups.Count != groupIds.Count)
            throw new BusinessRuleException("One of the tax groups doesn't exist.");

        var lines = new List<SalesLine>();
        var serials = new Dictionary<int, IReadOnlyList<long>>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = BuildLine(request.Lines[index], index, productMap, groups, settings.PricesIncludeTax);
            if (line.SerialNos.Count > 0)
            {
                var found = await documents.FindSerialsInStockAsync(organizationId, line.ProductId, warehouseId, line.SerialNos, cancellationToken);
                var missing = line.SerialNos.Where(s => !found.ContainsKey(s)).ToList();
                if (missing.Count > 0)
                    throw new BusinessRuleException($"Line {index + 1} ({productMap[line.ProductId].Name}): not in stock in this warehouse: {string.Join(", ", missing.Take(5))}.");
                serials[line.LineNo] = line.SerialNos.Select(s => found[s]).ToList();
            }
            lines.Add(line);
        }

        var batches = lines.Where(l => l.BatchId is not null).Select(l => (l.BatchId!.Value, l.ProductId)).ToList();
        if (!await ownership.BatchesBelongToAsync(organizationId, batches, cancellationToken))
            throw new BusinessRuleException("A chosen batch isn't one of the line product's batches.");

        var extraDiscount = TaxCalculator.Round(request.ExtraDiscountAmount);
        var charge = TaxCalculator.Round(request.ChargeAmount);
        var linesTotal = lines.Sum(l => l.LineTotal);
        if (extraDiscount > linesTotal + charge)
            throw new BusinessRuleException("The discount on the bill is more than the bill.");
        var total = TaxCalculator.Round(linesTotal + charge - extraDiscount + request.RoundOffAmount);
        if (total < 0)
            throw new BusinessRuleException("The invoice total can't be negative.");

        decimal paid;
        var method = Text.Clean(request.PaymentMethod);
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
        if (method is not null && !PaymentMethod.OnTheSpot.Contains(method))
            throw new BusinessRuleException("Take payment by cash, bank, mobile wallet or card. Record a cheque as a customer receipt.");

        var invoice = new SalesInvoice
        {
            InvoiceDate = request.InvoiceDate,
            SaleType = request.SaleType,
            DueDate = credit ? request.DueDate ?? request.InvoiceDate.AddDays(customer!.PaymentTermsDays) : null,
            CustomerId = customer?.Id,
            CustomerName = customer is null ? Text.Clean(request.CustomerName) : null,
            CustomerMobile = Text.Clean(request.CustomerMobile),
            WarehouseId = warehouseId,
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            SubtotalAmount = lines.Sum(l => TaxCalculator.Round(l.Quantity * l.UnitPrice)),
            DiscountAmount = lines.Sum(l => l.DiscountAmount) + extraDiscount,
            ExtraDiscountAmount = extraDiscount,
            TaxAmount = lines.Sum(l => l.TaxAmount),
            ChargeAmount = charge,
            RoundOffAmount = request.RoundOffAmount,
            TotalAmount = total,
            PaidAmount = paid,
            PaymentMethod = method,
            PaymentProvider = paid > 0 ? Text.Clean(request.PaymentProvider) : null,
            PaymentReferenceNo = paid > 0 ? Text.Clean(request.PaymentReferenceNo) : null,
            Remarks = Text.Clean(request.Remarks),
            Lines = lines
        };
        return (invoice, serials);
    }

    private static SalesLine BuildLine(
        SalesLineRequest request, int index, IReadOnlyDictionary<int, ProductLookupItem> products,
        IReadOnlyDictionary<int, TaxGroupOption> groups, bool pricesIncludeTax)
    {
        var label = $"Line {index + 1}";
        if (!products.TryGetValue(request.ProductId, out var product))
            throw new BusinessRuleException($"{label}: the product doesn't exist.");
        label = $"{label} ({product.Name})";
        var unit = product.Units.FirstOrDefault(u => u.UnitId == request.UnitId)
            ?? throw new BusinessRuleException($"{label}: the unit isn't one of the product's units.");

        var baseQuantity = TaxCalculator.Round(request.Quantity * unit.ConversionFactor);
        var gross = TaxCalculator.Round(request.Quantity * request.UnitPrice);
        var discount = request.DiscountAmount is { } fixedDiscount
            ? TaxCalculator.Round(fixedDiscount)
            : TaxCalculator.Round(gross * request.DiscountPercent / 100m);
        if (discount > gross)
            throw new BusinessRuleException($"{label}: the discount is more than the line amount.");
        var net = gross - discount;
        var group = request.TaxGroupId is null ? null : groups[request.TaxGroupId.Value];
        var tax = TaxCalculator.Compute(net, group, pricesIncludeTax);

        var line = new SalesLine
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
            TaxGroupId = request.TaxGroupId,
            TaxAmount = tax.TaxAmount,
            LineTotal = pricesIncludeTax ? net : net + tax.TaxAmount,
            Taxes = tax.Items,
            Remarks = Text.Clean(request.Remarks)
        };

        if (product.ProductType == "Stock")
        {
            if (product.TrackingType == "Batch")
                line.BatchId = request.BatchId;   // empty: the first to expire, chosen when posted
            if (product.TrackingType == "Serial")
            {
                var serialNos = request.SerialNos.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                if (baseQuantity != Math.Truncate(baseQuantity) || serialNos.Count != baseQuantity)
                    throw new BusinessRuleException($"{label}: enter one serial number per item ({baseQuantity:0.####}).");
                line.SerialNos = serialNos;
            }
        }
        else if (product.TrackingType != "None" || request.BatchId is not null)
            throw new BusinessRuleException($"{label}: only stock products have batches or serial numbers.");

        return line;
    }

    public Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteInvoiceAsync(organizationId, id, cancellationToken);

    public Task<SalesInvoice?> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetInvoiceAsync(organizationId, id, cancellationToken),
            () => documents.PostInvoiceAsync(organizationId, id, userId, cancellationToken));

    public Task<SalesInvoice?> CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetInvoiceAsync(organizationId, id, cancellationToken),
            () => documents.CancelInvoiceAsync(organizationId, id, userId, cancellationToken));

    public Task<IReadOnlyList<OpenSalesInvoice>> GetOpenInvoicesAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
        => documents.GetOpenInvoicesAsync(organizationId, customerId, cancellationToken);

    // =====================================================================
    // Sales returns
    // =====================================================================

    public Task<PagedResult<SalesReturn>> GetReturnsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetReturnsAsync(organizationId, query, cancellationToken);
    }

    public Task<SalesReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetReturnAsync(organizationId, id, cancellationToken);

    public async Task<SalesReturn> CreateReturnAsync(int organizationId, SalesReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var salesReturn = await BuildReturnAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.SaveReturnAsync(organizationId, salesReturn, userId, cancellationToken));
        return (await documents.GetReturnAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<SalesReturn?> UpdateReturnAsync(int organizationId, long id, SalesReturnUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await documents.GetReturnAsync(organizationId, id, cancellationToken) is null)
            return null;
        var salesReturn = await BuildReturnAsync(organizationId, id, request, cancellationToken);
        salesReturn.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => documents.SaveReturnAsync(organizationId, salesReturn, userId, cancellationToken));
        return await documents.GetReturnAsync(organizationId, id, cancellationToken);
    }

    /// <summary>
    /// A return is priced from the sale it came from: each line takes its unit, price, discount, tax, batch
    /// and cost from the invoice line in proportion to the quantity, and the sale's bill discount is shared
    /// the same way, so the customer gets back what they were charged.
    /// </summary>
    private async Task<SalesReturn> BuildReturnAsync(int organizationId, long? id, SalesReturnUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!SalesReturnReasons.All.Contains(request.Reason))
            throw new BusinessRuleException("Choose why the goods came back.");
        if (request.SettlementType is not (SalesReturnSettlement.AdjustDue or SalesReturnSettlement.Refund))
            throw new BusinessRuleException("Choose to refund the money or to deduct it from the customer's dues.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Add at least one item being returned.");

        var invoice = await documents.GetInvoiceAsync(organizationId, request.SalesInvoiceId, cancellationToken)
            ?? throw new BusinessRuleException("The sale doesn't exist.");
        if (invoice.Status != DocumentStatus.Posted)
            throw new BusinessRuleException("Goods can only be returned from a completed (posted) sale.");
        if (request.ReturnDate < invoice.InvoiceDate)
            throw new BusinessRuleException("The return date can't be before the sale date.");
        var adjustDue = request.SettlementType == SalesReturnSettlement.AdjustDue;
        if (adjustDue && invoice.CustomerId is null)
            throw new BusinessRuleException("A walk-in sale has no customer account to deduct from. Refund the money instead.");

        var warehouseId = request.WarehouseId ?? invoice.WarehouseId;
        await ownership.EnsureAsync(organizationId, "warehouse", [warehouseId], cancellationToken);

        var invoiceLines = invoice.Lines.ToDictionary(l => l.Id);
        if (request.Lines.GroupBy(l => l.SalesInvoiceLineId).Any(g => g.Count() > 1))
            throw new BusinessRuleException("An invoice line is listed twice; return it on one line.");

        var lines = request.Lines.Select((requested, index) =>
        {
            var label = $"Line {index + 1}";
            if (!invoiceLines.TryGetValue(requested.SalesInvoiceLineId, out var sold))
                throw new BusinessRuleException($"{label}: the item isn't on sale {invoice.InvoiceNo}.");
            label = $"{label} ({sold.ProductName})";
            var left = sold.Quantity - sold.ReturnedQuantity;
            if (requested.Quantity > left + 0.0001m)
                throw new BusinessRuleException($"{label}: only {left:0.####} {sold.UnitCode} sold on this invoice can still be returned.");

            var ratio = requested.Quantity / sold.Quantity;
            var baseQuantity = TaxCalculator.Round(requested.Quantity * sold.ConversionFactor);
            var line = new SalesReturnLine
            {
                LineNo = index + 1,
                SalesInvoiceLineId = sold.Id,
                ProductId = sold.ProductId,
                ProductType = sold.ProductType,
                TrackingType = sold.TrackingType,
                UnitId = sold.UnitId,
                ConversionFactor = sold.ConversionFactor,
                Quantity = requested.Quantity,
                BaseQuantity = baseQuantity,
                BatchId = sold.BatchId,
                UnitPrice = sold.UnitPrice,
                DiscountAmount = TaxCalculator.Round(sold.DiscountAmount * ratio),
                TaxGroupId = sold.TaxGroupId,
                TaxAmount = TaxCalculator.Round(sold.TaxAmount * ratio),
                LineTotal = TaxCalculator.Round(sold.LineTotal * ratio),
                UnitCost = sold.UnitCost,
                // Only stock comes back into stock.
                IsRestockable = sold.ProductType == "Stock" && requested.IsRestockable,
                Remarks = Text.Clean(requested.Remarks)
            };

            if (sold.TrackingType == "Serial")
            {
                var serialNos = requested.SerialNos.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                if (baseQuantity != Math.Truncate(baseQuantity) || serialNos.Count != baseQuantity)
                    throw new BusinessRuleException($"{label}: choose one serial number per item returned ({baseQuantity:0.####}).");
                var notSold = serialNos.Where(s => !sold.SerialNos.Contains(s, StringComparer.Ordinal)).ToList();
                if (notSold.Count > 0)
                    throw new BusinessRuleException($"{label}: not sold on this invoice: {string.Join(", ", notSold.Take(5))}.");
                line.SerialNos = serialNos;
            }
            return line;
        }).ToList();

        var linesTotal = lines.Sum(l => l.LineTotal);
        var invoiceLinesTotal = invoice.Lines.Sum(l => l.LineTotal);
        var discountShare = invoice.ExtraDiscountAmount > 0 && invoiceLinesTotal > 0
            ? TaxCalculator.Round(invoice.ExtraDiscountAmount * linesTotal / invoiceLinesTotal)
            : 0;
        var total = Math.Max(0, TaxCalculator.Round(linesTotal - discountShare));

        string? refundMethod = null;
        if (!adjustDue)
        {
            refundMethod = Text.Clean(request.RefundMethod) ?? PaymentMethod.Cash;
            if (!PaymentMethod.OnTheSpot.Contains(refundMethod))
                throw new BusinessRuleException("Refund by cash, bank, mobile wallet or card.");
            var room = await documents.GetRefundRoomAsync(organizationId, invoice.Id, id, cancellationToken);
            if (total > room + 0.0001m)
                throw new BusinessRuleException(room <= 0
                    ? "Nothing was paid on this sale yet, so nothing can be refunded. Deduct the return from the customer's dues instead."
                    : $"Only {room:0.##} was paid on this sale and not yet refunded. Refund at most that, or deduct the return from the dues.");
        }

        return new SalesReturn
        {
            ReturnDate = request.ReturnDate,
            CustomerId = invoice.CustomerId,
            CustomerName = invoice.CustomerId is null ? invoice.CustomerName : null,
            CustomerMobile = invoice.CustomerId is null ? invoice.CustomerMobile : null,
            WarehouseId = warehouseId,
            SalesInvoiceId = invoice.Id,
            Reason = request.Reason,
            SettlementType = request.SettlementType,
            RefundMethod = refundMethod,
            RefundProvider = adjustDue ? null : Text.Clean(request.RefundProvider),
            RefundReferenceNo = adjustDue ? null : Text.Clean(request.RefundReferenceNo),
            CurrencyLookupDataId = invoice.CurrencyLookupDataId,
            ExchangeRate = invoice.ExchangeRate,
            SubtotalAmount = lines.Sum(l => TaxCalculator.Round(l.Quantity * l.UnitPrice)),
            DiscountAmount = lines.Sum(l => l.DiscountAmount) + discountShare,
            TaxAmount = lines.Sum(l => l.TaxAmount),
            TotalAmount = total,
            Remarks = Text.Clean(request.Remarks),
            Lines = lines
        };
    }

    public Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.DeleteReturnAsync(organizationId, id, cancellationToken);

    public Task<SalesReturn?> PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReturnAsync(organizationId, id, cancellationToken),
            () => documents.PostReturnAsync(organizationId, id, userId, cancellationToken));

    public Task<SalesReturn?> CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReturnAsync(organizationId, id, cancellationToken),
            () => documents.CancelReturnAsync(organizationId, id, userId, cancellationToken));

    // =====================================================================
    // Customer receipts
    // =====================================================================

    public Task<PagedResult<CustomerReceipt>> GetReceiptsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return documents.GetReceiptsAsync(organizationId, query, cancellationToken);
    }

    public Task<CustomerReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => documents.GetReceiptAsync(organizationId, id, cancellationToken);

    public async Task<CustomerReceipt> CreateReceiptAsync(int organizationId, CustomerReceiptUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (request.ReceiptType is not ("Receipt" or "Refund"))
            throw new BusinessRuleException("Choose Receipt (from the customer) or Refund (to the customer).");
        if (!PaymentMethod.All.Contains(request.PaymentMethod))
            throw new BusinessRuleException("Choose how the money was paid.");
        if (TaxCalculator.Round(request.Amount) <= 0)
            throw new BusinessRuleException("Enter the amount (more than 0).");
        if (request.TaxDeductedAmount < 0)
            throw new BusinessRuleException("The tax deducted can't be negative.");
        var customer = await CustomerAsync(organizationId, request.CustomerId, cancellationToken);
        var settings = await inventorySetup.GetSettingAsync(organizationId, cancellationToken);
        var (currencyId, rate) = await CurrencyAsync(settings, request.CurrencyLookupDataId, request.ExchangeRate, cancellationToken);
        var refund = request.ReceiptType == "Refund";

        var settles = TaxCalculator.Round(request.Amount + (refund ? 0 : request.TaxDeductedAmount));
        var allocations = new List<ReceiptAllocationItem>();
        if (!refund && (request.Allocations.Count > 0 || request.AutoAllocate))
        {
            var open = (await documents.GetOpenInvoicesAsync(organizationId, customer.Id, cancellationToken))
                .Where(i => i.CurrencyLookupDataId == currencyId).ToList();
            if (request.Allocations.Count > 0)
            {
                foreach (var allocation in request.Allocations.GroupBy(a => a.SalesInvoiceId))
                {
                    var invoice = open.FirstOrDefault(i => i.Id == allocation.Key)
                        ?? throw new BusinessRuleException("A receipt can only settle this customer's open invoices in the same currency.");
                    var amount = TaxCalculator.Round(allocation.Sum(a => a.Amount));
                    if (amount > invoice.DueAmount)
                        throw new BusinessRuleException($"Invoice {invoice.InvoiceNo} has only {invoice.DueAmount:0.##} left to collect.");
                    allocations.Add(new ReceiptAllocationItem { SalesInvoiceId = invoice.Id, InvoiceNo = invoice.InvoiceNo, Amount = amount });
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
                    allocations.Add(new ReceiptAllocationItem { SalesInvoiceId = invoice.Id, InvoiceNo = invoice.InvoiceNo, Amount = amount });
                    remaining -= amount;
                }
            }
            if (allocations.Sum(a => a.Amount) > settles)
                throw new BusinessRuleException("The invoices settled add up to more than the receipt.");
        }
        else if (refund && request.Allocations.Count > 0)
            throw new BusinessRuleException("A refund to the customer doesn't settle invoices.");

        var cheque = request.PaymentMethod == PaymentMethod.Cheque;
        var receipt = new CustomerReceipt
        {
            ReceiptDate = request.ReceiptDate,
            CustomerId = customer.Id,
            ReceiptType = request.ReceiptType,
            PaymentMethod = request.PaymentMethod,
            PaymentProvider = Text.Clean(request.PaymentProvider),
            ReferenceNo = Text.Clean(request.ReferenceNo),
            ChequeDate = cheque ? request.ChequeDate ?? request.ReceiptDate : null,
            ChequeStatus = cheque ? "Pending" : null,
            CurrencyLookupDataId = currencyId,
            ExchangeRate = rate,
            Amount = TaxCalculator.Round(request.Amount),
            TaxDeductedAmount = refund ? 0 : TaxCalculator.Round(request.TaxDeductedAmount),
            Remarks = Text.Clean(request.Remarks),
            Allocations = allocations
        };
        var id = await BusinessDbErrors.ExecuteAsync(() => documents.CreateReceiptAsync(organizationId, receipt, userId, cancellationToken));
        return (await documents.GetReceiptAsync(organizationId, id, cancellationToken))!;
    }

    public Task<CustomerReceipt?> CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => ActAsync(() => documents.GetReceiptAsync(organizationId, id, cancellationToken),
            () => documents.CancelReceiptAsync(organizationId, id, userId, cancellationToken));

    public Task<CustomerReceipt?> SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default)
    {
        if (chequeStatus is not ("Pending" or "Cleared" or "Bounced"))
            throw new BusinessRuleException("A cheque is Pending, Cleared or Bounced.");
        return ActAsync(() => documents.GetReceiptAsync(organizationId, id, cancellationToken),
            () => documents.SetChequeStatusAsync(organizationId, id, chequeStatus, userId, cancellationToken));
    }
}
