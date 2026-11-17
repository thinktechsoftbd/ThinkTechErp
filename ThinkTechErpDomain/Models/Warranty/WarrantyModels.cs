using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Warranty;

public static class WarrantyRules
{
    public const string ViewWarrantyPermission = "WARRANTY.V";
    public const string ManageWarrantyPermission = "WARRANTY.M";

    /// <summary>The lookup table of device kinds a job is opened for (Laptop, Desktop PC, CC Camera, ...).</summary>
    public const string DeviceTypeLookupTable = "Service Device Type";

    /// <summary>
    /// The last day of the warranty and of the replacement period for a sale on this date, e.g. 12 months from
    /// 15 Jan 2026 runs to 14 Jan 2027; 7 replacement days to 21 Jan 2026 (never past the warranty's end).
    /// </summary>
    public static (DateOnly End, DateOnly? ReplacementEnd) Period(DateOnly start, int months, int replacementDays)
    {
        var end = start.AddMonths(months).AddDays(-1);
        DateOnly? replacementEnd = replacementDays > 0 ? start.AddDays(replacementDays - 1) : null;
        if (replacementEnd > end)
            replacementEnd = end;
        return (end, replacementEnd);
    }
}

public static class WarrantyCardSource
{
    public const string Sale = "Sale";
    public const string Replacement = "Replacement";
    public const string Manual = "Manual";
}

public static class WarrantyCardStatus
{
    public const string Active = "Active";
    public const string Replaced = "Replaced";
    public const string Void = "Void";
}

/// <summary>What a card covers today (public.warranty_coverage).</summary>
public static class WarrantyCoverage
{
    public const string InReplacement = "InReplacement";
    public const string InWarranty = "InWarranty";
    public const string Expired = "Expired";
    public const string Replaced = "Replaced";
    public const string Void = "Void";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { InReplacement, InWarranty, Expired, Replaced, Void };
}

public static class ServiceJobType
{
    public const string Warranty = "Warranty";
    public const string Paid = "Paid";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Warranty, Paid };
}

public static class ServiceLocation
{
    public const string InShop = "InShop";
    public const string OnSite = "OnSite";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { InShop, OnSite };
}

public static class ServicePriority
{
    public const string Normal = "Normal";
    public const string Urgent = "Urgent";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Normal, Urgent };
}

public static class ServiceJobStatus
{
    public const string Received = "Received";
    public const string Diagnosing = "Diagnosing";
    public const string AwaitingApproval = "AwaitingApproval";
    public const string InRepair = "InRepair";
    public const string WaitingForParts = "WaitingForParts";
    public const string AtSupplier = "AtSupplier";
    public const string Ready = "Ready";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";

    /// <summary>For list filters: every status but Delivered and Cancelled.</summary>
    public const string OpenFilter = "Open";

    /// <summary>The device is still with the shop and the job can be worked.</summary>
    public static readonly IReadOnlySet<string> Open = new HashSet<string>(StringComparer.Ordinal)
        { Received, Diagnosing, AwaitingApproval, InRepair, WaitingForParts, AtSupplier, Ready };

    public static readonly IReadOnlySet<string> All = new HashSet<string>(Open, StringComparer.Ordinal) { Delivered, Cancelled };
}

public static class ServiceResolution
{
    public const string Repaired = "Repaired";
    public const string Replaced = "Replaced";
    public const string NoFaultFound = "NoFaultFound";
    public const string NotRepairable = "NotRepairable";
    public const string CustomerDeclined = "CustomerDeclined";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Repaired, Replaced, NoFaultFound, NotRepairable, CustomerDeclined };
}

public static class ServiceApprovalStatus
{
    public const string NotRequired = "NotRequired";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class SupplierClaimStatus
{
    public const string Sent = "Sent";
    public const string Returned = "Returned";
    public const string Cancelled = "Cancelled";
}

public static class SupplierClaimOutcome
{
    public const string Repaired = "Repaired";
    public const string Replaced = "Replaced";
    public const string Rejected = "Rejected";
    public const string Credited = "Credited";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Repaired, Replaced, Rejected, Credited };
}

// =====================================================================
// Warranty policies and the products sold with them
// =====================================================================

