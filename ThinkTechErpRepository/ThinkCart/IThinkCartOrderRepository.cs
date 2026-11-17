using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;

namespace ThinkTechErpRepository.ThinkCart;

/// <summary>
/// Web orders as customers see them. ecom_order has no organization of its own; an order belongs to the
/// organization of its customer (supplier.organization_id).
/// </summary>
public interface IThinkCartOrderRepository
{
    Task<PagedResult<OrderSummary>> GetCustomerOrdersAsync(
        int organizationId, int customerId, string? status, int limit, int offset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An order by number, of this customer (when customerId is given) or shipped to this phone (when
    /// shippingPhone is given); null otherwise.
    /// </summary>
    Task<OrderDetail?> GetOrderAsync(
        int organizationId, string orderNumber, int? customerId, string? shippingPhone,
        CancellationToken cancellationToken = default);

    Task<AccountStats> GetStatsAsync(int customerId, CancellationToken cancellationToken = default);
}
