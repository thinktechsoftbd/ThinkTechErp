using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpRepository.Common;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Accounting;

public interface IVoucherService
{
    Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default);
    /// <summary>Cash and Bank Payment Vouchers that name a party: payments to suppliers.</summary>
    Task<PagedResult<Voucher>> GetBillPaymentsAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default);
    /// <summary>Cash and Bank Receive Vouchers that name a party: collections from customers.</summary>
    Task<PagedResult<Voucher>> GetBillCollectionsAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default);
    Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>The "Voucher Type" lookup list.</summary>
    Task<IReadOnlyList<LookupValueOption>> GetVoucherTypesAsync(CancellationToken cancellationToken = default);
    /// <summary>The "Payment Method" lookup list.</summary>
    Task<IReadOnlyList<LookupValueOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default);
    /// <summary>Saves a voucher as a draft; with post it is posted too.</summary>
    Task<Voucher> CreateVoucherAsync(int organizationId, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default);
    Task<Voucher?> UpdateVoucherAsync(int organizationId, long id, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<Voucher?> PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<Voucher?> CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>Supplier bill payment: Dr the supplier's payable head, Cr cash or bank. Posted at once.</summary>
    Task<Voucher> CreateBillPaymentAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>Customer bill collection: Dr cash or bank, Cr the customer's receivable head. Posted at once.</summary>
    Task<Voucher> CreateBillCollectionAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default);
}

