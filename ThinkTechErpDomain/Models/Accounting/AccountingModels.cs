using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Accounting;

public static class AccountingRules
{
    public const string ViewAccountingPermission = "ACCOUNTING.V";
    public const string ManageAccountingPermission = "ACCOUNTING.M";

    /// <summary>The sub class level: the ledger heads vouchers post to.</summary>
    public const int LedgerLevel = 5;

    private static readonly string[] LevelNames = ["Parent Class", "Account Class", "Main Class", "Map Class", "Sub Class"];

    public static string LevelName(int level) => level is >= 1 and <= LedgerLevel ? LevelNames[level - 1] : "Account";

    /// <summary>Digits a level adds to its parent's code when the code is left empty: 1, 11, 1101, 110101, 110101001.</summary>
    public static int CodeDigits(int level) => level switch
    {
        1 or 2 => 1,
        3 or 4 => 2,
        _ => 3
    };
}

/// <summary>What a parent class is; every account below it shares it.</summary>
public static class AccountNature
{
    public const string Asset = "Asset";
    public const string Liability = "Liability";
    public const string Equity = "Equity";
    public const string Income = "Income";
    public const string Expense = "Expense";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Asset, Liability, Equity, Income, Expense };

    /// <summary>Assets and expenses grow with debits; liabilities, equity and income with credits.</summary>
    public static bool IsDebit(string nature) => nature is Asset or Expense;

    /// <summary>Assets, liabilities and equity; income and expenses go to profit and loss.</summary>
    public static bool IsBalanceSheet(string nature) => nature is Asset or Liability or Equity;
}

/// <summary>What a sub class (ledger head) is used for.</summary>
public static class LedgerType
{
    public const string General = "General";
    /// <summary>Cash in hand: bills are paid from and collected into Cash and Bank heads.</summary>
    public const string Cash = "Cash";
    public const string Bank = "Bank";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { General, Cash, Bank };
}

/// <summary>The subsidiary ledger a ledger head keeps (Accounts Payable keeps suppliers).</summary>
public static class PartyType
{
    public const string Supplier = "Supplier";
    public const string Customer = "Customer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Supplier, Customer };
}

public static class VoucherType
{
    public const string Journal = "Journal";
    public const string Payment = "Payment";
    public const string Receipt = "Receipt";
    public const string Contra = "Contra";
    /// <summary>Supplier bill payment: debits the supplier's payable head, credits cash or bank.</summary>
    public const string BillPayment = "BillPayment";
    /// <summary>Customer bill collection: debits cash or bank, credits the customer's receivable head.</summary>
    public const string BillCollection = "BillCollection";

    /// <summary>The types entered on the voucher screen; bill payments and collections have their own.</summary>
    public static readonly IReadOnlySet<string> General = new HashSet<string>(StringComparer.Ordinal)
        { Journal, Payment, Receipt, Contra };

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Journal, Payment, Receipt, Contra, BillPayment, BillCollection };

    /// <summary>The public.document_sequence series the voucher number comes from.</summary>
    public static string NumberSeries(string voucherType) => voucherType switch
    {
        Payment => Common.DocumentType.PaymentVoucher,
        Receipt => Common.DocumentType.ReceiptVoucher,
        Contra => Common.DocumentType.ContraVoucher,
        BillPayment => Common.DocumentType.BillPayment,
        BillCollection => Common.DocumentType.BillCollection,
        _ => Common.DocumentType.JournalVoucher
    };
}

// ---- Chart of accounts

