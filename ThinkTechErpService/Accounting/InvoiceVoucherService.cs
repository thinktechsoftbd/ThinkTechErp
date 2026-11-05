using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpService.Purchase;
using ThinkTechErpService.Sales;

namespace ThinkTechErpService.Accounting;

/// <summary>
/// Finalizing an invoice: posts it (if it is still a draft) and posts its Bill Voucher, worked out from the ledger
/// mappings and linked to the invoice, which then stays locked until the voucher is deleted.
/// <list type="bullet">
/// <item>Purchase: Dr each item category's head; Cr the supplier's head for the unpaid part (credit) and the
/// payment source type's head for what was paid now (all of a cash purchase).</item>
/// <item>Sales: Dr the customer's head for the unpaid part (credit) and the fixed cash sales head for what was paid
/// now (all of a cash sale); Cr each item category's head.</item>
/// </list>
/// The invoice total (in BDT) is shared out over the categories in proportion to their line totals, so tax,
/// charges, round-off and discounts are carried with the goods and the voucher always balances.
/// </summary>
public interface IInvoiceVoucherService
{
    Task<PurchaseInvoice?> FinalizePurchaseInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> FinalizeSalesInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class InvoiceVoucherService(
    IPurchaseDocumentService purchases,
    ISalesDocumentService sales,
    IInvoiceVoucherRepository mappings,
    IVoucherService vouchers) : IInvoiceVoucherService
{
    public async Task<PurchaseInvoice?> FinalizePurchaseInvoiceAsync(
        int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var invoice = await purchases.GetInvoiceAsync(organizationId, id, cancellationToken);
        if (invoice is null)
            return null;
        RequireFinalizable(invoice.Status, invoice.BillVoucherRefNo);

        // Everything is checked before the invoice is posted, so a missing mapping leaves a draft as it was.
        var request = await BuildPurchaseVoucherAsync(organizationId, invoice, cancellationToken);
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
        RequireFinalizable(invoice.Status, invoice.BillVoucherRefNo);

        var request = await BuildSalesVoucherAsync(organizationId, invoice, cancellationToken);
        var posting = invoice.Status == DocumentStatus.Draft;
        if (posting)
            await sales.PostInvoiceAsync(organizationId, id, userId, cancellationToken);
        await CreateVoucherAsync(organizationId, request, userId, posting, cancellationToken);
        return await sales.GetInvoiceAsync(organizationId, id, cancellationToken);
    }

    private static void RequireFinalizable(string status, string? billVoucherRefNo)
    {
        if (status == DocumentStatus.Cancelled)
            throw new BusinessRuleException("A cancelled invoice can't be finalized.");
        if (billVoucherRefNo is not null)
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
        if (paid > 0 && !await mappings.LedgerHeadExistsAsync(VoucherLookups.CashSalesHeadId, cancellationToken))
            problems.Add($"The cash sales ledger head (id {VoucherLookups.CashSalesHeadId}) isn't in the chart of accounts.");
        ThrowIfAny(problems);

        var lines = new List<VoucherLineRequest>();
        if (due > 0)
            lines.Add(Line(invoice.CurrencyLookupDataId, invoice.ExchangeRate, customer!.AccSubClassId!.Value, customer.Id, due, 0,
                $"Receivable from {customer.Name}"));
        if (paid > 0)
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
