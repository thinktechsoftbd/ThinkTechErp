using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Accounting;

public static class AccountingRules
{
    public const string ViewAccountingPermission = "ACCOUNTING.V";
    public const string ManageAccountingPermission = "ACCOUNTING.M";

    /// <summary>The lookup list voucher lines take their currency from.</summary>
    public const string CurrencyTable = "Currency";

    /// <summary>The Currency lookup value a voucher line takes when it names none.</summary>
    public const string DefaultCurrency = "BDT";
}

/// <summary>What a parent class is (from its code: 1 Assets ... 5 Expenses); every account below it shares it.</summary>
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

/// <summary>The side of purchase.supplier a party picker lists: suppliers, customers, or both when not given.</summary>
public static class PartyType
{
    public const string Supplier = "Supplier";
    public const string Customer = "Customer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Supplier, Customer };
}

/// <summary>
/// Vouchers take their type and payment method from the "Voucher Type" and "Payment Method" lookup lists
/// (Settings → Lookup Data). The names below are the ones the rules and the bill screens rely on.
/// </summary>
public static class VoucherLookups
{
    public const string VoucherTypeTable = "Voucher Type";
    public const string PaymentMethodTable = "Payment Method";

    public const string CashPaymentVoucher = "Cash Payment Voucher";
    public const string BankPaymentVoucher = "Bank Payment Voucher";
    public const string CashReceiveVoucher = "Cash Receive Voucher";
    public const string BankReceiveVoucher = "Bank Receive Voucher";

    public const string Cheque = "Cheque";

    /// <summary>Supplier bill payments are entered as one of these.</summary>
    public static readonly IReadOnlyList<string> BillPaymentTypes = [CashPaymentVoucher, BankPaymentVoucher];

    /// <summary>Customer bill collections are entered as one of these.</summary>
    public static readonly IReadOnlyList<string> BillCollectionTypes = [CashReceiveVoucher, BankReceiveVoucher];

    /// <summary>A type named "Bank ..." (Bank Payment Voucher, Bank Receive Voucher, ...).</summary>
    public static bool IsBankVoucher(string? voucherType)
        => voucherType?.TrimStart().StartsWith("Bank", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsCheque(string? paymentMethod)
        => string.Equals(paymentMethod?.Trim(), Cheque, StringComparison.OrdinalIgnoreCase);
}

/// <summary>How a cheque is crossed: account payee only, or payable in cash.</summary>
public static class ChequePayType
{
    public const string AcPay = "AcPay";
    public const string Cash = "Cash";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { AcPay, Cash };
}

// ---- Lookups

/// <summary>A sub class (accounting.acc_sub_class) for the voucher and report pickers.</summary>
public sealed class LedgerHeadOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    /// <summary>Main class › map class, to tell heads with similar names apart.</summary>
    public string? Path { get; set; }
}

