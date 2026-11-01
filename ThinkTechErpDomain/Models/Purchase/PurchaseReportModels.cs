namespace ThinkTechErpDomain.Models.Purchase;

public sealed class SupplierLedger
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>+ = owed to the supplier, - = advance; base currency.</summary>
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<SupplierLedgerEntry> Entries { get; set; } = [];
}

public sealed class SupplierLedgerEntry
{
    public DateOnly? EntryDate { get; set; }
    /// <summary>Opening, Invoice, PaidOnInvoice, Return, ReturnRefund, Payment or Refund.</summary>
    public string EntryType { get; set; } = string.Empty;
    public long? DocumentId { get; set; }
    public string? DocumentNo { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal Balance { get; set; }
}

public sealed class SupplierDueRow
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    /// <summary>Ledger balance: + = owed to the supplier.</summary>
    public decimal Balance { get; set; }
    public int OpenInvoiceCount { get; set; }
    /// <summary>Unpaid invoice amounts by days past their due date (base currency).</summary>
    public decimal NotDue { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Over90 { get; set; }
    public DateOnly? OldestDueDate { get; set; }
}

public sealed class PurchaseRegisterRow
{
    public long InvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string PurchaseType { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public string? TaxInvoiceNo { get; set; }
    public string Currency { get; set; } = string.Empty;
    /// <summary>Amounts in the base currency.</summary>
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DueAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public sealed class PurchaseByProductRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    /// <summary>Base units bought.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Net of discount, before tax, base currency.</summary>
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal AveragePrice { get; set; }
    public int InvoiceCount { get; set; }
    public DateOnly? LastPurchaseDate { get; set; }
}
