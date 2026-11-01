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
    Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Saves a Journal, Payment, Receipt or Contra voucher as a draft; with post it is posted too.</summary>
    Task<Voucher> CreateVoucherAsync(int organizationId, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default);
    Task<Voucher?> UpdateVoucherAsync(int organizationId, long id, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<Voucher?> PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<Voucher?> CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>Supplier bill payment: Dr the supplier's payable head, Cr cash or bank. Posted at once.</summary>
    Task<Voucher> CreateBillPaymentAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>Customer bill collection: Dr cash or bank, Cr the customer's receivable head. Posted at once.</summary>
    Task<Voucher> CreateBillCollectionAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default);
}

public sealed class VoucherService(
    IVoucherRepository repository, IChartOfAccountsRepository chart, IOwnershipRepository ownership) : IVoucherService
{
    private static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal)
        { DocumentStatus.Draft, DocumentStatus.Posted, DocumentStatus.Cancelled };

    public Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.VoucherType = Text.Clean(query.VoucherType);
        query.Status = Text.Clean(query.Status);
        if (query.VoucherType is not null && !VoucherType.All.Contains(query.VoucherType))
            throw new BusinessRuleException("Unknown voucher type.");
        if (query.Status is not null && !Statuses.Contains(query.Status))
            throw new BusinessRuleException("The status must be Draft, Posted or Cancelled.");
        return repository.GetVouchersAsync(organizationId, query, cancellationToken);
    }

    public Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => repository.GetVoucherAsync(organizationId, id, cancellationToken);

    public async Task<Voucher> CreateVoucherAsync(int organizationId, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default)
    {
        var voucher = await BuildAsync(organizationId, 0, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.SaveVoucherAsync(organizationId, voucher, post, userId, cancellationToken));
        return (await repository.GetVoucherAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Voucher?> UpdateVoucherAsync(int organizationId, long id, VoucherUpsertRequest request, bool post, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetVoucherAsync(organizationId, id, cancellationToken);
        if (existing is null)
            return null;
        if (!VoucherType.General.Contains(existing.VoucherType))
            throw new BusinessRuleException("Bill payments and collections can't be edited. Cancel it and enter a new one.");

        var voucher = await BuildAsync(organizationId, id, request, cancellationToken);
        await BusinessDbErrors.ExecuteAsync(() => repository.SaveVoucherAsync(organizationId, voucher, post, userId, cancellationToken));
        return await repository.GetVoucherAsync(organizationId, id, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => repository.DeleteVoucherAsync(organizationId, id, cancellationToken);

    public async Task<Voucher?> PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var voucher = await repository.GetVoucherAsync(organizationId, id, cancellationToken);
        if (voucher is null)
            return null;

        // Heads may have been cleared in the chart since the draft was saved.
        await CheckLinesAsync(organizationId, voucher.Lines.Select(l => new VoucherLineRequest
        {
            AccountId = l.AccountId,
            SupplierId = l.SupplierId,
            CustomerId = l.CustomerId,
            DebitAmount = l.DebitAmount,
            CreditAmount = l.CreditAmount
        }).ToList(), cancellationToken);
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
        => CreateBillAsync(organizationId, VoucherType.BillPayment, request, userId, cancellationToken);

    public Task<Voucher> CreateBillCollectionAsync(int organizationId, BillVoucherRequest request, int userId, CancellationToken cancellationToken = default)
        => CreateBillAsync(organizationId, VoucherType.BillCollection, request, userId, cancellationToken);

    private async Task<Voucher> CreateBillAsync(
        int organizationId, string voucherType, BillVoucherRequest request, int userId, CancellationToken cancellationToken)
    {
        var isPayment = voucherType == VoucherType.BillPayment;
        var partyType = isPayment ? PartyType.Supplier : PartyType.Customer;
        var partyLabel = isPayment ? "supplier" : "customer";

        var party = (await chart.GetPartiesAsync(organizationId, partyType, null, [request.PartyId], 1, cancellationToken))
            .FirstOrDefault() ?? throw new BusinessRuleException($"The {partyLabel} doesn't exist.");
        var heads = await chart.GetLedgerHeadsAsync(organizationId, null, null, null,
            [request.PartyAccountId, request.CashAccountId], true, 2, cancellationToken);

        var partyHead = heads.FirstOrDefault(h => h.Id == request.PartyAccountId)
            ?? throw new BusinessRuleException("The ledger head doesn't exist.");
        if (partyHead.PartyType != partyType)
            throw new BusinessRuleException(isPayment
                ? "Choose a ledger head that keeps the supplier ledger, such as Accounts Payable."
                : "Choose a ledger head that keeps the customer ledger, such as Accounts Receivable.");
        var cashHead = heads.FirstOrDefault(h => h.Id == request.CashAccountId)
            ?? throw new BusinessRuleException("The cash or bank head doesn't exist.");
        if (cashHead.LedgerType is not (LedgerType.Cash or LedgerType.Bank))
            throw new BusinessRuleException(isPayment ? "Pay from a Cash or Bank ledger head." : "Deposit to a Cash or Bank ledger head.");
        foreach (var head in new[] { partyHead, cashHead }.Where(h => !h.IsActive))
            throw new BusinessRuleException($"{head.Code} {head.Name} is inactive. Tick it in the chart of accounts to post to it.");

        var method = request.PaymentMethod.Trim();
        if (!PaymentMethod.All.Contains(method))
            throw new BusinessRuleException("The payment method must be Cash, Bank, Cheque, MobileWallet, Card or Other.");

        var amount = TaxCalculator.Round(request.Amount);
        var partyLine = new VoucherLine
        {
            AccountId = partyHead.Id,
            SupplierId = isPayment ? party.Id : null,
            CustomerId = isPayment ? null : party.Id,
            DebitAmount = isPayment ? amount : 0,
            CreditAmount = isPayment ? 0 : amount
        };
        var cashLine = new VoucherLine
        {
            AccountId = cashHead.Id,
            DebitAmount = isPayment ? 0 : amount,
            CreditAmount = isPayment ? amount : 0
        };
        var lines = isPayment ? new[] { partyLine, cashLine } : new[] { cashLine, partyLine };
        for (var index = 0; index < lines.Length; index++)
            lines[index].LineNo = index + 1;

        var voucher = new Voucher
        {
            VoucherType = voucherType,
            VoucherDate = VoucherDate(request.VoucherDate),
            SupplierId = isPayment ? party.Id : null,
            CustomerId = isPayment ? null : party.Id,
            PaymentMethod = method,
            ReferenceNo = Text.Clean(request.ReferenceNo),
            ChequeDate = method == PaymentMethod.Cheque ? request.ChequeDate : null,
            Narration = Text.Clean(request.Narration)
                ?? (isPayment ? $"Bill payment to {party.Name}" : $"Bill collection from {party.Name}"),
            Lines = lines
        };
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.SaveVoucherAsync(organizationId, voucher, true, userId, cancellationToken));
        return (await repository.GetVoucherAsync(organizationId, id, cancellationToken))!;
    }

    private async Task<Voucher> BuildAsync(int organizationId, long id, VoucherUpsertRequest request, CancellationToken cancellationToken)
    {
        var type = request.VoucherType.Trim();
        if (!VoucherType.General.Contains(type))
            throw new BusinessRuleException("The voucher type must be Journal, Payment, Receipt or Contra.");

        foreach (var line in request.Lines)
        {
            line.DebitAmount = TaxCalculator.Round(line.DebitAmount);
            line.CreditAmount = TaxCalculator.Round(line.CreditAmount);
            line.LineNarration = Text.Clean(line.LineNarration);
        }
        var heads = await CheckLinesAsync(organizationId, request.Lines, cancellationToken);

        return new Voucher
        {
            Id = id,
            VoucherType = type,
            VoucherDate = VoucherDate(request.VoucherDate),
            ReferenceNo = Text.Clean(request.ReferenceNo),
            Narration = Text.Clean(request.Narration),
            Lines = request.Lines.Select((line, index) =>
            {
                var partyType = heads[line.AccountId].PartyType;
                return new VoucherLine
                {
                    LineNo = index + 1,
                    AccountId = line.AccountId,
                    SupplierId = partyType == PartyType.Supplier ? line.SupplierId : null,
                    CustomerId = partyType == PartyType.Customer ? line.CustomerId : null,
                    DebitAmount = line.DebitAmount,
                    CreditAmount = line.CreditAmount,
                    LineNarration = line.LineNarration
                };
            }).ToList()
        };
    }

    /// <summary>Each line is a debit or a credit to an active ledger head, naming the supplier or customer its head needs.</summary>
    private async Task<Dictionary<int, LedgerHeadOption>> CheckLinesAsync(
        int organizationId, IReadOnlyList<VoucherLineRequest> lines, CancellationToken cancellationToken)
    {
        if (lines.Count < 2)
            throw new BusinessRuleException("A voucher needs at least two lines: a debit and a credit.");

        var ids = lines.Select(l => l.AccountId).Distinct().ToList();
        var heads = (await chart.GetLedgerHeadsAsync(organizationId, null, null, null, ids, true, ids.Count, cancellationToken))
            .ToDictionary(h => h.Id);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var label = $"Line {index + 1}";
            if (line.DebitAmount < 0 || line.CreditAmount < 0 || (line.DebitAmount > 0) == (line.CreditAmount > 0))
                throw new BusinessRuleException($"{label}: enter either a debit or a credit amount.");
            if (!heads.TryGetValue(line.AccountId, out var head))
                throw new BusinessRuleException($"{label}: choose a ledger head (a sub class) from the chart of accounts.");
            if (!head.IsActive)
                throw new BusinessRuleException($"{label}: {head.Code} {head.Name} is inactive. Tick it in the chart of accounts to post to it.");
            if (head.PartyType == PartyType.Supplier && line.SupplierId is null)
                throw new BusinessRuleException($"{label}: choose the supplier for {head.Name}.");
            if (head.PartyType == PartyType.Customer && line.CustomerId is null)
                throw new BusinessRuleException($"{label}: choose the customer for {head.Name}.");
        }

        await ownership.EnsureAsync(organizationId, "supplier",
            lines.Where(l => heads[l.AccountId].PartyType == PartyType.Supplier).Select(l => l.SupplierId), cancellationToken);
        await ownership.EnsureAsync(organizationId, "customer",
            lines.Where(l => heads[l.AccountId].PartyType == PartyType.Customer).Select(l => l.CustomerId), cancellationToken);
        return heads;
    }

    private static DateOnly VoucherDate(DateOnly date) => date == default ? DateOnly.FromDateTime(DateTime.UtcNow) : date;
}
