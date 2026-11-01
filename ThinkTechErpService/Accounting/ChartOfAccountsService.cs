using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Accounting;

public interface IChartOfAccountsService
{
    Task<IReadOnlyList<ChartAccount>> GetAccountsAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<ChartAccount?> GetAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        int organizationId, string? search, IReadOnlyCollection<string>? ledgerTypes, string? partyType,
        IReadOnlyCollection<int>? ids, bool includeInactive, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default);
    Task<ChartAccount> CreateAccountAsync(int organizationId, ChartAccountUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ChartAccount?> UpdateAccountAsync(int organizationId, int accountId, ChartAccountUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ChartAccount?> SetActiveAsync(int organizationId, int accountId, bool isActive, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default);
    /// <summary>Adds a ready-made chart (assets, liabilities, equity, income, expenses) to an organization without one.</summary>
    Task<IReadOnlyList<ChartAccount>> ImportStandardChartAsync(int organizationId, int userId, CancellationToken cancellationToken = default);
}

public sealed class ChartOfAccountsService(IChartOfAccountsRepository repository) : IChartOfAccountsService
{
    public Task<IReadOnlyList<ChartAccount>> GetAccountsAsync(int organizationId, CancellationToken cancellationToken = default)
        => repository.GetAccountsAsync(organizationId, cancellationToken);

    public Task<ChartAccount?> GetAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default)
        => repository.GetAccountAsync(organizationId, accountId, cancellationToken);

    public Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        int organizationId, string? search, IReadOnlyCollection<string>? ledgerTypes, string? partyType,
        IReadOnlyCollection<int>? ids, bool includeInactive, int limit, CancellationToken cancellationToken = default)
    {
        if (ledgerTypes is not null && ledgerTypes.Any(type => !LedgerType.All.Contains(type)))
            throw new BusinessRuleException("The ledger type must be General, Cash or Bank.");
        partyType = Text.Clean(partyType);
        if (partyType is not null && !PartyType.All.Contains(partyType))
            throw new BusinessRuleException("The party type must be Supplier or Customer.");
        return repository.GetLedgerHeadsAsync(organizationId, Text.Clean(search), ledgerTypes is { Count: > 0 } ? ledgerTypes : null,
            partyType, ids is { Count: > 0 } ? ids : null, includeInactive, limit, cancellationToken);
    }

    public Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!PartyType.All.Contains(partyType))
            throw new BusinessRuleException("The party type must be Supplier or Customer.");
        return repository.GetPartiesAsync(organizationId, partyType, Text.Clean(search), ids is { Count: > 0 } ? ids : null,
            limit, cancellationToken);
    }

    public async Task<ChartAccount> CreateAccountAsync(int organizationId, ChartAccountUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var account = await NormalizeAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateAccountAsync(organizationId, account, userId, cancellationToken));
        return (await repository.GetAccountAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<ChartAccount?> UpdateAccountAsync(int organizationId, int accountId, ChartAccountUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAccountAsync(organizationId, accountId, cancellationToken);
        if (existing is null)
            return null;

        var account = await NormalizeAsync(organizationId, existing, request, cancellationToken);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateAccountAsync(organizationId, account, userId, cancellationToken))
            ? await repository.GetAccountAsync(organizationId, accountId, cancellationToken)
            : null;
    }

    public async Task<ChartAccount?> SetActiveAsync(int organizationId, int accountId, bool isActive, int userId, CancellationToken cancellationToken = default)
        => await repository.SetActiveAsync(organizationId, accountId, isActive, userId, cancellationToken)
            ? await repository.GetAccountAsync(organizationId, accountId, cancellationToken)
            : null;

    public Task<DeleteOutcome> DeleteAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default)
        => repository.DeleteAccountAsync(organizationId, accountId, cancellationToken);

    public async Task<IReadOnlyList<ChartAccount>> ImportStandardChartAsync(int organizationId, int userId, CancellationToken cancellationToken = default)
    {
        await BusinessDbErrors.ExecuteAsync(() => repository.ImportAsync(organizationId, StandardChart.Accounts, userId, cancellationToken));
        return await repository.GetAccountsAsync(organizationId, cancellationToken);
    }

    /// <summary>Checks the request and works out the level, nature and ledger settings from the parent.</summary>
    private async Task<ChartAccount> NormalizeAsync(
        int organizationId, ChartAccount? existing, ChartAccountUpsertRequest request, CancellationToken cancellationToken)
    {
        var account = new ChartAccount
        {
            Id = existing?.Id ?? 0,
            ParentId = request.ParentId,
            Code = Text.Clean(request.Code) ?? existing?.Code ?? string.Empty,
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            Description = Text.Clean(request.Description),
            IsActive = request.IsActive
        };
        if (account.Name.Length == 0)
            throw new BusinessRuleException("Enter the account name.");

        if (request.ParentId is null)
        {
            if (existing is { Level: > 1 })
                throw new BusinessRuleException($"Choose the {AccountingRules.LevelName(existing.Level - 1)} this account belongs to.");
            account.Level = 1;
            account.Nature = Text.Clean(request.Nature)
                ?? throw new BusinessRuleException("Choose what the parent class is: Asset, Liability, Equity, Income or Expense.");
            if (!AccountNature.All.Contains(account.Nature))
                throw new BusinessRuleException("The nature must be Asset, Liability, Equity, Income or Expense.");
        }
        else
        {
            var parent = await repository.GetAccountAsync(organizationId, request.ParentId.Value, cancellationToken)
                ?? throw new BusinessRuleException("The parent account doesn't exist.");
            if (parent.Level >= AccountingRules.LedgerLevel)
                throw new BusinessRuleException("A sub class is a ledger head; accounts can't be added below it.");
            if (existing is not null && parent.Level != existing.Level - 1)
                throw new BusinessRuleException(existing.Level == 1
                    ? "A parent class is the top of the chart; it can't move under another account."
                    : $"Move the account only under another {AccountingRules.LevelName(existing.Level - 1)}.");
            account.Level = parent.Level + 1;
            account.Nature = parent.Nature;
            // An account under a cleared checkbox starts cleared too.
            account.IsActive = request.IsActive && parent.IsActive;
        }

        if (account.Level == AccountingRules.LedgerLevel)
        {
            account.LedgerType = Text.Clean(request.LedgerType) ?? LedgerType.General;
            account.PartyType = Text.Clean(request.PartyType);
            if (!LedgerType.All.Contains(account.LedgerType))
                throw new BusinessRuleException("The ledger type must be General, Cash or Bank.");
            if (account.PartyType is not null && !PartyType.All.Contains(account.PartyType))
                throw new BusinessRuleException("The subsidiary ledger must be of suppliers or customers.");
            if (account.PartyType is not null && account.LedgerType != LedgerType.General)
                throw new BusinessRuleException("A Cash or Bank head can't keep a supplier or customer ledger.");
            if (existing is { HasEntries: true } && existing.PartyType != account.PartyType)
                throw new BusinessRuleException("Vouchers are posted to this head, so its supplier or customer ledger can't change.");
        }

        if (account.Code.Length > 0 &&
            await repository.CodeExistsAsync(organizationId, account.Code, existing?.Id, cancellationToken))
            throw new BusinessConflictException("An account with this code already exists.");
        return account;
    }
}

