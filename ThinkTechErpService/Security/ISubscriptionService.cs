using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISubscriptionService
{
    Task<PagedResult<Subscription>> GetSubscriptionsAsync(SubscriptionFilter filter, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Subscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);
    Task<Subscription> CreateSubscriptionAsync(SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Subscription?> UpdateSubscriptionAsync(long subscriptionId, SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);
}
