using ThinkTechErpDomain.Models.ThinkCart;

namespace ThinkTechErpRepository.ThinkCart;

/// <summary>Carts (cart, cart_item) and the sizes they hold, priced now for one organization.</summary>
public interface IThinkCartCartRepository
{
    /// <summary>The cart with this object_id, whatever its status; null when there is none.</summary>
    Task<CartHeader?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default);

    /// <summary>The cart's lines with their quantity, priced now; sizes of other organizations are left out.</summary>
    Task<IReadOnlyList<VariantLine>> GetCartLinesAsync(
        int organizationId, long cartId, CancellationToken cancellationToken = default);

    /// <summary>These sizes of the organization, priced now (visible or not; IsVisible says).</summary>
    Task<IReadOnlyList<VariantLine>> GetVariantsAsync(
        int organizationId, IReadOnlyList<long> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Creates an active cart with these lines (quantity and the price shown now).</summary>
    Task<Guid> CreateCartAsync(
        int? customerId, IReadOnlyList<VariantLine> lines, CancellationToken cancellationToken = default);

    /// <summary>Sets a line's quantity and price shown (inserting it when new); keeps the cart active.</summary>
    Task SetItemAsync(
        long cartId, long variantId, int quantity, decimal unitPrice, int? customerId,
        CancellationToken cancellationToken = default);

    Task RemoveItemAsync(long cartId, long variantId, int? customerId, CancellationToken cancellationToken = default);

    /// <summary>Incomplete-order capture: stores the values given and checkout_started_at the first time.</summary>
    Task UpdateContactAsync(
        long cartId, string? name, string? phone, string? address, CancellationToken cancellationToken = default);
}

public sealed class CartHeader
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? CustomerId { get; set; }

    /// <summary>Carts that became an order (converted) or were merged into another are gone for the storefront.</summary>
    public bool IsOpen => Status is "active" or "abandoned";
}
