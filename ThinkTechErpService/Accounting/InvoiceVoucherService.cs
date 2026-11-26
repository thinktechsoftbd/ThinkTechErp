using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.ServiceInvoices;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Common;
using ThinkTechErpService.Purchase;
using ThinkTechErpService.Sales;
using ThinkTechErpService.ServiceInvoices;

namespace ThinkTechErpService.Accounting;

/// <summary>
/// Finalizing an invoice: posts it (if it is still a draft) and posts its Bill Voucher, worked out from the ledger
/// mappings and linked to the invoice, which then stays locked until the voucher is deleted.
/// <list type="bullet">
/// <item>Purchase: Dr each item category's head; Cr the supplier's head for the unpaid part (credit) and the
/// payment source type's head for what was paid now (all of a cash purchase).</item>
/// <item>Sales: Dr the customer's head for the unpaid part (credit) and the payment source type's head for what was
/// paid now (all of a cash sale; the fixed cash sales head when the invoice has no source, as one billed from a
/// service job); Cr each item category's head.</item>
/// <item>Service: as a sale, with the service charge (labour) credited to the service charge head chosen under
/// Inventory → Settings, beside the parts' category heads.</item>
/// </list>
/// The invoice total (in BDT) is shared out over the categories in proportion to their line totals, so tax,
/// charges, round-off and discounts are carried with the goods and the voucher always balances.
/// </summary>
public interface IInvoiceVoucherService
{
    Task<PurchaseInvoice?> FinalizePurchaseInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> FinalizeSalesInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<ServiceInvoice?> FinalizeServiceInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Back to draft so it can be changed or deleted. A finalized invoice that isn't fully paid has its bill voucher
    /// unposted, which puts it back to draft; a finalized, fully paid one stays locked (its voucher is unposted under
    /// Accounting instead). Otherwise as the invoice's own unpost: its stock reversed, refused while payments or returns
    /// settle it.
    /// </summary>
    Task<PurchaseInvoice?> UnpostPurchaseInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> UnpostSalesInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<ServiceInvoice?> UnpostServiceInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>Maps a supplier's or customer's ledger head (the Map popup of a failed finalize); false if there's no such party.</summary>
    Task<bool> SetPartyLedgerHeadAsync(int organizationId, int partyId, int? accSubClassId, int userId, CancellationToken cancellationToken = default);
}