/// <summary>A supplier or customer (both live in purchase.supplier) for the voucher and report pickers.</summary>
public sealed class PartyOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public bool IsSupplier { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>A value of a lookup list (Currency, Voucher Type, Payment Method).</summary>
public sealed class LookupValueOption
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

// ---- Vouchers

public sealed class Voucher
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public int OrganizationId { get; set; }
    /// <summary>00000000001, 00000000002, ... per organization.</summary>
    public string VoucherRefNo { get; set; } = string.Empty;
    /// <summary>A "Voucher Type" lookup value.</summary>
    public long VoucherTypeId { get; set; }
    public string VoucherType { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    /// <summary>A "Payment Method" lookup value, if any.</summary>
    public long? PaymentMethodId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ChequeNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    /// <summary>AcPay or Cash, for a cheque.</summary>
    public string? PayType { get; set; }
    public string? Narration { get; set; }
    /// <summary>The suppliers and customers named on the lines.</summary>
    public string? PartyNames { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    /// <summary>Empty in lists.</summary>
    public IReadOnlyList<VoucherLine> Lines { get; set; } = [];
}

public sealed class VoucherLine
{
    /// <summary>0 for a line not saved yet.</summary>
    public long Id { get; set; }
    public int LineNo { get; set; }
    /// <summary>The ledger head: an accounting.acc_sub_class id (voucher_line.acc_sub_class_id).</summary>
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    /// <summary>The supplier or customer the line is for, if any (voucher_line.party_id).</summary>
    public int? PartyId { get; set; }
    public string? PartyCode { get; set; }
    public string? PartyName { get; set; }
    /// <summary>In BDT, whatever the line's currency.</summary>
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string? Currency { get; set; }
    public decimal LocalExchangeRate { get; set; }
    public string? LineNarration { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>The cheque details a bank voucher paid by cheque must carry.</summary>
public interface IChequeDetails
{
    long? PaymentMethodId { get; }
    string? ChequeNo { get; set; }
    DateOnly? ChequeDate { get; set; }
    string? PayType { get; set; }
}

public sealed class VoucherUpsertRequest : IChequeDetails
{
    /// <summary>A "Voucher Type" lookup value.</summary>
    [Range(1, long.MaxValue)] public long VoucherTypeId { get; set; }

    public DateOnly VoucherDate { get; set; }

    /// <summary>Optional: a "Payment Method" lookup value.</summary>
    [Range(1, long.MaxValue)] public long? PaymentMethodId { get; set; }

    /// <summary>Required (with ChequeDate and PayType) for a bank voucher paid by cheque.</summary>
    [StringLength(50)] public string? ChequeNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    /// <summary>AcPay or Cash.</summary>
    [StringLength(10)] public string? PayType { get; set; }

    [StringLength(500)] public string? Narration { get; set; }

    [Required, MinLength(2), MaxLength(500)]
    public List<VoucherLineRequest> Lines { get; set; } = [];
}

public sealed class VoucherLineRequest
{
    /// <summary>The saved line this is, when editing a draft; empty for a new line.</summary>
    [Range(1, long.MaxValue)] public long? Id { get; set; }
    /// <summary>A sub class (acc_sub_class id).</summary>
    [Range(1, int.MaxValue)] public int AccountId { get; set; }
    /// <summary>Optional: the supplier or customer the line is for.</summary>
    [Range(1, int.MaxValue)] public int? PartyId { get; set; }
    /// <summary>In BDT.</summary>
    [Range(typeof(decimal), "0", "99999999999999")] public decimal DebitAmount { get; set; }
    [Range(typeof(decimal), "0", "99999999999999")] public decimal CreditAmount { get; set; }
    /// <summary>A Currency lookup value; empty = BDT.</summary>
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }
    /// <summary>Empty = 1.</summary>
    [Range(typeof(decimal), "0.000001", "999999999999")] public decimal? LocalExchangeRate { get; set; }
    [StringLength(300)] public string? LineNarration { get; set; }
}

/// <summary>
/// A supplier bill payment (a Cash or Bank Payment Voucher) or a customer bill collection (a Cash or Bank
/// Receive Voucher), posted as a two-line voucher.
/// </summary>
public sealed class BillVoucherRequest : IChequeDetails
{
    /// <summary>Cash/Bank Payment Voucher for a payment, Cash/Bank Receive Voucher for a collection.</summary>
    [Range(1, long.MaxValue)] public long VoucherTypeId { get; set; }

    public DateOnly VoucherDate { get; set; }

    /// <summary>The supplier paid, or the customer collected from.</summary>
    [Range(1, int.MaxValue)] public int PartyId { get; set; }

    /// <summary>The sub class the party's balance is kept on (Accounts Payable, Accounts Receivable, ...).</summary>
    [Range(1, int.MaxValue)] public int PartyAccountId { get; set; }

    /// <summary>The cash or bank sub class the money is paid from or deposited to.</summary>
    [Range(1, int.MaxValue)] public int CashAccountId { get; set; }

    [Range(typeof(decimal), "0.0001", "99999999999999")] public decimal Amount { get; set; }

    /// <summary>Optional: a "Payment Method" lookup value.</summary>
    [Range(1, long.MaxValue)] public long? PaymentMethodId { get; set; }

    [StringLength(50)] public string? ChequeNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    [StringLength(10)] public string? PayType { get; set; }
    [StringLength(500)] public string? Narration { get; set; }
}

public sealed class VoucherQuery
{
    public string? Search { get; set; }
    public long? VoucherTypeId { get; set; }
    /// <summary>Set by the bill lists: only these types, and only vouchers with a party on a line.</summary>
    public IReadOnlyList<long>? BillTypeIds { get; set; }
    public string? Status { get; set; }
    /// <summary>A supplier or customer named on any line.</summary>
    public int? PartyId { get; set; }
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
    public int? PartyId { get; set; }
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
    public string VoucherRefNo { get; set; } = string.Empty;
    public string VoucherType { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string? ChequeNo { get; set; }
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
    public int PartyId { get; set; }
    /// <summary>Debit minus credit on the head for the supplier or customer.</summary>
    public decimal Balance { get; set; }
}