public sealed class VoucherService(
    IVoucherRepository repository, ILedgerLookupRepository lookups, IOwnershipRepository ownership) : IVoucherService
{
    private static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal)
        { DocumentStatus.Draft, DocumentStatus.Posted, DocumentStatus.Cancelled };

    public Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.Status = Text.Clean(query.Status);
        if (query.Status is not null && !Statuses.Contains(query.Status))
            throw new BusinessRuleException("The status must be Draft, Posted or Cancelled.");
        return repository.GetVouchersAsync(organizationId, query, cancellationToken);
    }

    public Task<PagedResult<Voucher>> GetBillPaymentsAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default)
        => GetBillsAsync(organizationId, VoucherLookups.BillPaymentTypes, query, cancellationToken);

    public Task<PagedResult<Voucher>> GetBillCollectionsAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default)
        => GetBillsAsync(organizationId, VoucherLookups.BillCollectionTypes, query, cancellationToken);

    private async Task<PagedResult<Voucher>> GetBillsAsync(
        int organizationId, IReadOnlyList<string> typeNames, VoucherQuery query, CancellationToken cancellationToken)
    {
        var types = await GetVoucherTypesAsync(cancellationToken);
        query.BillTypeIds = types.Where(t => typeNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).Select(t => t.Id).ToList();
        return await GetVouchersAsync(organizationId, query, cancellationToken);
    }

    public Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => repository.GetVoucherAsync(organizationId, id, cancellationToken);

    public Task<IReadOnlyList<LookupValueOption>> GetVoucherTypesAsync(CancellationToken cancellationToken = default)
        => lookups.GetLookupValuesAsync(VoucherLookups.VoucherTypeTable, cancellationToken);

    public Task<IReadOnlyList<LookupValueOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default)
        => lookups.GetLookupValuesAsync(VoucherLookups.PaymentMethodTable, cancellationToken);

    public async Task<Voucher> CreateVoucherAsync(int organizationId, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default)
    {
        var voucher = await BuildAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.SaveVoucherAsync(organizationId, voucher, post, userId, cancellationToken));
        return (await repository.GetVoucherAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Voucher?> UpdateVoucherAsync(int organizationId, long id, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetVoucherAsync(organizationId, id, cancellationToken);
        if (existing is null)
            return null;

        var voucher = await BuildAsync(organizationId, existing, request, cancellationToken);
        await BusinessDbErrors.ExecuteAsync(() => repository.SaveVoucherAsync(organizationId, voucher, post, userId, cancellationToken));
        return await repository.GetVoucherAsync(organizationId, id, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
        => repository.DeleteVoucherAsync(organizationId, id, userId, cancellationToken);

    public async Task<Voucher?> PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        // The lines were checked when the draft was saved, and a sub class with voucher lines can't be deleted.
        if (await repository.GetVoucherAsync(organizationId, id, cancellationToken) is null)
            return null;
        await repository.PostVoucherAsync(organizationId, id, userId, cancellationToken);
        return await repository.GetVoucherAsync(organizationId, id, cancellationToken);
    }

    public async Task<Voucher?> CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        if (await repository.GetVoucherAsync(organizationId, id, cancellationToken) is null)
            return null;
        await repository.CancelVoucherAsync(organizationId, id, userId, cancellationToken);
        return await repository.GetVoucherAsync(organizationId, id, cancellationToken);
    }

    public Task<Voucher> CreateBillPaymentAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default)
        => CreateBillAsync(organizationId, true, request, userId, cancellationToken);

    public Task<Voucher> CreateBillCollectionAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default)
        => CreateBillAsync(organizationId, false, request, userId, cancellationToken);

    private async Task<Voucher> CreateBillAsync(
        int organizationId, bool isPayment, BillVoucherRequest request, int userId, CancellationToken cancellationToken)
    {
        var partyType = isPayment ? PartyType.Supplier : PartyType.Customer;
        var partyLabel = isPayment ? "supplier" : "customer";

        var allowed = isPayment ? VoucherLookups.BillPaymentTypes : VoucherLookups.BillCollectionTypes;
        var type = await VoucherTypeAsync(request.VoucherTypeId, null, cancellationToken);
        if (!allowed.Contains(type.Name, StringComparer.OrdinalIgnoreCase))
            throw new BusinessRuleException($"A bill {(isPayment ? "payment" : "collection")} is a {string.Join(" or a ", allowed)}.");
        var method = await PaymentMethodAsync(request.PaymentMethodId, null, cancellationToken);
        CheckCheque(request, type.Name, method?.Name);

        var party = (await lookups.GetPartiesAsync(organizationId, partyType, null, [request.PartyId], 1, cancellationToken))
            .FirstOrDefault() ?? throw new BusinessRuleException($"The {partyLabel} doesn't exist.");
        if (request.PartyAccountId == request.CashAccountId)
            throw new BusinessRuleException(isPayment
                ? "Pay from a cash or bank head other than the supplier's head."
                : "Deposit to a cash or bank head other than the customer's head.");
        var heads = await lookups.GetLedgerHeadsAsync(null, [request.PartyAccountId, request.CashAccountId], 2, cancellationToken);
        var partyHead = heads.FirstOrDefault(h => h.Id == request.PartyAccountId)
            ?? throw new BusinessRuleException($"The {partyLabel}'s ledger head doesn't exist.");
        var cashHead = heads.FirstOrDefault(h => h.Id == request.CashAccountId)
            ?? throw new BusinessRuleException("The cash or bank head doesn't exist.");

        var currency = DefaultCurrency(await lookups.GetLookupValuesAsync(AccountingRules.CurrencyTable, cancellationToken));
        var amount = TaxCalculator.Round(request.Amount);
        var partyLine = new VoucherLine
        {
            AccountId = partyHead.Id,
            PartyId = party.Id,
            DebitAmount = isPayment ? amount : 0,
            CreditAmount = isPayment ? 0 : amount,
            CurrencyLookupDataId = currency,
            LocalExchangeRate = 1
        };
        var cashLine = new VoucherLine
        {
            AccountId = cashHead.Id,
            DebitAmount = isPayment ? 0 : amount,
            CreditAmount = isPayment ? amount : 0,
            CurrencyLookupDataId = currency,
            LocalExchangeRate = 1
        };
        var lines = isPayment ? new[] { partyLine, cashLine } : new[] { cashLine, partyLine };
        for (var index = 0; index < lines.Length; index++)
            lines[index].LineNo = index + 1;

        var voucher = new Voucher
        {
            VoucherTypeId = type.Id,
            VoucherDate = VoucherDate(request.VoucherDate),
            PaymentMethodId = method?.Id,
            ChequeNo = request.ChequeNo,
            ChequeDate = request.ChequeDate,
            PayType = request.PayType,
            Narration = Text.Clean(request.Narration)
                ?? (isPayment ? $"Bill payment to {party.Name}" : $"Bill collection from {party.Name}"),
            Lines = lines
        };
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.SaveVoucherAsync(organizationId, voucher, true, userId, cancellationToken));
        return (await repository.GetVoucherAsync(organizationId, id, cancellationToken))!;
    }

    /// <param name="existing">The saved draft when editing: it may keep a type or method since made inactive.</param>
    private async Task<Voucher> BuildAsync(int organizationId, Voucher? existing, VoucherUpsertRequest request, CancellationToken cancellationToken)
    {
        var type = await VoucherTypeAsync(request.VoucherTypeId, existing?.VoucherTypeId, cancellationToken);
        var method = await PaymentMethodAsync(request.PaymentMethodId, existing?.PaymentMethodId, cancellationToken);
        CheckCheque(request, type.Name, method?.Name);

        foreach (var line in request.Lines)
        {
            line.DebitAmount = TaxCalculator.Round(line.DebitAmount);
            line.CreditAmount = TaxCalculator.Round(line.CreditAmount);
            line.LineNarration = Text.Clean(line.LineNarration);
        }
        var defaultCurrency = await CheckLinesAsync(organizationId, request.Lines, cancellationToken);

        return new Voucher
        {
            Id = existing?.Id ?? 0,
            VoucherTypeId = type.Id,
            VoucherDate = VoucherDate(request.VoucherDate),
            PaymentMethodId = method?.Id,
            ChequeNo = request.ChequeNo,
            ChequeDate = request.ChequeDate,
            PayType = request.PayType,
            Narration = Text.Clean(request.Narration),
            Lines = request.Lines.Select((line, index) => new VoucherLine
            {
                Id = line.Id ?? 0,
                LineNo = index + 1,
                AccountId = line.AccountId,
                PartyId = line.PartyId,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                CurrencyLookupDataId = line.CurrencyLookupDataId ?? defaultCurrency,
                LocalExchangeRate = line.LocalExchangeRate ?? 1,
                LineNarration = line.LineNarration
            }).ToList()
        };
    }

    private async Task<LookupValueOption> VoucherTypeAsync(long id, long? keptId, CancellationToken cancellationToken)
    {
        var type = (await GetVoucherTypesAsync(cancellationToken)).FirstOrDefault(t => t.Id == id)
            ?? throw new BusinessRuleException("Choose a voucher type from the Voucher Type lookup list.");
        if (!type.IsActive && type.Id != keptId)
            throw new BusinessRuleException($"The voucher type {type.Name} is inactive.");
        return type;
    }

    private async Task<LookupValueOption?> PaymentMethodAsync(long? id, long? keptId, CancellationToken cancellationToken)
    {
        if (id is null)
            return null;
        var method = (await GetPaymentMethodsAsync(cancellationToken)).FirstOrDefault(m => m.Id == id)
            ?? throw new BusinessRuleException("Choose a payment method from the Payment Method lookup list.");
        if (!method.IsActive && method.Id != keptId)
            throw new BusinessRuleException($"The payment method {method.Name} is inactive.");
        return method;
    }

    /// <summary>
    /// The cheque details belong to a cheque only, and a bank voucher paid by cheque must have all three:
    /// the cheque no, the date on the cheque and how it is crossed (A/C Pay or Cash).
    /// </summary>
    private static void CheckCheque(IChequeDetails request, string voucherType, string? paymentMethod)
    {
        if (!VoucherLookups.IsCheque(paymentMethod))
        {
            request.ChequeNo = null;
            request.ChequeDate = null;
            request.PayType = null;
            return;
        }

        request.ChequeNo = Text.Clean(request.ChequeNo);
        request.PayType = Text.Clean(request.PayType);
        if (request.PayType is not null && !ChequePayType.All.Contains(request.PayType))
            throw new BusinessRuleException("The pay type must be AcPay (A/C Pay) or Cash.");
        if (!VoucherLookups.IsBankVoucher(voucherType))
            return;

        var missing = new List<string>();
        if (request.ChequeNo is null) missing.Add("cheque no");
        if (request.ChequeDate is null) missing.Add("cheque date");
        if (request.PayType is null) missing.Add("pay type (A/C Pay or Cash)");
        if (missing.Count > 0)
            throw new BusinessRuleException($"A {voucherType} paid by cheque needs the {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Each line is a debit or a credit (in BDT) to a sub class of the chart, in a currency of the Currency list,
    /// optionally naming one of the organization's suppliers or customers. Returns the BDT lookup id.
    /// </summary>
    private async Task<long> CheckLinesAsync(
        int organizationId, IReadOnlyList<VoucherLineRequest> lines, CancellationToken cancellationToken)
    {
        if (lines.Count < 2)
            throw new BusinessRuleException("A voucher needs at least two lines: a debit and a credit.");

        var ids = lines.Select(l => l.AccountId).Distinct().ToList();
        var heads = (await lookups.GetLedgerHeadsAsync(null, ids, ids.Count, cancellationToken)).Select(h => h.Id).ToHashSet();
        var currencies = await lookups.GetLookupValuesAsync(AccountingRules.CurrencyTable, cancellationToken);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var label = $"Line {index + 1}";
            if (line.DebitAmount < 0 || line.CreditAmount < 0 || (line.DebitAmount > 0) == (line.CreditAmount > 0))
                throw new BusinessRuleException($"{label}: enter either a debit or a credit amount.");
            if (!heads.Contains(line.AccountId))
                throw new BusinessRuleException($"{label}: choose a ledger head (a sub class) from the chart of accounts.");
            if (line.CurrencyLookupDataId is { } currency && currencies.All(c => c.Id != currency))
                throw new BusinessRuleException($"{label}: choose a currency from the Currency lookup list.");
            if (line.LocalExchangeRate <= 0)
                throw new BusinessRuleException($"{label}: the exchange rate must be more than 0.");
        }

        await ownership.EnsureAsync(organizationId, "party", lines.Select(l => l.PartyId), cancellationToken);
        return DefaultCurrency(currencies);
    }

    private static long DefaultCurrency(IReadOnlyList<LookupValueOption> currencies)
        => currencies.FirstOrDefault(c => string.Equals(c.Name, AccountingRules.DefaultCurrency, StringComparison.OrdinalIgnoreCase))?.Id
           ?? throw new BusinessRuleException($"Add {AccountingRules.DefaultCurrency} to the Currency lookup list under Settings → Lookup Data.");

    private static DateOnly VoucherDate(DateOnly date) => date == default ? DateOnly.FromDateTime(DateTime.UtcNow) : date;
}
