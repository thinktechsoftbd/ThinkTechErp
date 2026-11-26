using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ServiceInvoices;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Sales;
using ThinkTechErpRepository.ServiceInvoices;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.ServiceInvoices;

public interface IServiceInvoiceService
{
    Task<PagedResult<ServiceInvoice>> GetInvoicesAsync(int organizationId, ServiceInvoiceQuery query, CancellationToken cancellationToken = default);
    Task<ServiceInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<ServiceInvoice> CreateInvoiceAsync(int organizationId, ServiceInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ServiceInvoice?> UpdateInvoiceAsync(int organizationId, long id, ServiceInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Posts a draft (finalizing does it first): it is then locked. Null if there's no such invoice.</summary>
    Task<ServiceInvoice?> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Back to draft, so it can be changed or deleted. Refused while a posted bill voucher is there;
    /// <paramref name="billVoucherUnposting"/> is set when that voucher is being unposted.
    /// </summary>
    Task<ServiceInvoice?> UnpostInvoiceAsync(int organizationId, long id, int userId, bool billVoucherUnposting = false, CancellationToken cancellationToken = default);
    /// <summary>Customers and suppliers (every party can be billed), by code, name or mobile; or exactly these ids.</summary>
    Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service invoices: the parts used on a job (lines, priced like a sale), the service charge and a note of the work.
/// Saved as drafts; finalizing posts one (it is then locked) and posts its Bill Voucher. Nothing moves in stock and
/// no due is booked to the customer's ledger: the voucher carries it in the accounts.
/// </summary>
public sealed class ServiceInvoiceService(
    IServiceInvoiceRepository invoices,
    ISalesSetupRepository salesSetup,
    IProductRepository products,
    IInventorySetupRepository inventorySetup,
    IPaymentSourceRepository paymentSources,
    IOwnershipRepository ownership) : IServiceInvoiceService
{
    public Task<PagedResult<ServiceInvoice>> GetInvoicesAsync(int organizationId, ServiceInvoiceQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return invoices.GetInvoicesAsync(organizationId, query, cancellationToken);
    }

    public Task<ServiceInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => invoices.GetInvoiceAsync(organizationId, id, cancellationToken);

    public async Task<ServiceInvoice> CreateInvoiceAsync(int organizationId, ServiceInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await BuildInvoiceAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => invoices.SaveInvoiceAsync(organizationId, invoice, userId, cancellationToken));
        return (await invoices.GetInvoiceAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<ServiceInvoice?> UpdateInvoiceAsync(int organizationId, long id, ServiceInvoiceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await invoices.GetInvoiceAsync(organizationId, id, cancellationToken) is null)
            return null;
        var invoice = await BuildInvoiceAsync(organizationId, request, cancellationToken);
        invoice.Id = id;
        await BusinessDbErrors.ExecuteAsync(() => invoices.SaveInvoiceAsync(organizationId, invoice, userId, cancellationToken));
        return await invoices.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => invoices.DeleteInvoiceAsync(organizationId, id, cancellationToken);

    public async Task<ServiceInvoice?> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        if (invoice.Status != DocumentStatus.Draft || !await invoices.PostInvoiceAsync(organizationId, id, userId, cancellationToken))
            throw new BusinessRuleException($"Service invoice {invoice.InvoiceNo} is {invoice.Status.ToLowerInvariant()}; only a draft can be posted.");
        return await invoices.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    public async Task<ServiceInvoice?> UnpostInvoiceAsync(int organizationId, long id, int userId, bool billVoucherUnposting = false, CancellationToken cancellationToken = default)
    {
        if (await invoices.GetInvoiceAsync(organizationId, id, cancellationToken) is null)
            return null;
        await invoices.UnpostInvoiceAsync(organizationId, id, userId, billVoucherUnposting, cancellationToken);
        return await invoices.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    public Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(
        int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
        => salesSetup.GetCustomerOptionsAsync(organizationId, Text.Clean(search), ids, limit, includeSuppliers: true, cancellationToken);

    private async Task<ServiceInvoice> BuildInvoiceAsync(int organizationId, ServiceInvoiceUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.SaleType is not (SaleTypes.Cash or SaleTypes.Credit))
            throw new BusinessRuleException("Choose Cash or Credit.");
        var credit = request.SaleType == SaleTypes.Credit;
        if (credit && request.CustomerId is null)
            throw new BusinessRuleException("A credit service invoice needs a customer on file, so the due can be collected later.");
        // Every party can be billed (the picker lists suppliers too); billing one makes them a customer.
        if (request.CustomerId is int partyId)
            await salesSetup.MakeCustomerAsync(organizationId, partyId, cancellationToken);
        var customer = request.CustomerId is null
            ? null
            : await salesSetup.GetCustomerAsync(organizationId, request.CustomerId.Value, cancellationToken)
              ?? throw new BusinessRuleException("The customer doesn't exist.");
        if (customer is { IsActive: false })
            throw new BusinessRuleException($"{customer.Name} is inactive. Activate the customer to bill them.");

        var serviceAmount = TaxCalculator.Round(request.ServiceAmount);
        if (request.Lines.Count == 0 && serviceAmount == 0)
            throw new BusinessRuleException("Add the parts used or a service amount.");

        var settings = await inventorySetup.GetSettingAsync(organizationId, cancellationToken);
        var baseCurrency = settings.BaseCurrencyLookupDataId
            ?? throw new BusinessRuleException("Set the base currency in Inventory Settings first.");
        var currencyId = request.CurrencyLookupDataId ?? baseCurrency;
        if (currencyId != baseCurrency && !await salesSetup.LookupValueExistsAsync(currencyId, SettingsRules.CurrencyLookupTable, cancellationToken))
            throw new BusinessRuleException("Choose a currency from the Currency lookup list.");
        var rate = currencyId == baseCurrency ? 1 : request.ExchangeRate;
        var warehouseId = request.WarehouseId ?? settings.DefaultWarehouseId
            ?? throw new BusinessRuleException("Choose the warehouse the parts come from.");
        await ownership.EnsureAsync(organizationId, "warehouse", [warehouseId], cancellationToken);

        var productMap = await products.GetForLinesAsync(organizationId, request.Lines.Select(l => l.ProductId), cancellationToken);
        var groupIds = request.Lines.Where(l => l.TaxGroupId is not null).Select(l => l.TaxGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? new Dictionary<int, TaxGroupOption>()
            : (await inventorySetup.GetTaxGroupOptionsAsync(organizationId, groupIds, cancellationToken)).ToDictionary(g => g.Id);
        if (groups.Count != groupIds.Count)
            throw new BusinessRuleException("One of the tax groups doesn't exist.");

        var lines = request.Lines.Select((line, index) => BuildLine(line, index, productMap, groups, settings.PricesIncludeTax)).ToList();
        var batches = lines.Where(l => l.BatchId is not null).Select(l => (l.BatchId!.Value, l.ProductId)).ToList();
        if (!await ownership.BatchesBelongToAsync(organizationId, batches, cancellationToken))
            throw new BusinessRuleException("A chosen batch isn't one of the line product's batches.");

        var extraDiscount = TaxCalculator.Round(request.ExtraDiscountAmount);
        var charge = TaxCalculator.Round(request.ChargeAmount);
        var linesTotal = lines.Sum(l => l.LineTotal);
        if (extraDiscount > linesTotal + charge + serviceAmount)
            throw new BusinessRuleException("The discount on the bill is more than the bill.");
        var total = TaxCalculator.Round(linesTotal + charge + serviceAmount - extraDiscount + request.RoundOffAmount);
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
            throw new BusinessRuleException("Take payment by cash, bank, mobile wallet or card.");

        // Required when money is taken (a cash bill, or something paid now on a credit bill): the payment source line
        // whose ledger head the money goes to. A credit bill with nothing paid now has none.
        int? paymentSourceLineId = null;
        if (paid > 0)
        {
            paymentSourceLineId = request.InvPaymentSourceDId;
            if (paymentSourceLineId is null || !await paymentSources.ActiveLineExistsAsync(paymentSourceLineId.Value, cancellationToken))
                throw new BusinessRuleException("Choose the payment source and its type.");
        }

        return new ServiceInvoice
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
            ServiceAmount = serviceAmount,
            RoundOffAmount = request.RoundOffAmount,
            TotalAmount = total,
            PaidAmount = paid,
            PaymentMethod = method,
            PaymentProvider = paid > 0 ? Text.Clean(request.PaymentProvider) : null,
            PaymentReferenceNo = paid > 0 ? Text.Clean(request.PaymentReferenceNo) : null,
            InvPaymentSourceDId = paymentSourceLineId,
            // Nothing settles a service invoice later yet, so what was paid on it is all that is settled.
            PaymentStatus = paid >= total ? "Paid" : paid > 0 ? "PartiallyPaid" : "Unpaid",
            ServiceNote = Text.Clean(request.ServiceNote),
            Remarks = Text.Clean(request.Remarks),
            Lines = lines
        };
    }

    private static ServiceInvoiceLine BuildLine(
        ServiceInvoiceLineRequest request, int index, IReadOnlyDictionary<int, ProductLookupItem> products,
        IReadOnlyDictionary<int, TaxGroupOption> groups, bool pricesIncludeTax)
    {
        var label = $"Line {index + 1}";
        if (!products.TryGetValue(request.ProductId, out var product))
            throw new BusinessRuleException($"{label}: the product doesn't exist.");
        label = $"{label} ({product.Name})";
        var unit = product.Units.FirstOrDefault(u => u.UnitId == request.UnitId)
            ?? throw new BusinessRuleException($"{label}: the unit isn't one of the product's units.");

        var gross = TaxCalculator.Round(request.Quantity * request.UnitPrice);
        var discount = request.DiscountAmount is { } fixedDiscount
            ? TaxCalculator.Round(fixedDiscount)
            : TaxCalculator.Round(gross * request.DiscountPercent / 100m);
        if (discount > gross)
            throw new BusinessRuleException($"{label}: the discount is more than the line amount.");
        var net = gross - discount;
        var group = request.TaxGroupId is null ? null : groups[request.TaxGroupId.Value];
        var tax = TaxCalculator.Compute(net, group, pricesIncludeTax);

        return new ServiceInvoiceLine
        {
            LineNo = index + 1,
            ProductId = product.Id,
            ProductType = product.ProductType,
            TrackingType = product.TrackingType,
            Description = Text.Clean(request.Description),
            UnitId = unit.UnitId,
            ConversionFactor = unit.ConversionFactor,
            Quantity = request.Quantity,
            BaseQuantity = TaxCalculator.Round(request.Quantity * unit.ConversionFactor),
            // Only a batch-tracked stock item keeps its lot.
            BatchId = product is { ProductType: "Stock", TrackingType: "Batch" } ? request.BatchId : null,
            UnitPrice = request.UnitPrice,
            DiscountPercent = request.DiscountAmount is null ? request.DiscountPercent : (gross > 0 ? Math.Round(discount / gross * 100, 4) : 0),
            DiscountAmount = discount,
            TaxGroupId = request.TaxGroupId,
            TaxAmount = tax.TaxAmount,
            LineTotal = pricesIncludeTax ? net : net + tax.TaxAmount,
            Remarks = Text.Clean(request.Remarks)
        };
    }
}
