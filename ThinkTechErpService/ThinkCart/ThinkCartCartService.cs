using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.ThinkCart;

namespace ThinkTechErpService.ThinkCart;

public interface IThinkCartCartService
{
    Task<Cart> CreateCartAsync(
        int organizationId, int? customerId, CartCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null when the cart doesn't exist or became an order.</summary>
    Task<Cart?> GetCartAsync(int organizationId, Guid cartId, CancellationToken cancellationToken = default);

    /// <summary>Sets a line's quantity (0 removes it; clamped to the order limits; 409 when out of stock).</summary>
    Task<Cart?> SetItemAsync(
        int organizationId, int? customerId, Guid cartId, CartLineInput input, CancellationToken cancellationToken = default);

    Task<Cart?> RemoveItemAsync(
        int organizationId, int? customerId, Guid cartId, long variantId, CancellationToken cancellationToken = default);

    /// <returns>False when the cart doesn't exist or became an order.</returns>
    Task<bool> UpdateContactAsync(Guid cartId, CartContactRequest request, CancellationToken cancellationToken = default);

    /// <summary>"You may also like": frequently bought together, then related, of the cart's products.</summary>
    Task<IReadOnlyList<ProductCard>?> GetRecommendationsAsync(
        int organizationId, Guid cartId, int limit, CancellationToken cancellationToken = default);
}

public sealed class ThinkCartCartService(
    IThinkCartCartRepository carts,
    IThinkCartCatalogRepository catalog,
    IThinkCartCatalogService catalogService) : IThinkCartCartService
{
    public async Task<Cart> CreateCartAsync(
        int organizationId, int? customerId, CartCreateRequest request, CancellationToken cancellationToken = default)
    {
        // The same size twice counts once, with the quantities added up.
        var wanted = (request.Items ?? [])
            .Where(item => item.Quantity > 0)
            .GroupBy(item => item.VariantId)
            .Select(group => (VariantId: group.Key, Quantity: group.Sum(item => item.Quantity)))
            .ToList();

        var variants = (await carts.GetVariantsAsync(
                organizationId, wanted.Select(item => item.VariantId).ToList(), cancellationToken))
            .ToDictionary(line => line.VariantId);

        var lines = new List<VariantLine>();
        foreach (var (variantId, quantity) in wanted)
        {
            if (!variants.TryGetValue(variantId, out var line))
                throw new BusinessRuleException($"Product variant {variantId} isn't available.");

            line.Quantity = ThinkCartPricingEngine.ClampQuantity(line, Math.Min(quantity, ThinkCartRules.MaxCartQuantity));
            ThinkCartPricingEngine.EnsureAvailable(line, line.Quantity);
            lines.Add(line);
        }

        var cartId = await carts.CreateCartAsync(customerId, lines, cancellationToken);
        return ThinkCartPricingEngine.ToCart(cartId, lines);
    }

    public async Task<Cart?> GetCartAsync(int organizationId, Guid cartId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetCartAsync(cartId, cancellationToken);
        if (cart is not { IsOpen: true })
            return null;

        return ThinkCartPricingEngine.ToCart(
            cart.ObjectId, await carts.GetCartLinesAsync(organizationId, cart.Id, cancellationToken));
    }

    public async Task<Cart?> SetItemAsync(
        int organizationId, int? customerId, Guid cartId, CartLineInput input, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetCartAsync(cartId, cancellationToken);
        if (cart is not { IsOpen: true })
            return null;

        if (input.Quantity <= 0)
        {
            await carts.RemoveItemAsync(cart.Id, input.VariantId, customerId, cancellationToken);
        }
        else
        {
            var line = (await carts.GetVariantsAsync(organizationId, [input.VariantId], cancellationToken)).SingleOrDefault()
                       ?? throw new BusinessRuleException($"Product variant {input.VariantId} isn't available.");
            var quantity = ThinkCartPricingEngine.ClampQuantity(line, Math.Min(input.Quantity, ThinkCartRules.MaxCartQuantity));
            ThinkCartPricingEngine.EnsureAvailable(line, quantity);
            await carts.SetItemAsync(cart.Id, line.VariantId, quantity, line.Price, customerId, cancellationToken);
        }

        return ThinkCartPricingEngine.ToCart(
            cart.ObjectId, await carts.GetCartLinesAsync(organizationId, cart.Id, cancellationToken));
    }

    public async Task<Cart?> RemoveItemAsync(
        int organizationId, int? customerId, Guid cartId, long variantId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetCartAsync(cartId, cancellationToken);
        if (cart is not { IsOpen: true })
            return null;

        await carts.RemoveItemAsync(cart.Id, variantId, customerId, cancellationToken);
        return ThinkCartPricingEngine.ToCart(
            cart.ObjectId, await carts.GetCartLinesAsync(organizationId, cart.Id, cancellationToken));
    }

    public async Task<bool> UpdateContactAsync(
        Guid cartId, CartContactRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetCartAsync(cartId, cancellationToken);
        if (cart is not { IsOpen: true })
            return false;

        // A partly typed phone is kept as typed, so the call centre still sees it.
        var phone = Clean(request.Phone);
        await carts.UpdateContactAsync(
            cart.Id, Clean(request.Name), phone is null ? null : ThinkCartRules.NormalizeMobile(phone) ?? phone,
            Clean(request.Address), cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ProductCard>?> GetRecommendationsAsync(
        int organizationId, Guid cartId, int limit, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetCartAsync(cartId, cancellationToken);
        if (cart is not { IsOpen: true })
            return null;

        var inCart = (await carts.GetCartLinesAsync(organizationId, cart.Id, cancellationToken))
            .Select(line => line.ListingId)
            .Distinct()
            .ToList();
        var ids = await catalog.GetRelatedListingIdsAsync(
            inCart, ["frequently_bought_together", "related"], cancellationToken);
        return await catalogService.GetCardsAsync(organizationId, ids, limit, cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
