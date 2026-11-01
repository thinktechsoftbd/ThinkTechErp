using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Purchase;

public static class PurchaseTypes
{
    public const string Cash = "Cash";
    public const string Credit = "Credit";
}

public static class PurchaseOrderStatus
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    public const string PartiallyReceived = "PartiallyReceived";
    public const string Received = "Received";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public static class PaymentStatus
{
    public const string Unpaid = "Unpaid";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
}

public static class PurchaseReturnReasons
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { "Damaged", "Expired", "WrongItem", "Excess", "QualityIssue", "Other" };
}

/// <summary>A line of a purchase document as the user enters it. Quantity and price are in UnitId.</summary>
public sealed class PurchaseLineRequest
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

    [Range(1, long.MaxValue)] public long? PurchaseOrderLineId { get; set; }
    [Range(1, long.MaxValue)] public long? GoodsReceiptLineId { get; set; }
    [Range(1, long.MaxValue)] public long? PurchaseInvoiceLineId { get; set; }

    /// <summary>Stock coming in: a new batch (created when posted).</summary>
    [StringLength(60)]
    public string? BatchNo { get; set; }

    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Stock going out (returns): the batch it leaves from.</summary>
    [Range(1, long.MaxValue)]
    public long? BatchId { get; set; }

    /// <summary>Goods receipts: quantity refused on arrival (not taken into stock).</summary>
    [Range(0, 999_999_999_999)]
    public decimal RejectedQuantity { get; set; }

    /// <summary>Serial-tracked products: one serial number per item.</summary>
    public List<string> SerialNos { get; set; } = [];

    [StringLength(500)]
    public string? Remarks { get; set; }
}

/// <summary>A cost of bringing the stock in (transport, labour, customs), spread over the lines' cost.</summary>
public sealed class PurchaseChargeRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string ChargeName { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.0001", "999999999999")]
    public decimal Amount { get; set; }

    /// <summary>Value or Quantity.</summary>
    public string AllocationMethod { get; set; } = "Value";

    /// <summary>The supplier billed it on this document (adds to what is owed to them).</summary>
    public bool OnSupplierBill { get; set; }

    /// <summary>Someone else billed it, e.g. a transporter set up as a supplier.</summary>
    [Range(1, int.MaxValue)]
    public int? PayeeSupplierId { get; set; }
}

