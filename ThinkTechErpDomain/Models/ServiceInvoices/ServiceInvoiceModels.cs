using System.ComponentModel.DataAnnotations;
using ThinkTechErpDomain.Models.Sales;

namespace ThinkTechErpDomain.Models.ServiceInvoices;

public static class ServiceInvoiceRules
{
    public const string ViewServiceInvoicePermission = "SERVICE_INVOICE.V";
    public const string ManageServiceInvoicePermission = "SERVICE_INVOICE.M";
}

/// <summary>A part used on the job, as the user enters it. Quantity and price are in UnitId.</summary>
public sealed class ServiceInvoiceLineRequest
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

    /// <summary>Batch-tracked products: the lot the part came from (optional).</summary>
    [Range(1, long.MaxValue)]
    public long? BatchId { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }
}

public sealed class ServiceInvoiceLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductType { get; set; } = "Stock";
    public string TrackingType { get; set; } = "None";
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
    public string? Remarks { get; set; }
}

public sealed class ServiceInvoiceQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? SaleType { get; set; }
    public string? PaymentStatus { get; set; }
    /// <summary>number, date, customer, type, service, total, due, payment or status; others: newest first.</summary>
    public string? Sort { get; set; }
    public bool Descending { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

/// <summary>
/// A bill for service work: the parts used (lines), the service charge and a note of what was done. Cash or credit;
/// a cash bill may name a walk-in customer.
/// </summary>
public sealed class ServiceInvoice
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string SaleType { get; set; } = SaleTypes.Cash;
    public DateOnly? DueDate { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    /// <summary>The customer's name, or the walk-in name on a cash bill.</summary>
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
    /// <summary>A discount on the whole bill, taken off the total after tax.</summary>
    public decimal ExtraDiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    /// <summary>Other charges (delivery, pick-up and the like).</summary>
    public decimal ChargeAmount { get; set; }
    /// <summary>The service / labour charge, part of the total.</summary>
    public decimal ServiceAmount { get; set; }
    public decimal RoundOffAmount { get; set; }
    /// <summary>Lines + charge + service amount - bill discount + round off.</summary>
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
    /// <summary>What was serviced or repaired.</summary>
    public string? ServiceNote { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<ServiceInvoiceLine> Lines { get; set; } = [];
}

public sealed class ServiceInvoiceUpsertRequest
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

    [Range(0, 999_999_999_999)] public decimal ServiceAmount { get; set; }
    [Range(0, 999_999_999_999)] public decimal ExtraDiscountAmount { get; set; }
    [Range(0, 999_999_999_999)] public decimal ChargeAmount { get; set; }
    [Range(-999_999, 999_999)] public decimal RoundOffAmount { get; set; }

    /// <summary>Paid now. Ignored for Cash, which is always paid in full.</summary>
    [Range(0, 999_999_999_999)] public decimal PaidAmount { get; set; }

    public string? PaymentMethod { get; set; }
    [StringLength(100)] public string? PaymentProvider { get; set; }
    [StringLength(100)] public string? PaymentReferenceNo { get; set; }
    [StringLength(2000)] public string? ServiceNote { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }

    /// <summary>The parts used; may be empty when the bill is for the service charge alone.</summary>
    public List<ServiceInvoiceLineRequest> Lines { get; set; } = [];
}