public sealed class WarrantyPolicy
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Total warranty: faults are repaired free of charge for this long after the sale.</summary>
    public int WarrantyMonths { get; set; }
    /// <summary>The first part of it in which a faulty unit is replaced (0: no replacement).</summary>
    public int ReplacementDays { get; set; }
    public bool CoversParts { get; set; }
    public bool CoversLabor { get; set; }
    public string? Terms { get; set; }
    public bool IsActive { get; set; }
    /// <summary>How many products are sold with this policy.</summary>
    public int ProductCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class WarrantyPolicyUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 600)] public int WarrantyMonths { get; set; } = 12;
    [Range(0, 18_600)] public int ReplacementDays { get; set; }
    public bool CoversParts { get; set; } = true;
    public bool CoversLabor { get; set; } = true;
    [StringLength(2000)] public string? Terms { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class WarrantyPolicyOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int WarrantyMonths { get; set; }
    public int ReplacementDays { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>A product with the warranty it is sold with.</summary>
public sealed class ProductWarrantyItem
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductType { get; set; } = "Stock";
    public string TrackingType { get; set; } = "None";
    public string? CategoryName { get; set; }
    public string? BrandName { get; set; }
    public int? WarrantyPolicyId { get; set; }
    public string? WarrantyPolicyName { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ProductWarrantyQuery
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public int? WarrantyPolicyId { get; set; }
    /// <summary>true: only products with a policy; false: only those without.</summary>
    public bool? HasPolicy { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public sealed class ProductWarrantyAssignRequest
{
    [MinLength(1, ErrorMessage = "Choose at least one product."), MaxLength(500)]
    public List<int> ProductIds { get; set; } = [];

    /// <summary>Null takes the warranty off the products.</summary>
    [Range(1, int.MaxValue)] public int? WarrantyPolicyId { get; set; }
}

// =====================================================================
// Warranty cards
// =====================================================================

public sealed class WarrantyCard
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string WarrantyNo { get; set; } = string.Empty;
    public string Source { get; set; } = WarrantyCardSource.Sale;
    public long? SalesInvoiceId { get; set; }
    public string? InvoiceNo { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public long? SalesInvoiceLineId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public long? SerialNumberId { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public int? WarrantyPolicyId { get; set; }
    public string? WarrantyPolicyName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? ReplacementEndDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool CoversParts { get; set; }
    public bool CoversLabor { get; set; }
    public string? Terms { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public DateOnly? SupplierWarrantyEndDate { get; set; }
    public long? ReplacesRegistrationId { get; set; }
    public string? ReplacesWarrantyNo { get; set; }
    /// <summary>The card of the unit that replaced this one, when Replaced.</summary>
    public long? ReplacedByRegistrationId { get; set; }
    public string? ReplacedByWarrantyNo { get; set; }
    public string Status { get; set; } = WarrantyCardStatus.Active;
    /// <summary>InReplacement, InWarranty, Expired, Replaced or Void (today, UTC).</summary>
    public string Coverage { get; set; } = WarrantyCoverage.InWarranty;
    public int DaysRemaining { get; set; }
    public string? VoidReason { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Service jobs opened under this card.</summary>
    public int JobCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class WarrantyCardQuery
{
    /// <summary>Warranty no., serial no., customer name or mobile, product code or name, invoice no.</summary>
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Coverage { get; set; }
    public string? Source { get; set; }
    public int? ProductId { get; set; }
    public int? CustomerId { get; set; }
    /// <summary>Start date (date of sale) from / to.</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

/// <summary>A card for a unit sold before the system went live (or otherwise without an invoice here).</summary>
public sealed class WarrantyCardCreateRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }

    /// <summary>The serial as printed on the unit; matched to the product's serial numbers when it is one of them.</summary>
    [StringLength(100)] public string? SerialNo { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999")] public decimal Quantity { get; set; } = 1;

    [Range(1, int.MaxValue)] public int? CustomerId { get; set; }
    [StringLength(250)] public string? CustomerName { get; set; }
    [StringLength(30)] public string? CustomerMobile { get; set; }

    /// <summary>Null = the product's policy. Without one, EndDate is required.</summary>
    [Range(1, int.MaxValue)] public int? WarrantyPolicyId { get; set; }

    /// <summary>The date of sale.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Null = worked out from the policy.</summary>
    public DateOnly? EndDate { get; set; }
    public DateOnly? ReplacementEndDate { get; set; }

    /// <summary>Null = as the policy says (or true without one).</summary>
    public bool? CoversParts { get; set; }
    public bool? CoversLabor { get; set; }
    [StringLength(2000)] public string? Terms { get; set; }

    [Range(1, int.MaxValue)] public int? SupplierId { get; set; }
    public DateOnly? SupplierWarrantyEndDate { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
}

public sealed class WarrantyCardVoidRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A serial-tracked unit found in the shop's records, with the sale it went out on.</summary>
public sealed class SerialSaleInfo
{
    public long SerialNumberId { get; set; }
    public string SerialNo { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    /// <summary>serial_number.status: InStock, Sold, ...</summary>
    public string Status { get; set; } = string.Empty;
    public long? SalesInvoiceId { get; set; }
    public string? InvoiceNo { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    /// <summary>True when an Active card covers the unit.</summary>
    public bool HasActiveCard { get; set; }
}

/// <summary>What the counter finds for a serial no., warranty no., invoice no. or mobile number.</summary>
public sealed class WarrantyCheckResult
{
    public string Query { get; set; } = string.Empty;
    public List<WarrantyCard> Cards { get; set; } = [];
    /// <summary>Units with this serial in the shop's stock records (sold or not), card or no card.</summary>
    public List<SerialSaleInfo> Serials { get; set; } = [];
    /// <summary>Earlier service jobs for the same serial, card or mobile, newest first.</summary>
    public List<ServiceJob> Jobs { get; set; } = [];
}

// =====================================================================
// Service jobs
// =====================================================================

public sealed class ServiceJob
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string JobNo { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public string JobType { get; set; } = ServiceJobType.Paid;
    public string ServiceLocation { get; set; } = Warranty.ServiceLocation.InShop;
    public string Priority { get; set; } = ServicePriority.Normal;

    public int? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerMobile { get; set; }
    public string? ServiceAddress { get; set; }

    public long DeviceTypeLookupDataId { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public long? SerialNumberId { get; set; }
    public string? BrandName { get; set; }
    public string? ModelName { get; set; }
    public string? SerialNo { get; set; }
    public string? AccessoriesReceived { get; set; }
    public string? PhysicalCondition { get; set; }
    public string ProblemDescription { get; set; } = string.Empty;

    public long? WarrantyRegistrationId { get; set; }
    public string? WarrantyNo { get; set; }
    public string? WarrantyCoverage { get; set; }
    public DateOnly? WarrantyEndDate { get; set; }
    public string? WarrantyVoidReason { get; set; }

    public int? TechnicianUserId { get; set; }
    public string? TechnicianName { get; set; }
    public string? Diagnosis { get; set; }
    public decimal EstimatedAmount { get; set; }
    public string ApprovalStatus { get; set; } = ServiceApprovalStatus.NotRequired;
    public DateTime? ApprovedAt { get; set; }
    public DateOnly? PromisedDate { get; set; }

    public string Status { get; set; } = ServiceJobStatus.Received;
    public string? Resolution { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int? DeliveredBy { get; set; }
    public string? DeliveredToName { get; set; }
    public string? CancelReason { get; set; }

    public long? SalesInvoiceId { get; set; }
    public string? InvoiceNo { get; set; }
    public string? InvoiceStatus { get; set; }
    public decimal? InvoiceTotal { get; set; }
    public decimal? InvoiceDue { get; set; }

    /// <summary>What the customer pays for the items (warranty-covered lines are 0).</summary>
    public decimal ItemsTotal { get; set; }
    public int ItemCount { get; set; }
    /// <summary>Supplier claims for this job still out (Sent).</summary>
    public int OpenClaimCount { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<ServiceJobItem> Items { get; set; } = [];
    public List<ServiceJobStatusChange> History { get; set; } = [];
    public List<SupplierClaim> Claims { get; set; } = [];
}

public sealed class ServiceJobItem
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
    public decimal Quantity { get; set; }
    public long? BatchId { get; set; }
    public string? BatchNo { get; set; }
    public long? SerialNumberId { get; set; }
    public string? SerialNo { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public bool IsWarrantyCovered { get; set; }
    public long? SalesInvoiceLineId { get; set; }
    public string? Remarks { get; set; }
}

public sealed class ServiceJobStatusChange
{
    public long Id { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool CustomerNotified { get; set; }
    public DateTime ChangedAt { get; set; }
    public int? ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
}

public sealed class ServiceJobQuery
{
    /// <summary>Job no., customer name or mobile, serial no., brand, model, warranty no., invoice no.</summary>
    public string? Search { get; set; }
    /// <summary>A status, or Open for every job still with the shop.</summary>
    public string? Status { get; set; }
    public string? JobType { get; set; }
    public int? TechnicianUserId { get; set; }
    public int? CustomerId { get; set; }
    public long? DeviceTypeLookupDataId { get; set; }
    /// <summary>Received from / to (dates, UTC).</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

public sealed class ServiceJobUpsertRequest
{
    /// <summary>Null = now. Ignored on update.</summary>
    public DateTime? ReceivedAt { get; set; }

    [Required] public string JobType { get; set; } = ServiceJobType.Paid;
    public string ServiceLocation { get; set; } = Warranty.ServiceLocation.InShop;
    public string Priority { get; set; } = ServicePriority.Normal;

    /// <summary>A customer on file, or a walk-in by name and mobile. A warranty job defaults to the card's customer.</summary>
    [Range(1, int.MaxValue)] public int? CustomerId { get; set; }
    [StringLength(250)] public string? CustomerName { get; set; }
    [StringLength(30)] public string? CustomerMobile { get; set; }
    [StringLength(500)] public string? ServiceAddress { get; set; }

    [Range(1, long.MaxValue)] public long DeviceTypeLookupDataId { get; set; }
    /// <summary>The shop's product, when the device is one (a warranty job defaults to the card's).</summary>
    [Range(1, int.MaxValue)] public int? ProductId { get; set; }
    [StringLength(100)] public string? SerialNo { get; set; }
    [StringLength(100)] public string? BrandName { get; set; }
    [StringLength(150)] public string? ModelName { get; set; }
    [StringLength(500)] public string? AccessoriesReceived { get; set; }
    [StringLength(1000)] public string? PhysicalCondition { get; set; }

    [Required, StringLength(2000, MinimumLength = 1)]
    public string ProblemDescription { get; set; } = string.Empty;

    /// <summary>Required for a Warranty job. On a Paid job: the card whose warranty doesn't apply (say why below).</summary>
    [Range(1, long.MaxValue)] public long? WarrantyRegistrationId { get; set; }
    [StringLength(500)] public string? WarrantyVoidReason { get; set; }

    [Range(1, int.MaxValue)] public int? TechnicianUserId { get; set; }
    [StringLength(2000)] public string? Diagnosis { get; set; }
    [Range(0, 999_999_999_999)] public decimal EstimatedAmount { get; set; }
    public DateOnly? PromisedDate { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
}

public sealed class ServiceJobItemRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [StringLength(500)] public string? Description { get; set; }
    [Range(1, int.MaxValue)] public int UnitId { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999")] public decimal Quantity { get; set; } = 1;
    [Range(1, long.MaxValue)] public long? BatchId { get; set; }
    /// <summary>For a serial-tracked part: the unit fitted (quantity 1).</summary>
    [StringLength(100)] public string? SerialNo { get; set; }
    [Range(0, 999_999_999_999)] public decimal UnitPrice { get; set; }
    [Range(0, 999_999_999_999)] public decimal DiscountAmount { get; set; }
    /// <summary>Free under the warranty: price and discount must be 0.</summary>
    public bool IsWarrantyCovered { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}

public sealed class ServiceJobItemsRequest
{
    [MaxLength(200)] public List<ServiceJobItemRequest> Items { get; set; } = [];
}

public sealed class ServiceJobStatusRequest
{
    /// <summary>Any open status (Received ... Ready). Deliver and cancel have their own actions.</summary>
    [Required] public string Status { get; set; } = string.Empty;
    /// <summary>Required for Ready (unless already set).</summary>
    public string? Resolution { get; set; }
    [StringLength(2000)] public string? ResolutionNote { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public bool CustomerNotified { get; set; }
}

public sealed class ServiceJobApprovalRequest
{
    /// <summary>true: the customer accepts the estimate (the job goes to InRepair); false: declined.</summary>
    public bool Approved { get; set; }
    /// <summary>The estimate agreed, when it changed.</summary>
    [Range(0, 999_999_999_999)] public decimal? EstimatedAmount { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
}

public sealed class ServiceJobDeliverRequest
{
    /// <summary>Who collected the device (null = the customer).</summary>
    [StringLength(150)] public string? DeliveredToName { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public bool CustomerNotified { get; set; }
}

public sealed class ServiceJobCancelRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Bills a job: a sales invoice made from its items (warranty-covered lines at 0).</summary>
public sealed class ServiceJobInvoiceRequest
{
    /// <summary>Null = today (UTC).</summary>
    public DateOnly? InvoiceDate { get; set; }
    [Required] public string SaleType { get; set; } = "Cash";
    /// <summary>Where parts are taken from. Null = the default warehouse in Inventory Settings.</summary>
    [Range(1, int.MaxValue)] public int? WarehouseId { get; set; }
    [Range(0, 999_999_999_999)] public decimal PaidAmount { get; set; }
    public string? PaymentMethod { get; set; }
    [StringLength(100)] public string? PaymentProvider { get; set; }
    [StringLength(100)] public string? PaymentReferenceNo { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
    /// <summary>Post the invoice straight away (parts leave stock, warranty cards are issued).</summary>
    public bool Post { get; set; }
}

// =====================================================================
// Supplier claims (RMA)
// =====================================================================

public sealed class SupplierClaim
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ClaimNo { get; set; } = string.Empty;
    public long? ServiceJobId { get; set; }
    public string? JobNo { get; set; }
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long? SerialNumberId { get; set; }
    public string? SerialNo { get; set; }
    public string ProblemDescription { get; set; } = string.Empty;
    public DateOnly SentDate { get; set; }
    public string? SupplierReferenceNo { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    public string Status { get; set; } = SupplierClaimStatus.Sent;
    public DateOnly? ReturnedDate { get; set; }
    public string? Outcome { get; set; }
    public long? ReplacementSerialNumberId { get; set; }
    public string? ReplacementSerialNo { get; set; }
    public decimal ServiceChargeAmount { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Days since it was sent (to the return date once back).</summary>
    public int DaysOut { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class SupplierClaimQuery
{
    /// <summary>Claim no., supplier, product, serial no., supplier's reference, job no.</summary>
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? SupplierId { get; set; }
    public long? ServiceJobId { get; set; }
    /// <summary>Sent date from / to.</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}

public sealed class SupplierClaimUpsertRequest
{
    /// <summary>The customer's job the unit came from; null for stock that failed before it was sold.</summary>
    [Range(1, long.MaxValue)] public long? ServiceJobId { get; set; }
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    /// <summary>Null = the job's product.</summary>
    [Range(1, int.MaxValue)] public int? ProductId { get; set; }
    /// <summary>Null = the job's serial.</summary>
    [StringLength(100)] public string? SerialNo { get; set; }
    [Required, StringLength(2000, MinimumLength = 1)]
    public string ProblemDescription { get; set; } = string.Empty;
    /// <summary>Null = today (UTC).</summary>
    public DateOnly? SentDate { get; set; }
    [StringLength(100)] public string? SupplierReferenceNo { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
}

public sealed class SupplierClaimReturnRequest
{
    /// <summary>Null = today (UTC).</summary>
    public DateOnly? ReturnedDate { get; set; }
    [Required] public string Outcome { get; set; } = string.Empty;
    /// <summary>For Replaced: the serial of the unit the supplier sent back.</summary>
    [StringLength(100)] public string? ReplacementSerialNo { get; set; }
    [Range(0, 999_999_999_999)] public decimal ServiceChargeAmount { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
}