/// <summary>A chart for a trading business to start from; every account can be renamed, moved or removed.</summary>
internal static class StandardChart
{
    public static readonly IReadOnlyList<StandardChartAccount> Accounts = Build();

    private static List<StandardChartAccount> Build()
    {
        var accounts = new List<StandardChartAccount>();

        void Add(string code, string name, string nature, string? ledgerType = null, string? partyType = null)
        {
            // Codes follow the levels: 1, 11, 1101, 110101, 110101001.
            var level = code.Length switch { 1 => 1, 2 => 2, 4 => 3, 6 => 4, _ => 5 };
            var parent = level switch { 1 => null, 2 => code[..1], 3 => code[..2], 4 => code[..4], _ => code[..6] };
            accounts.Add(new StandardChartAccount(level, code, parent, name, nature, ledgerType, partyType));
        }

        const string asset = AccountNature.Asset, liability = AccountNature.Liability, equity = AccountNature.Equity,
            income = AccountNature.Income, expense = AccountNature.Expense;

        Add("1", "Assets", asset);
        Add("11", "Current Assets", asset);
        Add("1101", "Cash and Cash Equivalents", asset);
        Add("110101", "Cash in Hand", asset);
        Add("110101001", "Cash in Hand", asset, LedgerType.Cash);
        Add("110101002", "Petty Cash", asset, LedgerType.Cash);
        Add("110102", "Cash at Bank", asset);
        Add("110102001", "Bank Account", asset, LedgerType.Bank);
        Add("110103", "Mobile Financial Services", asset);
        Add("110103001", "bKash / Nagad Account", asset, LedgerType.Bank);
        Add("1102", "Trade Receivables", asset);
        Add("110201", "Accounts Receivable", asset);
        Add("110201001", "Accounts Receivable - Customers", asset, LedgerType.General, PartyType.Customer);
        Add("1103", "Inventories", asset);
        Add("110301", "Stock in Trade", asset);
        Add("110301001", "Inventory", asset);
        Add("1104", "Advances, Deposits and Prepayments", asset);
        Add("110401", "Advances", asset);
        Add("110401001", "Advance to Suppliers", asset, LedgerType.General, PartyType.Supplier);
        Add("110401002", "Advance to Staff", asset);
        Add("110402", "Deposits and Prepayments", asset);
        Add("110402001", "Security Deposits", asset);
        Add("110402002", "Advance Income Tax", asset);
        Add("12", "Non-Current Assets", asset);
        Add("1201", "Property, Plant and Equipment", asset);
        Add("120101", "Fixed Assets", asset);
        Add("120101001", "Furniture and Fixtures", asset);
        Add("120101002", "Office Equipment", asset);
        Add("120101003", "Computers and Accessories", asset);
        Add("120101004", "Vehicles", asset);
        Add("120102", "Accumulated Depreciation", asset);
        Add("120102001", "Accumulated Depreciation", asset);

        Add("2", "Liabilities", liability);
        Add("21", "Current Liabilities", liability);
        Add("2101", "Trade Payables", liability);
        Add("210101", "Accounts Payable", liability);
        Add("210101001", "Accounts Payable - Suppliers", liability, LedgerType.General, PartyType.Supplier);
        Add("2102", "Accrued Expenses", liability);
        Add("210201", "Expenses Payable", liability);
        Add("210201001", "Salaries Payable", liability);
        Add("210201002", "Rent Payable", liability);
        Add("210201003", "Utilities Payable", liability);
        Add("2103", "Duties and Taxes", liability);
        Add("210301", "Taxes Payable", liability);
        Add("210301001", "VAT Payable", liability);
        Add("210301002", "Tax Deducted at Source", liability);
        Add("2104", "Advances Received", liability);
        Add("210401", "Customer Advances", liability);
        Add("210401001", "Advance from Customers", liability, LedgerType.General, PartyType.Customer);
        Add("22", "Non-Current Liabilities", liability);
        Add("2201", "Borrowings", liability);
        Add("220101", "Loans", liability);
        Add("220101001", "Bank Loan", liability);

        Add("3", "Equity", equity);
        Add("31", "Owners' Equity", equity);
        Add("3101", "Capital", equity);
        Add("310101", "Capital Account", equity);
        Add("310101001", "Owner's Capital", equity);
        Add("310101002", "Drawings", equity);
        Add("3102", "Reserves and Surplus", equity);
        Add("310201", "Retained Earnings", equity);
        Add("310201001", "Retained Earnings", equity);

        Add("4", "Income", income);
        Add("41", "Operating Income", income);
        Add("4101", "Sales", income);
        Add("410101", "Sales Revenue", income);
        Add("410101001", "Sales", income);
        Add("410101002", "Sales Returns", income);
        Add("410101003", "Discount Allowed", income);
        Add("42", "Other Income", income);
        Add("4201", "Non-Operating Income", income);
        Add("420101", "Other Income", income);
        Add("420101001", "Discount Received", income);
        Add("420101002", "Interest Income", income);
        Add("420101003", "Miscellaneous Income", income);

        Add("5", "Expenses", expense);
        Add("51", "Cost of Sales", expense);
        Add("5101", "Cost of Goods Sold", expense);
        Add("510101", "Purchases", expense);
        Add("510101001", "Purchases", expense);
        Add("510101002", "Purchase Returns", expense);
        Add("510101003", "Carriage Inward", expense);
        Add("52", "Operating Expenses", expense);
        Add("5201", "Administrative Expenses", expense);
        Add("520101", "Staff Costs", expense);
        Add("520101001", "Salaries and Wages", expense);
        Add("520101002", "Staff Welfare", expense);
        Add("520102", "Office Expenses", expense);
        Add("520102001", "Office Rent", expense);
        Add("520102002", "Electricity and Utilities", expense);
        Add("520102003", "Telephone and Internet", expense);
        Add("520102004", "Printing and Stationery", expense);
        Add("520102005", "Repairs and Maintenance", expense);
        Add("520102006", "Depreciation", expense);
        Add("5202", "Selling and Distribution Expenses", expense);
        Add("520201", "Selling Expenses", expense);
        Add("520201001", "Advertisement and Promotion", expense);
        Add("520201002", "Carriage Outward", expense);
        Add("520201003", "Sales Commission", expense);
        Add("53", "Financial Expenses", expense);
        Add("5301", "Finance Costs", expense);
        Add("530101", "Bank Charges and Interest", expense);
        Add("530101001", "Bank Charges", expense);
        Add("530101002", "Interest on Loan", expense);
        return accounts;
    }
}
