namespace ThinkTechErpDomain.Models.Accounting;

// The ledger mappings a Bill Voucher is worked out from when an invoice is finalized.

/// <summary>A product's category and the category's ledger head (category.acc_sub_class_id).</summary>
public sealed class ProductLedgerHead
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? AccSubClassId { get; set; }
}

/// <summary>A supplier's or customer's ledger head (supplier.acc_sub_class_id).</summary>
public sealed class PartyLedgerHead
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? AccSubClassId { get; set; }
}

/// <summary>A payment source line (the "source type") and its ledger head.</summary>
public sealed class PaymentSourceLedgerHead
{
    public int Id { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string PaymentFor { get; set; } = string.Empty;
    public int AccSubClassId { get; set; }
}
