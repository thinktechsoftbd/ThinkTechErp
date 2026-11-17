using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.ThinkCart;

public sealed class ThinkCartCartRepository(IDbConnectionFactory connectionFactory) : IThinkCartCartRepository
{
    public async Task<CartHeader?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CartHeader>(new CommandDefinition("""
            SELECT id AS Id, object_id AS ObjectId, status AS Status, customer_id AS CustomerId
            FROM public.cart
            WHERE object_id = @CartId
            """, new { CartId = cartId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<VariantLine>> GetCartLinesAsync(
        int organizationId, long cartId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await ReadCartLinesAsync(connection, null, organizationId, cartId, cancellationToken);
    }

    public async Task<IReadOnlyList<VariantLine>> GetVariantsAsync(
        int organizationId, IReadOnlyList<long> variantIds, CancellationToken cancellationToken = default)
    {
        if (variantIds.Count == 0)
            return [];

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<VariantLine>(new CommandDefinition($"""
            SELECT {ThinkCartSql.VariantLineColumns}
            FROM public.product_variant pv
            {ThinkCartSql.VariantJoins}
            WHERE pv.id = ANY(@Ids)
            """, new { Org = organizationId, Ids = variantIds.Distinct().ToArray() }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Guid> CreateCartAsync(
        int? customerId, IReadOnlyList<VariantLine> lines, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var cart = await connection.QuerySingleAsync<(long Id, Guid ObjectId)>(new CommandDefinition("""
            INSERT INTO public.cart (customer_id, status) VALUES (@CustomerId, 'active')
            RETURNING id, object_id
            """, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken));

        if (lines.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.cart_item (cart_id, product_variant_id, quantity, unit_price)
                SELECT @CartId, line.variant_id, line.quantity, line.unit_price
                FROM unnest(CAST(@VariantIds AS bigint[]), CAST(@Quantities AS integer[]), CAST(@Prices AS numeric[]))
                     AS line(variant_id, quantity, unit_price)
                """, new
            {
                CartId = cart.Id,
                VariantIds = lines.Select(line => line.VariantId).ToArray(),
                Quantities = lines.Select(line => line.Quantity).ToArray(),
                Prices = lines.Select(line => line.Price).ToArray()
            }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return cart.ObjectId;
    }

    public async Task SetItemAsync(
        long cartId, long variantId, int quantity, decimal unitPrice, int? customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.cart_item (cart_id, product_variant_id, quantity, unit_price)
            VALUES (@CartId, @VariantId, @Quantity, @UnitPrice)
            ON CONFLICT (cart_id, product_variant_id)
            DO UPDATE SET quantity = EXCLUDED.quantity, unit_price = EXCLUDED.unit_price, updated_at = now();

            UPDATE public.cart
            SET status = 'active', customer_id = coalesce(customer_id, @CustomerId), updated_at = now()
            WHERE id = @CartId;
            """, new { CartId = cartId, VariantId = variantId, Quantity = quantity, UnitPrice = unitPrice, CustomerId = customerId },
            cancellationToken: cancellationToken));
    }

    public async Task RemoveItemAsync(long cartId, long variantId, int? customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.cart_item WHERE cart_id = @CartId AND product_variant_id = @VariantId;

            UPDATE public.cart
            SET status = 'active', customer_id = coalesce(customer_id, @CustomerId), updated_at = now()
            WHERE id = @CartId;
            """, new { CartId = cartId, VariantId = variantId, CustomerId = customerId }, cancellationToken: cancellationToken));
    }

    public async Task UpdateContactAsync(
        long cartId, string? name, string? phone, string? address, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.cart
            SET contact_name = coalesce(@Name, contact_name),
                contact_phone = coalesce(@Phone, contact_phone),
                contact_address = coalesce(@Address, contact_address),
                checkout_started_at = coalesce(checkout_started_at, now()),
                updated_at = now()
            WHERE id = @CartId
            """, new { CartId = cartId, Name = name, Phone = phone, Address = address }, cancellationToken: cancellationToken));
    }

    /// <summary>A cart's lines priced now. Shared with place-order, which reads them inside its transaction.</summary>
    internal static async Task<IReadOnlyList<VariantLine>> ReadCartLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, long cartId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<VariantLine>(new CommandDefinition($"""
            SELECT {ThinkCartSql.VariantLineColumns}, ci.quantity AS Quantity
            FROM public.cart_item ci
            JOIN public.product_variant pv ON pv.id = ci.product_variant_id
            {ThinkCartSql.VariantJoins}
            WHERE ci.cart_id = @CartId
            ORDER BY ci.added_at, ci.id
            """, new { Org = organizationId, CartId = cartId }, transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
