namespace ThinkTechErpDomain.Models.Common;

/// <summary>A business rule the request breaks (answered with 400 and an { error } body).</summary>
public sealed class BusinessRuleException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The request clashes with existing data, e.g. a duplicate code (answered with 409).</summary>
public sealed class BusinessConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Statuses shared by the stock and purchase documents.</summary>
public static class DocumentStatus
{
    public const string Draft = "Draft";
    public const string Posted = "Posted";
    public const string Cancelled = "Cancelled";
}

/// <summary>Document types as stored in public.document_sequence and stock_movement.source_document_type.</summary>
public static class DocumentType
{
    public const string PurchaseOrder = "PurchaseOrder";
    public const string GoodsReceipt = "GoodsReceipt";
    public const string PurchaseInvoice = "PurchaseInvoice";
    public const string PurchaseReturn = "PurchaseReturn";
    public const string SupplierPayment = "SupplierPayment";
    public const string StockAdjustment = "StockAdjustment";
    public const string StockTransfer = "StockTransfer";
    public const string StockCount = "StockCount";
    public const string SalesInvoice = "SalesInvoice";
    public const string SalesReturn = "SalesReturn";
    public const string CustomerReceipt = "CustomerReceipt";
    /// <summary>Every accounting voucher, whatever its type: one series per organization.</summary>
    public const string Voucher = "Voucher";
    /// <summary>Not a document: the series new customer codes are taken from.</summary>
    public const string Customer = "Customer";
    /// <summary>Not a document: the series a new product's code is taken from when none is typed.</summary>
    public const string Product = "Product";

    /// <summary>The number prefix a new organization's series starts with, e.g. PINV-00001.</summary>
    public static string DefaultPrefix(string documentType) => documentType switch
    {
        PurchaseOrder => "PO-",
        GoodsReceipt => "GRN-",
        PurchaseInvoice => "PINV-",
        PurchaseReturn => "PRET-",
        SupplierPayment => "PAY-",
        StockAdjustment => "ADJ-",
        StockTransfer => "TRF-",
        StockCount => "CNT-",
        SalesInvoice => "INV-",
        SalesReturn => "SRET-",
        // Money receipt, as shops call it.
        CustomerReceipt => "MR-",
        // Voucher Ref. Numbers are digits only: 00000000001.
        Voucher => "",
        Customer => "C-",
        Product => "P-",
        _ => "DOC-"
    };

    /// <summary>How many digits a new organization's series is padded to.</summary>
    public static short DefaultPadding(string documentType) => documentType == Voucher ? (short)11 : (short)5;
}

/// <summary>How money moved at the counter or in a payment.</summary>
public static class PaymentMethod
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Cheque = "Cheque";
    public const string MobileWallet = "MobileWallet";
    public const string Card = "Card";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Cash, Bank, Cheque, MobileWallet, Card, Other };

    /// <summary>Paid on the spot with a document: everything but a cheque, which can bounce.</summary>
    public static readonly IReadOnlySet<string> OnTheSpot = new HashSet<string>(StringComparer.Ordinal)
        { Cash, Bank, MobileWallet, Card, Other };
}

/// <summary>A choice for a drop-down: id and display name.</summary>
public sealed class OptionItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
}