public sealed class PurchaseLine
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
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? TaxGroupId { get; set; }
    public string? TaxGroupName { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public long? PurchaseOrderLineId { get; set; }
    public long? GoodsReceiptLineId { get; set; }
    public long? PurchaseInvoiceLineId { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public long? BatchId { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal LandedUnitCost { get; set; }
    public decimal RejectedQuantity { get; set; }
    /// <summary>Order lines: received so far; receipt lines: billed so far; and returned so far.</summary>
    public decimal ReceivedQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public List<string> SerialNos { get; set; } = [];
    public string? Remarks { get; set; }
    /// <summary>Each tax on the line (invoices), for the purchase VAT register.</summary>
    public List<LineTaxItem> Taxes { get; set; } = [];
}

public sealed class LineTaxItem
{
    public int TaxRateId { get; set; }
    public string TaxCode { get; set; } = string.Empty;
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    /// <summary>False for tax a VAT-registered business can't claim back: it is part of the cost.</summary>
    public bool IsRecoverable { get; set; } = true;
}

public sealed class PurchaseCharge
{
    public long Id { get; set; }
    public string ChargeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AllocationMethod { get; set; } = "Value";
    public int? PayeeSupplierId { get; set; }
    public string? PayeeName { get; set; }
    public bool OnSupplierBill { get; set; }
}

public sealed class PurchaseDocumentQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? SupplierId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? PurchaseType { get; set; }
    public string? PaymentStatus { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

// ---- Supplier (purchase) invoice

public sealed class PurchaseInvoice
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string PurchaseType { get; set; } = PurchaseTypes.Cash;
    public DateOnly? DueDate { get; set; }
    public int? SupplierId { get; set; }
    /// <summary>The supplier's name, or the walk-in name on a cash purchase.</summary>
    public string? SupplierName { get; set; }
    public string? SupplierMobile { get; set; }
    public bool ReceivesStock { get; set; } = true;
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public long? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNo { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public DateOnly? SupplierInvoiceDate { get; set; }
    public string? TaxInvoiceNo { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal RoundOffAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReferenceNo { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DueAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public string PaymentStatus { get; set; } = "Unpaid";
    public int LineCount { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
    public List<PurchaseCharge> Charges { get; set; } = [];
    public List<InvoiceAllocationItem> Allocations { get; set; } = [];
}

public sealed class InvoiceAllocationItem
{
    public long Id { get; set; }
    public DateOnly AllocationDate { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Payment or Return.</summary>
    public string SourceType { get; set; } = string.Empty;
    public long SourceId { get; set; }
    public string SourceNo { get; set; } = string.Empty;
}

public sealed class PurchaseInvoiceUpsertRequest
{
    public DateOnly InvoiceDate { get; set; }

    [Required]
    public string PurchaseType { get; set; } = PurchaseTypes.Cash;

    public DateOnly? DueDate { get; set; }

    [Range(1, int.MaxValue)] public int? SupplierId { get; set; }
    [StringLength(250)] public string? SupplierName { get; set; }
    [StringLength(30)] public string? SupplierMobile { get; set; }

    /// <summary>True: a direct purchase that receives the stock. False: bills earlier goods receipts.</summary>
    public bool ReceivesStock { get; set; } = true;

    [Range(1, int.MaxValue)] public int? WarehouseId { get; set; }
    [Range(1, long.MaxValue)] public long? PurchaseOrderId { get; set; }
    [StringLength(60)] public string? SupplierInvoiceNo { get; set; }
    public DateOnly? SupplierInvoiceDate { get; set; }
    [StringLength(60)] public string? TaxInvoiceNo { get; set; }

    /// <summary>Null = the organization's base currency.</summary>
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [Range(-999_999, 999_999)] public decimal RoundOffAmount { get; set; }

    /// <summary>Paid on the spot. Ignored for Cash, which is always paid in full.</summary>
    [Range(0, 999_999_999_999)] public decimal PaidAmount { get; set; }

    public string? PaymentMethod { get; set; }
    [StringLength(100)] public string? PaymentProvider { get; set; }
    [StringLength(100)] public string? PaymentReferenceNo { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<PurchaseLineRequest> Lines { get; set; } = [];

    public List<PurchaseChargeRequest> Charges { get; set; } = [];
}

// ---- Purchase order

public sealed class PurchaseOrder
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string? SupplierReference { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    public int PaymentTermsDays { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public int LineCount { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedBy { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
}

public sealed class PurchaseOrderUpsertRequest
{
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Range(1, int.MaxValue)] public int WarehouseId { get; set; }
    [StringLength(60)] public string? SupplierReference { get; set; }
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [Range(0, 3650)] public int PaymentTermsDays { get; set; }
    [Range(0, 999_999_999_999)] public decimal ChargeAmount { get; set; }
    [StringLength(4000)] public string? TermsAndConditions { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<PurchaseLineRequest> Lines { get; set; } = [];
}

// ---- Goods receipt

public sealed class GoodsReceipt
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public DateOnly ReceiptDate { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public long? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNo { get; set; }
    public string? SupplierChallanNo { get; set; }
    public DateOnly? SupplierChallanDate { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    /// <summary>Landed value of the stock received, base currency.</summary>
    public decimal TotalValue { get; set; }
    public string Status { get; set; } = "Draft";
    /// <summary>Every line has been billed on supplier invoices.</summary>
    public bool IsFullyInvoiced { get; set; }
    public int LineCount { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
    public List<PurchaseCharge> Charges { get; set; } = [];
}

public sealed class GoodsReceiptUpsertRequest
{
    public DateOnly ReceiptDate { get; set; }
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Range(1, int.MaxValue)] public int WarehouseId { get; set; }
    [Range(1, long.MaxValue)] public long? PurchaseOrderId { get; set; }
    [StringLength(60)] public string? SupplierChallanNo { get; set; }
    public DateOnly? SupplierChallanDate { get; set; }
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [StringLength(1000)] public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<PurchaseLineRequest> Lines { get; set; } = [];

    public List<PurchaseChargeRequest> Charges { get; set; } = [];
}

// ---- Purchase return

public sealed class PurchaseReturn
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ReturnNo { get; set; } = string.Empty;
    public DateOnly ReturnDate { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public long? GoodsReceiptId { get; set; }
    public string? GoodsReceiptNo { get; set; }
    public long? PurchaseInvoiceId { get; set; }
    public string? PurchaseInvoiceNo { get; set; }
    public string Reason { get; set; } = "Damaged";
    public string SettlementType { get; set; } = "AdjustDue";
    public string? RefundMethod { get; set; }
    public string? RefundProvider { get; set; }
    public string? RefundReferenceNo { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public int LineCount { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
}

public sealed class PurchaseReturnUpsertRequest
{
    public DateOnly ReturnDate { get; set; }
    [Range(1, int.MaxValue)] public int? SupplierId { get; set; }
    [StringLength(250)] public string? SupplierName { get; set; }
    [Range(1, int.MaxValue)] public int WarehouseId { get; set; }
    [Range(1, long.MaxValue)] public long? GoodsReceiptId { get; set; }
    [Range(1, long.MaxValue)] public long? PurchaseInvoiceId { get; set; }
    [Required] public string Reason { get; set; } = "Damaged";
    [Required] public string SettlementType { get; set; } = "AdjustDue";
    public string? RefundMethod { get; set; }
    [StringLength(100)] public string? RefundProvider { get; set; }
    [StringLength(100)] public string? RefundReferenceNo { get; set; }
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [StringLength(1000)] public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<PurchaseLineRequest> Lines { get; set; } = [];
}

// ---- Supplier payment

public sealed class SupplierPayment
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string PaymentNo { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string PaymentType { get; set; } = "Payment";
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
    public List<PaymentAllocationItem> Allocations { get; set; } = [];
}

public sealed class PaymentAllocationItem
{
    public long PurchaseInvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal Amount { get; set; }
}

public sealed class SupplierPaymentUpsertRequest
{
    public DateOnly PaymentDate { get; set; }
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Required] public string PaymentType { get; set; } = "Payment";
    [Required] public string PaymentMethod { get; set; } = "Cash";
    [StringLength(100)] public string? PaymentProvider { get; set; }
    [StringLength(100)] public string? ReferenceNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1;

    [Range(typeof(decimal), "0.0001", "999999999999", ErrorMessage = "Amount must be more than 0.")]
    public decimal Amount { get; set; }

    [Range(0, 999_999_999_999)] public decimal TaxDeductedAmount { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    /// <summary>Invoices this payment settles. Empty with AutoAllocate: oldest open invoices first.</summary>
    public List<PaymentAllocationRequest> Allocations { get; set; } = [];

    public bool AutoAllocate { get; set; }
}

public sealed class PaymentAllocationRequest
{
    [Range(1, long.MaxValue)]
    public long PurchaseInvoiceId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999")]
    public decimal Amount { get; set; }
}

public sealed class ChequeStatusRequest
{
    /// <summary>Pending, Cleared or Bounced. Bounced takes the payment back off the invoices.</summary>
    [Required]
    public string ChequeStatus { get; set; } = "Cleared";
}

/// <summary>A posted credit invoice with something still due, for allocating a payment.</summary>
public sealed class OpenPurchaseInvoice
{
    public long Id { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DueAmount { get; set; }
}