public sealed class ChartAccount
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    /// <summary>1 Parent Class, 2 Account Class, 3 Main Class, 4 Map Class, 5 Sub Class (ledger head).</summary>
    public int Level { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public string? LedgerType { get; set; }
    public string? PartyType { get; set; }
    public int SortOrder { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int ChildCount { get; set; }
    /// <summary>Voucher lines (draft or posted) use the head, so it can't be deleted.</summary>
    public bool HasEntries { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class ChartAccountUpsertRequest
{
    /// <summary>Empty for a parent class; otherwise the account one level up.</summary>
    [Range(1, int.MaxValue)] public int? ParentId { get; set; }

    /// <summary>Empty takes the parent's code plus the next number (1101 → 110101).</summary>
    [StringLength(30)] public string? Code { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent classes only: Asset, Liability, Equity, Income or Expense.</summary>
    [StringLength(20)] public string? Nature { get; set; }

    /// <summary>Sub classes only: General (the default), Cash or Bank.</summary>
    [StringLength(20)] public string? LedgerType { get; set; }

    /// <summary>Sub classes only: Supplier or Customer to keep a subsidiary ledger of them.</summary>
    [StringLength(20)] public string? PartyType { get; set; }

    [Range(0, 1_000_000)] public int SortOrder { get; set; }
    [StringLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Ticks or clears an account's checkbox in the tree: the account and everything below it.</summary>
public sealed class ChartAccountActiveRequest
{
    public bool IsActive { get; set; }
}

/// <summary>A sub class for the voucher and report pickers.</summary>
public sealed class LedgerHeadOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public string LedgerType { get; set; } = string.Empty;
    public string? PartyType { get; set; }
    /// <summary>Main class › map class, to tell heads with similar names apart.</summary>
    public string? Path { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>A supplier or customer for the subsidiary-ledger pickers.</summary>
public sealed class PartyOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public bool IsActive { get; set; }
}

// ---- Vouchers

public sealed class Voucher
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string VoucherNo { get; set; } = string.Empty;
    public string VoucherType { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ReferenceNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public string? Narration { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Empty in lists.</summary>
    public IReadOnlyList<VoucherLine> Lines { get; set; } = [];
}

public sealed class VoucherLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? PartyType { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? LineNarration { get; set; }
}

public sealed class VoucherUpsertRequest
{
    /// <summary>Journal, Payment, Receipt or Contra.</summary>
    [Required, StringLength(20)]
    public string VoucherType { get; set; } = string.Empty;

    public DateOnly VoucherDate { get; set; }
    [StringLength(100)] public string? ReferenceNo { get; set; }
    [StringLength(500)] public string? Narration { get; set; }

    [Required, MinLength(2), MaxLength(500)]
    public List<VoucherLineRequest> Lines { get; set; } = [];
}

public sealed class VoucherLineRequest
{
    [Range(1, int.MaxValue)] public int AccountId { get; set; }
    [Range(1, int.MaxValue)] public int? SupplierId { get; set; }
    [Range(1, int.MaxValue)] public int? CustomerId { get; set; }
    [Range(typeof(decimal), "0", "99999999999999")] public decimal DebitAmount { get; set; }
    [Range(typeof(decimal), "0", "99999999999999")] public decimal CreditAmount { get; set; }
    [StringLength(300)] public string? LineNarration { get; set; }
}

/// <summary>A supplier bill payment or a customer bill collection, posted as a two-line voucher.</summary>
public sealed class BillVoucherRequest
{
    public DateOnly VoucherDate { get; set; }

    /// <summary>The supplier paid, or the customer collected from.</summary>
    [Range(1, int.MaxValue)] public int PartyId { get; set; }

    /// <summary>The supplier's payable head, or the customer's receivable head (a Supplier/Customer sub class).</summary>
    [Range(1, int.MaxValue)] public int PartyAccountId { get; set; }

    /// <summary>The Cash or Bank head the money is paid from or deposited to.</summary>
    [Range(1, int.MaxValue)] public int CashAccountId { get; set; }

    [Range(typeof(decimal), "0.0001", "99999999999999")] public decimal Amount { get; set; }

    [Required, StringLength(20)]
    public string PaymentMethod { get; set; } = string.Empty;

    [StringLength(100)] public string? ReferenceNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    [StringLength(500)] public string? Narration { get; set; }
}

public sealed class VoucherQuery
{
    public string? Search { get; set; }
    public string? VoucherType { get; set; }
    public string? Status { get; set; }
    public int? SupplierId { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

// ---- Reports

public sealed class SubsidiaryLedger
{
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public string? PartyType { get; set; }
    public int? SupplierId { get; set; }
    public int? CustomerId { get; set; }
    public string? PartyCode { get; set; }
    public string? PartyName { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Balances are debit minus credit: + is a debit balance, - a credit balance.</summary>
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<LedgerEntryRow> Entries { get; set; } = [];
}

public sealed class LedgerEntryRow
{
    public long VoucherId { get; set; }
    public string VoucherNo { get; set; } = string.Empty;
    public string VoucherType { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Narration { get; set; }
    public string? PartyName { get; set; }
    /// <summary>The heads on the other side of the voucher.</summary>
    public string? Particulars { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    /// <summary>Running balance, debit minus credit.</summary>
    public decimal Balance { get; set; }
}

/// <summary>A ledger head with its place in the chart and its balances (debit minus credit).</summary>
public sealed class AccountBalanceRow
{
    public int AccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public int ParentClassId { get; set; }
    public string ParentClassCode { get; set; } = string.Empty;
    public string ParentClassName { get; set; } = string.Empty;
    public int AccountClassId { get; set; }
    public string AccountClassCode { get; set; } = string.Empty;
    public string AccountClassName { get; set; } = string.Empty;
    public int MainClassId { get; set; }
    public string MainClassCode { get; set; } = string.Empty;
    public string MainClassName { get; set; } = string.Empty;
    public int MapClassId { get; set; }
    public string MapClassCode { get; set; } = string.Empty;
    public string MapClassName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal ClosingBalance { get; set; }
}

public sealed class TrialBalance
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public IReadOnlyList<AccountBalanceRow> Rows { get; set; } = [];
    public decimal OpeningDebit { get; set; }
    public decimal OpeningCredit { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal ClosingDebit { get; set; }
    public decimal ClosingCredit { get; set; }
}

public sealed class BalanceSheet
{
    public DateOnly AsOf { get; set; }
    /// <summary>Asset, liability and equity heads with a balance on the date.</summary>
    public IReadOnlyList<AccountBalanceRow> Rows { get; set; } = [];
    /// <summary>Income less expenses up to the date, shown under equity.</summary>
    public decimal NetProfit { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    /// <summary>Equity heads plus the net profit.</summary>
    public decimal TotalEquity { get; set; }
}

public sealed class PartyBalance
{
    public int AccountId { get; set; }
    /// <summary>Debit minus credit on the head for the supplier or customer.</summary>
    public decimal Balance { get; set; }
}
