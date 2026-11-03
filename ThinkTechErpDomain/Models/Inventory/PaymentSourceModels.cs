using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Inventory;

// Payment sources (public.inv_payment_source_h, with its lines in public.inv_payment_source_d): where money is
// paid from or received into (Cash, a bank, bKash, ...), each with the ledger head (a sub class of the chart)
// to post to for every kind of payment. Shared by every organization, like the chart of accounts.

public sealed class PaymentSource
{
    public int Id { get; set; }
    public int ObjectId { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public IReadOnlyList<PaymentSourceLine> Lines { get; set; } = [];
}

/// <summary>What a payment is for, and the ledger head it posts to.</summary>
public sealed class PaymentSourceLine
{
    public int Id { get; set; }
    public string PaymentFor { get; set; } = string.Empty;
    public int AccSubClassId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    /// <summary>Map class/main class/account class/parent class, as the ledger head pickers show it.</summary>
    public string? AccountPath { get; set; }
}

public sealed class PaymentSourceUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(150)] public string? NameLocal { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(200)]
    public List<PaymentSourceLineRequest> Lines { get; set; } = [];
}

public sealed class PaymentSourceLineRequest
{
    /// <summary>An existing line of this source to keep (and update); empty adds a new line.</summary>
    [Range(1, int.MaxValue)] public int? Id { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string PaymentFor { get; set; } = string.Empty;

    [Range(1, int.MaxValue)] public int AccSubClassId { get; set; }
}
