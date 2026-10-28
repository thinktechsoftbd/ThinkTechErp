using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISubscriptionRepository
{
    Task<PagedResult<Subscription>> GetSubscriptionsAsync(SubscriptionFilter filter, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Subscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the organization already has a Pending or Active subscription to the module whose
    /// dates overlap [startDate, endDate].
    /// </summary>
    Task<bool> HasOverlapAsync(int organizationId, int subscriptionModuleId, DateOnly startDate, DateOnly endDate,
        long? excludeSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Saves the request as given; the caller sets EndDate and DiscountAmount. The total is subtotal − discount + VAT.</summary>
    Task<long> CreateSubscriptionAsync(SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateSubscriptionAsync(long subscriptionId, SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);
}