public sealed class InvoiceVoucherService(
    IPurchaseDocumentService purchases,
    ISalesDocumentService sales,
    IServiceInvoiceService serviceInvoices,
    IInventorySetupRepository inventorySetup,
    IInvoiceVoucherRepository mappings,
    IVoucherService vouchers) : IInvoiceVoucherService
{
    public async Task<PurchaseInvoice?> FinalizePurchaseInvoiceAsync(
        int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await purchases.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        RequireFinalizable(invoice.Status, invoice.BillVoucherRefNo, invoice.BillVoucherStatus);

        // Everything is checked before the invoice is posted, so a missing mapping leaves a draft as it was.
        var request = await BuildPurchaseVoucherAsync(organizationId, invoice, cancellationToken);
        // An unposted bill voucher from an earlier finalize (the invoice went back to draft) is replaced.
        if (invoice.BillVoucherId is long staleVoucherId)
            await vouchers.DeleteVoucherAsync(organizationId, staleVoucherId, userId, cancellationToken);
        var posting = invoice.Status == DocumentStatus.Draft;
        if (posting)
            await purchases.PostInvoiceAsync(organizationId, id, userId, cancellationToken);
        await CreateVoucherAsync(organizationId, request, userId, posting, cancellationToken);
        return await purchases.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    public async Task<SalesInvoice?> FinalizeSalesInvoiceAsync(
        int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await sales.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        RequireFinalizable(invoice.Status, invoice.BillVoucherRefNo, invoice.BillVoucherStatus);

        var request = await BuildSalesVoucherAsync(organizationId, invoice, cancellationToken);
        // An unposted bill voucher from an earlier finalize (the invoice went back to draft) is replaced.
        if (invoice.BillVoucherId is long staleVoucherId)
            await vouchers.DeleteVoucherAsync(organizationId, staleVoucherId, userId, cancellationToken);
        var posting = invoice.Status == DocumentStatus.Draft;
        if (posting)
            await sales.PostInvoiceAsync(organizationId, id, userId, cancellationToken);
        await CreateVoucherAsync(organizationId, request, userId, posting, cancellationToken);
        return await sales.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    public async Task<ServiceInvoice?> FinalizeServiceInvoiceAsync(
        int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await serviceInvoices.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        RequireFinalizable(invoice.Status, invoice.BillVoucherRefNo, invoice.BillVoucherStatus);

        // Everything is checked before the invoice is posted, so a missing mapping leaves a draft as it was.
        var request = await BuildServiceVoucherAsync(organizationId, invoice, cancellationToken);
        // An unposted bill voucher from an earlier finalize (the invoice went back to draft) is replaced.
        if (invoice.BillVoucherId is long staleVoucherId)
            await vouchers.DeleteVoucherAsync(organizationId, staleVoucherId, userId, cancellationToken);
        var posting = invoice.Status == DocumentStatus.Draft;
        if (posting)
            await serviceInvoices.PostInvoiceAsync(organizationId, id, userId, cancellationToken);
        await CreateVoucherAsync(organizationId, request, userId, posting, cancellationToken);
        return await serviceInvoices.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    public async Task<PurchaseInvoice?> UnpostPurchaseInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await purchases.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        if (await UnpostBillVoucherAsync(organizationId, invoice.BillVoucherId, invoice.BillVoucherStatus, invoice.BillVoucherRefNo,
                invoice.PaymentStatus, userId, cancellationToken))
            return await purchases.GetInvoiceAsync(organizationId, id, cancellationToken);
        return await purchases.UnpostInvoiceAsync(organizationId, id, userId, cancellationToken: cancellationToken);
    }

    public async Task<SalesInvoice?> UnpostSalesInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await sales.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        if (await UnpostBillVoucherAsync(organizationId, invoice.BillVoucherId, invoice.BillVoucherStatus, invoice.BillVoucherRefNo,
                invoice.PaymentStatus, userId, cancellationToken))
            return await sales.GetInvoiceAsync(organizationId, id, cancellationToken);
        return await sales.UnpostInvoiceAsync(organizationId, id, userId, cancellationToken: cancellationToken);
    }

    public async Task<ServiceInvoice?> UnpostServiceInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await serviceInvoices.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        if (await UnpostBillVoucherAsync(organizationId, invoice.BillVoucherId, invoice.BillVoucherStatus, invoice.BillVoucherRefNo,
                invoice.PaymentStatus, userId, cancellationToken))
            return await serviceInvoices.GetInvoiceAsync(organizationId, id, cancellationToken);
        return await serviceInvoices.UnpostInvoiceAsync(organizationId, id, userId, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// A finalized invoice: its bill voucher is unposted, which puts the invoice back to draft (stock reversed); refused
    /// when it is fully paid. False when the invoice isn't finalized.
    /// </summary>
    private async Task<bool> UnpostBillVoucherAsync(
        int organizationId, long? voucherId, string? voucherStatus, string? voucherRefNo, string paymentStatus, int userId,
        CancellationToken cancellationToken)
    {
        if (voucherId is not long billVoucherId || voucherStatus != DocumentStatus.Posted)
            return false;
        if (paymentStatus == PaymentStatus.Paid)
            throw new BusinessRuleException(
                $"The invoice is finalized and fully paid: unpost its bill voucher {voucherRefNo} under Accounting → Vouchers to change it.");
        await vouchers.UnpostVoucherAsync(organizationId, billVoucherId, userId, cancellationToken);
        return true;
    }

    public Task<bool> SetPartyLedgerHeadAsync(
        int organizationId, int partyId, int? accSubClassId, int userId, CancellationToken cancellationToken = default)
        => BusinessDbErrors.ExecuteAsync(() => mappings.SetPartyLedgerHeadAsync(organizationId, partyId, accSubClassId, userId, cancellationToken));

    private static void RequireFinalizable(string status, string? billVoucherRefNo, string? billVoucherStatus)
    {
        if (status == DocumentStatus.Cancelled)
            throw new BusinessRuleException("A cancelled invoice can't be finalized.");
        if (billVoucherRefNo is not null && billVoucherStatus == DocumentStatus.Posted)
            throw new BusinessRuleException($"The invoice is already finalized: its bill voucher is {billVoucherRefNo}.");
    }

    private async Task CreateVoucherAsync(
        int organizationId, SourceVoucherRequest request, int userId, bool postedNow, CancellationToken cancellationToken)
    {
        try
        {
            await vouchers.CreateSourceVoucherAsync(organizationId, request, userId, cancellationToken);
        }
        catch (BusinessRuleException exception) when (postedNow)
        {
            throw new BusinessRuleException(
                $"The invoice was posted, but its bill voucher wasn't made: {exception.Message} Finalize it again from the invoice.", exception);
        }
    }

    // ---- Purchase

    private async Task<SourceVoucherRequest> BuildPurchaseVoucherAsync(
        int organizationId, PurchaseInvoice invoice, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var categories = await CategoryHeadsAsync(organizationId, invoice.Lines.Select(l => (l.ProductId, l.LineTotal)), problems, cancellationToken);
        var (total, paid, due) = Amounts(invoice.TotalAmount, invoice.PaidAmount, invoice.ExchangeRate, problems);

        PartyLedgerHead? supplier = null;
        if (due > 0)
        {
            supplier = invoice.SupplierId is int supplierId ? await mappings.GetPartyHeadAsync(organizationId, supplierId, cancellationToken) : null;
            if (supplier is null)
                problems.Add("The unpaid part needs a supplier on file.");
            else if (supplier.AccSubClassId is null)
                problems.Add($"The supplier {supplier.Name} has no ledger head: map one under Purchase → Suppliers.");
        }
        PaymentSourceLedgerHead? source = null;
        if (paid > 0)
        {
            source = invoice.InvPaymentSourceDId is int lineId ? await mappings.GetPaymentSourceHeadAsync(lineId, cancellationToken) : null;
            if (source is null)
                problems.Add("Choose the payment source and its type on the invoice.");
        }
        ThrowIfAny(problems);

        var lines = new List<VoucherLineRequest>();
        foreach (var (head, names, amount) in Share(categories, total))
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, head, null, amount, 0, names));
        if (due > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, supplier!.AccSubClassId!.Value, supplier.Id, 0, due,
                $"Payable to {supplier.Name}"));
        if (paid > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, source!.AccSubClassId, null, 0, paid,
                $"Paid from {source.SourceName} · {source.PaymentFor}"));

        return new SourceVoucherRequest
        {
            VoucherTypeName = VoucherLookups.BillVoucher,
            VoucherDate = invoice.InvoiceDate,
            Narration = $"{invoice.PurchaseType} purchase {invoice.InvoiceNo} from {invoice.SupplierName ?? "a walk-in supplier"}",
            PurchaseInvoiceId = invoice.Id,
            Lines = lines
        };
    }

    // ---- Sales

    private async Task<SourceVoucherRequest> BuildSalesVoucherAsync(
        int organizationId, SalesInvoice invoice, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var categories = await CategoryHeadsAsync(organizationId, invoice.Lines.Select(l => (l.ProductId, l.LineTotal)), problems, cancellationToken);
        var (total, paid, due) = Amounts(invoice.TotalAmount, invoice.PaidAmount, invoice.ExchangeRate, problems);

        PartyLedgerHead? customer = null;
        if (due > 0)
        {
            customer = invoice.CustomerId is int customerId ? await mappings.GetPartyHeadAsync(organizationId, customerId, cancellationToken) : null;
            if (customer is null)
                problems.Add("The unpaid part needs a customer on file.");
            else if (customer.AccSubClassId is null)
                problems.Add($"The customer {customer.Name} has no ledger head: map one under Sales → Customers.");
        }
        PaymentSourceLedgerHead? source = null;
        if (paid > 0 && invoice.InvPaymentSourceDId is int lineId)
        {
            source = await mappings.GetPaymentSourceHeadAsync(lineId, cancellationToken);
            if (source is null)
                problems.Add("Choose the payment source and its type on the invoice.");
        }
        else if (paid > 0 && !await mappings.LedgerHeadExistsAsync(VoucherLookups.CashSalesHeadId, cancellationToken))
            problems.Add($"The cash sales ledger head (id {VoucherLookups.CashSalesHeadId}) isn't in the chart of accounts.");
        ThrowIfAny(problems);

        var lines = new List<VoucherLineRequest>();
        if (due > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, customer!.AccSubClassId!.Value, customer.Id, due, 0,
                $"Receivable from {customer.Name}"));
        if (paid > 0 && source is not null)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, source.AccSubClassId, null, paid, 0,
                $"Received in {source.SourceName} · {source.PaymentFor}"));
        else if (paid > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, VoucherLookups.CashSalesHeadId, null, paid, 0,
                invoice.SaleType == SaleTypes.Credit ? "Paid now" : "Cash sale"));
        foreach (var (head, names, amount) in Share(categories, total))
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, head, null, 0, amount, names));

        return new SourceVoucherRequest
        {
            VoucherTypeName = VoucherLookups.BillVoucher,
            VoucherDate = invoice.InvoiceDate,
            Narration = $"{invoice.SaleType} sale {invoice.InvoiceNo} to {invoice.CustomerName ?? "a walk-in customer"}",
            SalesInvoiceId = invoice.Id,
            Lines = lines
        };
    }

    // ---- Service

    private async Task<SourceVoucherRequest> BuildServiceVoucherAsync(
        int organizationId, ServiceInvoice invoice, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        // The parts by their categories' heads (a bill may have none), and the service charge by its own head.
        var heads = invoice.Lines.Count > 0
            ? await CategoryHeadsAsync(organizationId, invoice.Lines.Select(l => (l.ProductId, l.LineTotal)), problems, cancellationToken)
            : [];
        if (invoice.ServiceAmount > 0)
        {
            var serviceHead = (await inventorySetup.GetSettingAsync(organizationId, cancellationToken)).ServiceChargeAccSubClassId;
            if (serviceHead is null)
                problems.Add("Choose the service charge ledger head under Inventory → Settings.");
            else
                heads = WithHead(heads, serviceHead.Value, "Service charge", invoice.ServiceAmount);
        }
        var (total, paid, due) = Amounts(invoice.TotalAmount, invoice.PaidAmount, invoice.ExchangeRate, problems);

        PartyLedgerHead? customer = null;
        if (due > 0)
        {
            customer = invoice.CustomerId is int customerId ? await mappings.GetPartyHeadAsync(organizationId, customerId, cancellationToken) : null;
            if (customer is null)
                problems.Add("The unpaid part needs a customer on file.");
            else if (customer.AccSubClassId is null)
                problems.Add($"The customer {customer.Name} has no ledger head: map one under Sales → Customers.");
        }
        PaymentSourceLedgerHead? source = null;
        if (paid > 0 && invoice.InvPaymentSourceDId is int lineId)
        {
            source = await mappings.GetPaymentSourceHeadAsync(lineId, cancellationToken);
            if (source is null)
                problems.Add("Choose the payment source and its type on the invoice.");
        }
        else if (paid > 0 && !await mappings.LedgerHeadExistsAsync(VoucherLookups.CashSalesHeadId, cancellationToken))
            problems.Add($"The cash sales ledger head (id {VoucherLookups.CashSalesHeadId}) isn't in the chart of accounts.");
        ThrowIfAny(problems);

        var lines = new List<VoucherLineRequest>();
        if (due > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, customer!.AccSubClassId!.Value, customer.Id, due, 0,
                $"Receivable from {customer.Name}"));
        if (paid > 0 && source is not null)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, source.AccSubClassId, null, paid, 0,
                $"Received in {source.SourceName} · {source.PaymentFor}"));
        else if (paid > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, VoucherLookups.CashSalesHeadId, null, paid, 0,
                invoice.SaleType == SaleTypes.Credit ? "Paid now" : "Cash service invoice"));
        foreach (var (head, names, amount) in Share(heads, total))
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, head, null, 0, amount, names));

        return new SourceVoucherRequest
        {
            VoucherTypeName = VoucherLookups.BillVoucher,
            VoucherDate = invoice.InvoiceDate,
            Narration = $"{invoice.SaleType} service invoice {invoice.InvoiceNo} to {invoice.CustomerName ?? "a walk-in customer"}",
            ServiceInvoiceId = invoice.Id,
            Lines = lines
        };
    }

    /// <summary>The heads with one more amount: added to the head's entry if it is already there.</summary>
    private static List<(int Head, string Names, decimal Weight)> WithHead(
        List<(int Head, string Names, decimal Weight)> heads, int head, string name, decimal weight)
    {
        var index = heads.FindIndex(h => h.Head == head);
        if (index < 0)
            return [.. heads, (head, name, weight)];
        var list = heads.ToList();
        list[index] = (head, $"{list[index].Names}, {name}", list[index].Weight + weight);
        return list;
    }

    // ---- Shared

    /// <summary>The invoice's line totals by ledger head (through each product's category), with the category names.</summary>
    private async Task<List<(int Head, string Names, decimal Weight)>> CategoryHeadsAsync(
        int organizationId, IEnumerable<(int ProductId, decimal Amount)> lines, List<string> problems, CancellationToken cancellationToken)
    {
        var list = lines.ToList();
        if (list.Count == 0)
        {
            problems.Add("The invoice has no items.");
            return [];
        }

        var products = (await mappings.GetProductHeadsAsync(organizationId, list.Select(l => l.ProductId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.ProductId);
        var noCategory = products.Values.Where(p => p.CategoryId is null).Select(p => p.ProductName).Distinct().ToList();
        var unmapped = products.Values.Where(p => p.CategoryId is not null && p.AccSubClassId is null)
            .Select(p => p.CategoryName!).Distinct().ToList();
        if (noCategory.Count > 0)
            problems.Add($"These items have no category: {string.Join(", ", noCategory)}. Set it under Inventory → Products.");
        if (unmapped.Count > 0)
            problems.Add($"These categories have no ledger head: {string.Join(", ", unmapped)}. Map them under Inventory → Categories.");
        if (list.Any(l => !products.ContainsKey(l.ProductId)))
            problems.Add("Some items on the invoice no longer exist.");
        if (problems.Count > 0)
            return [];

        return list
            .GroupBy(l => products[l.ProductId].AccSubClassId!.Value)
            .Select(g => (g.Key,
                string.Join(", ", g.Select(l => products[l.ProductId].CategoryName).Distinct()),
                g.Sum(l => l.Amount)))
            .ToList();
    }

    /// <summary>The total, the paid-now part and the unpaid part, in BDT (2 decimals).</summary>
    private static (decimal Total, decimal Paid, decimal Due) Amounts(decimal total, decimal paid, decimal rate, List<string> problems)
    {
        var totalBdt = Bdt(total, rate);
        if (totalBdt <= 0)
            problems.Add("The invoice total is 0, so there is nothing to post.");
        var paidBdt = Math.Min(Bdt(paid, rate), totalBdt);
        return (totalBdt, paidBdt, totalBdt - paidBdt);
    }

    /// <summary>
    /// Shares the total out over the heads in proportion to their line totals; the largest takes the rounding, so
    /// the shares add up to the total exactly. Heads with nothing are left out.
    /// </summary>
    private static IEnumerable<(int Head, string Names, decimal Amount)> Share(List<(int Head, string Names, decimal Weight)> heads, decimal total)
    {
        var weight = heads.Sum(h => h.Weight);
        if (weight <= 0 || total <= 0)
            yield break;

        var largest = heads.OrderByDescending(h => h.Weight).First().Head;
        var others = heads.Where(h => h.Head != largest)
            .Select(h => (h.Head, h.Names, Amount: Math.Round(total * h.Weight / weight, 2, MidpointRounding.AwayFromZero)))
            .ToList();
        var first = heads.First(h => h.Head == largest);
        yield return (first.Head, first.Names, total - others.Sum(o => o.Amount));
        foreach (var other in others.Where(o => o.Amount > 0))
            yield return other;
    }

    private static decimal Bdt(decimal amount, decimal rate) => Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);

    private static VoucherLineRequest Line(
        long currencyId, decimal rate, int head, int? partyId, decimal debit, decimal credit, string narration) => new()
    {
        AccountId = head,
        PartyId = partyId,
        DebitAmount = debit,
        CreditAmount = credit,
        CurrencyLookupDataId = currencyId,
        LocalExchangeRate = rate,
        LineNarration = narration.Length > 300 ? narration[..300] : narration
    };

    private static void ThrowIfAny(List<string> problems)
    {
        if (problems.Count > 0)
            throw new BusinessRuleException($"The bill voucher can't be made: {string.Join(" ", problems)}");
    }
}
