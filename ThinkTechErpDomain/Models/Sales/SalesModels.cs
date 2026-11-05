using System.ComponentModel.DataAnnotations;
using ThinkTechErpDomain.Models.Purchase;

namespace ThinkTechErpDomain.Models.Sales;

public static class SalesRules
{
    public const string ViewSalesPermission = "SALES.V";
    public const string ManageSalesPermission = "SALES.M";

    public const string CustomerGroupLookupTable = "Customer Group";
}

public static class SaleTypes
{
    public const string Cash = "Cash";
    public const string Credit = "Credit";
}

// ---- Customers

public sealed class Customer
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public long? CustomerGroupLookupDataId { get; set; }
    public string? CustomerGroup { get; set; }
    public string? ContactPerson { get; set; }
    public string? Mobile { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public string? NationalIdNo { get; set; }
    public string? TaxRegistrationNo { get; set; }
    public int PaymentTermsDays { get; set; }
    /// <summary>The most the customer may owe at once (base currency). Null = no limit, 0 = cash only.</summary>
    public decimal? CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningBalanceDate { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Also a supplier: the same party is listed on the purchase screens.</summary>
    public bool IsSupplier { get; set; }
    /// <summary>Owed by the customer now (+), or paid in advance by them (-), in the base currency.</summary>
    public decimal Balance { get; set; }
    /// <summary>The party's ledger head (supplier.acc_sub_class_id, shared with the supplier side): a credit sale's receivable.</summary>
    public int? AccSubClassId { get; set; }
    public string? LedgerHeadCode { get; set; }
    public string? LedgerHeadName { get; set; }
    public string? LedgerHeadPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CustomerUpsertRequest
{
    /// <summary>Empty: the next code in the series (C-00001, ...).</summary>
    [StringLength(30)]
    public string? Code { get; set; }

    [Required, StringLength(250, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)] public string? NameLocal { get; set; }
    [Range(1, long.MaxValue)] public long? CustomerGroupLookupDataId { get; set; }
    [StringLength(150)] public string? ContactPerson { get; set; }
    [StringLength(30)] public string? Mobile { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(150), EmailAddress] public string? Email { get; set; }
    [StringLength(250)] public string? AddressLine1 { get; set; }
    [StringLength(250)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? State { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(2, MinimumLength = 2)] public string? CountryCode { get; set; }
    [StringLength(30)] public string? NationalIdNo { get; set; }
    [StringLength(50)] public string? TaxRegistrationNo { get; set; }
    [Range(0, 3650)] public int PaymentTermsDays { get; set; }
    [Range(0, 999_999_999_999)] public decimal? CreditLimit { get; set; }
    [Range(-999_999_999_999, 999_999_999_999)] public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningBalanceDate { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Also a supplier; their purchase terms are set on the purchase Suppliers screen.</summary>
    public bool IsSupplier { get; set; }
    /// <summary>The ledger head (sub class) credit sales are debited to; shared with the supplier side.</summary>
    [Range(1, int.MaxValue)] public int? AccSubClassId { get; set; }
}

/// <summary>A customer as the sales screen picks it, with what they owe and the credit left.</summary>
public sealed class CustomerOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public int PaymentTermsDays { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal Balance { get; set; }
    /// <summary>Credit limit minus balance; null when there is no limit.</summary>
    public decimal? AvailableCredit { get; set; }
    public bool IsActive { get; set; }
    /// <summary>False for a supplier listed with includeSuppliers: selling to them makes them a customer too.</summary>
    public bool IsCustomer { get; set; }
}

// ---- Sales invoice

/// <summary>A line as the user enters it. Quantity and price are in UnitId.</summary>
public sealed class SalesLineRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int UnitId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999", ErrorMessage = "Quantity must be more than 0.")]
    public decimal Quantity { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal UnitPrice { get; set; }

    [Range(0, 100)]
    public decimal DiscountPercent { get; set; }

    /// <summary>A fixed discount for the line; when given it wins over DiscountPercent.</summary>
    [Range(0, 999_999_999_999)]
    public decimal? DiscountAmount { get; set; }

    [Range(1, int.MaxValue)]
    public int? TaxGroupId { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Batch-tracked products: the lot sold from. Empty: the first to expire with enough stock.</summary>
    [Range(1, long.MaxValue)]
    public long? BatchId { get; set; }

    /// <summary>Serial-tracked products: one serial number per item, in stock in the warehouse.</summary>
    public List<string> SerialNos { get; set; } = [];

    [StringLength(500)]
    public string? Remarks { get; set; }
}

public sealed class SalesLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductType { get; set; } = "Stock";
    public string TrackingType { get; set; } = "None";
    public bool HasExpiry { get; set; }
    public string? Description { get; set; }
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public long? BatchId { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? TaxGroupId { get; set; }
    public string? TaxGroupName { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>Average cost per base unit when posted (base currency): the cost of goods sold.</summary>
    public decimal UnitCost { get; set; }
    /// <summary>Brought back on posted sales returns, in UnitId.</summary>
    public decimal ReturnedQuantity { get; set; }
    public List<string> SerialNos { get; set; } = [];
    public string? Remarks { get; set; }
    /// <summary>Each tax on the line, for the sales VAT register.</summary>
    public List<LineTaxItem> Taxes { get; set; } = [];
}

public sealed class SalesDocumentQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? SaleType { get; set; }
    public string? PaymentStatus { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

public sealed class SalesInvoice
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string SaleType { get; set; } = SaleTypes.Cash;
    public DateOnly? DueDate { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    /// <summary>The customer's name, or the walk-in name on a cash sale.</summary>
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public string? CustomerAddress { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    /// <summary>Quantity x price of the lines, before discounts and tax.</summary>
    public decimal SubtotalAmount { get; set; }
    /// <summary>Line discounts plus the discount on the whole bill.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>A discount on the whole bill, taken off the total after tax ("less" on a memo).</summary>
    public decimal ExtraDiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    /// <summary>Delivery, labour and the like, added to the bill.</summary>
    public decimal ChargeAmount { get; set; }
    public decimal RoundOffAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReferenceNo { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DueAmount { get; set; }
    /// <summary>Cost of the goods sold (base currency), known once posted.</summary>
    public decimal CostAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public string PaymentStatus { get; set; } = "Unpaid";
    /// <summary>The Bill Voucher made by finalizing the invoice, while it is there; the invoice is locked meanwhile.</summary>
    public long? BillVoucherId { get; set; }
    public string? BillVoucherRefNo { get; set; }
    public string? BillVoucherStatus { get; set; }
    public int LineCount { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<SalesLine> Lines { get; set; } = [];
    public List<SalesAllocationItem> Allocations { get; set; } = [];
}

/// <summary>What settled an invoice after the sale: a customer receipt (or, later, a return).</summary>
public sealed class SalesAllocationItem
{
    public long Id { get; set; }
    public DateOnly AllocationDate { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Receipt or Return.</summary>
    public string SourceType { get; set; } = string.Empty;
    public long SourceId { get; set; }
    public string SourceNo { get; set; } = string.Empty;
}

public sealed class SalesInvoiceUpsertRequest
{
    public DateOnly InvoiceDate { get; set; }

    [Required]
    public string SaleType { get; set; } = SaleTypes.Cash;

    public DateOnly? DueDate { get; set; }

    /// <summary>Required for Credit; optional for Cash (a walk-in customer is named below).</summary>
    [Range(1, int.MaxValue)] public int? CustomerId { get; set; }
    [StringLength(250)] public string? CustomerName { get; set; }
    [StringLength(30)] public string? CustomerMobile { get; set; }

    /// <summary>Null = the default warehouse in Inventory Settings.</summary>
    [Range(1, int.MaxValue)] public int? WarehouseId { get; set; }

    /// <summary>Null = the organization's base currency.</summary>
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [Range(0, 999_999_999_999)] public decimal ExtraDiscountAmount { get; set; }
    [Range(0, 999_999_999_999)] public decimal ChargeAmount { get; set; }
    [Range(-999_999, 999_999)] public decimal RoundOffAmount { get; set; }

    /// <summary>Paid at the counter. Ignored for Cash, which is always paid in full.</summary>
    [Range(0, 999_999_999_999)] public decimal PaidAmount { get; set; }

    public string? PaymentMethod { get; set; }
    [StringLength(100)] public string? PaymentProvider { get; set; }
    [StringLength(100)] public string? PaymentReferenceNo { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<SalesLineRequest> Lines { get; set; } = [];
}

/// <summary>A posted invoice with something still due, for allocating a receipt.</summary>
public sealed class OpenSalesInvoice
{
    public long Id { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DueAmount { get; set; }
}

// ---- Sales return (goods the customer brings back, against the sale's memo)

public static class SalesReturnReasons
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { "Damaged", "Expired", "WrongItem", "NotNeeded", "QualityIssue", "Other" };
}

public static class SalesReturnSettlement
{
    /// <summary>Deducted from what the customer owes on the invoice; any rest stays as their advance.</summary>
    public const string AdjustDue = "AdjustDue";

    /// <summary>The money is given back at the counter.</summary>
    public const string Refund = "Refund";
}

public sealed class SalesReturnLineRequest
{
    /// <summary>The invoice line returned: its unit, price, discount, tax, batch and cost are taken from it.</summary>
    [Range(1, long.MaxValue)]
    public long SalesInvoiceLineId { get; set; }

    /// <summary>In the invoice line's unit.</summary>
    [Range(typeof(decimal), "0.0001", "999999999999", ErrorMessage = "Quantity must be more than 0.")]
    public decimal Quantity { get; set; }

    /// <summary>False for goods that can't be sold again (damaged): they don't go back into stock.</summary>
    public bool IsRestockable { get; set; } = true;

    /// <summary>Serial-tracked products: the numbers coming back (sold on that invoice line).</summary>
    public List<string> SerialNos { get; set; } = [];

    [StringLength(500)]
    public string? Remarks { get; set; }
}

public sealed class SalesReturnUpsertRequest
{
    public DateOnly ReturnDate { get; set; }

    /// <summary>The sale (memo) the goods were bought on.</summary>
    [Range(1, long.MaxValue)]
    public long SalesInvoiceId { get; set; }

    /// <summary>Where the goods go back to. Null = the warehouse they were sold from.</summary>
    [Range(1, int.MaxValue)] public int? WarehouseId { get; set; }

    [Required] public string Reason { get; set; } = "NotNeeded";
    [Required] public string SettlementType { get; set; } = SalesReturnSettlement.Refund;
    public string? RefundMethod { get; set; }
    [StringLength(100)] public string? RefundProvider { get; set; }
    [StringLength(100)] public string? RefundReferenceNo { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<SalesReturnLineRequest> Lines { get; set; } = [];
}

public sealed class SalesReturnLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public long? SalesInvoiceLineId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductType { get; set; } = "Stock";
    public string TrackingType { get; set; } = "None";
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public long? BatchId { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? TaxGroupId { get; set; }
    public string? TaxGroupName { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>Per base unit: the sale line's cost of goods, at which restockable goods come back.</summary>
    public decimal UnitCost { get; set; }
    public bool IsRestockable { get; set; } = true;
    public List<string> SerialNos { get; set; } = [];
    public string? Remarks { get; set; }
}

public sealed class SalesReturn
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ReturnNo { get; set; } = string.Empty;
    public DateOnly ReturnDate { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public long? SalesInvoiceId { get; set; }
    public string? SalesInvoiceNo { get; set; }
    public string Reason { get; set; } = "NotNeeded";
    public string SettlementType { get; set; } = SalesReturnSettlement.Refund;
    public string? RefundMethod { get; set; }
    public string? RefundProvider { get; set; }
    public string? RefundReferenceNo { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    public decimal SubtotalAmount { get; set; }
    /// <summary>Line discounts plus this return's share of the sale's bill discount.</summary>
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>Of the total, how much settled the invoice's due (AdjustDue).</summary>
    public decimal AdjustedAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public int LineCount { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<SalesReturnLine> Lines { get; set; } = [];
}

// ---- Customer receipt (money received after the sale)

public sealed class CustomerReceipt
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public DateOnly ReceiptDate { get; set; }
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerMobile { get; set; }
    /// <summary>Receipt (money in) or Refund (money given back, e.g. an unused advance).</summary>
    public string ReceiptType { get; set; } = "Receipt";
    public string PaymentMethod { get; set; } = "Cash";
    public string? PaymentProvider { get; set; }
    public string? ReferenceNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public string? ChequeStatus { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    public decimal Amount { get; set; }
    public decimal TaxDeductedAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal UnallocatedAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<ReceiptAllocationItem> Allocations { get; set; } = [];
}

public sealed class ReceiptAllocationItem
{
    public long SalesInvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal Amount { get; set; }
}

public sealed class CustomerReceiptUpsertRequest
{
    public DateOnly ReceiptDate { get; set; }
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    [Required] public string ReceiptType { get; set; } = "Receipt";
    [Required] public string PaymentMethod { get; set; } = "Cash";
    [StringLength(100)] public string? PaymentProvider { get; set; }
    [StringLength(100)] public string? ReferenceNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [Range(typeof(decimal), "0.0001", "999999999999", ErrorMessage = "Amount must be more than 0.")]
    public decimal Amount { get; set; }

    /// <summary>VAT or AIT the customer withheld for us (VDS/TDS); settles their dues like cash.</summary>
    [Range(0, 999_999_999_999)] public decimal TaxDeductedAmount { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    /// <summary>Invoices this receipt settles. Empty with AutoAllocate: oldest open invoices first.</summary>
    public List<ReceiptAllocationRequest> Allocations { get; set; } = [];

    public bool AutoAllocate { get; set; }
}

public sealed class ReceiptAllocationRequest
{
    [Range(1, long.MaxValue)]
    public long SalesInvoiceId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999")]
    public decimal Amount { get; set; }
}

// ---- Reports

public sealed class CustomerLedger
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>+ = owed by the customer, - = advance; base currency.</summary>
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<CustomerLedgerEntry> Entries { get; set; } = [];
}

public sealed class CustomerLedgerEntry
{
    public DateOnly? EntryDate { get; set; }
    /// <summary>Opening, Invoice, PaidOnInvoice, Return, ReturnRefund, Receipt or Refund.</summary>
    public string EntryType { get; set; } = string.Empty;
    public long? DocumentId { get; set; }
    public string? DocumentNo { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal Balance { get; set; }
}

public sealed class CustomerDueRow
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    /// <summary>Ledger balance: + = owed by the customer.</summary>
    public decimal Balance { get; set; }
    public decimal? CreditLimit { get; set; }
    public int OpenInvoiceCount { get; set; }
    /// <summary>Unpaid invoice amounts by days past their due date (base currency).</summary>
    public decimal NotDue { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Over90 { get; set; }
    public DateOnly? OldestDueDate { get; set; }
}

public sealed class SalesRegisterRow
{
    public long InvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string SaleType { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    /// <summary>Amounts in the base currency.</summary>
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DueAmount { get; set; }
    public decimal CostAmount { get; set; }
    /// <summary>Net sales (total less tax) minus the cost of goods sold.</summary>
    public decimal Profit { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public sealed class SalesByProductRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    /// <summary>Base units sold.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Net of discount and tax, base currency.</summary>
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal CostAmount { get; set; }
    public decimal Profit { get; set; }
    public decimal MarginPercent { get; set; }
    public decimal AveragePrice { get; set; }
    public int InvoiceCount { get; set; }
    public DateOnly? LastSaleDate { get; set; }
}
