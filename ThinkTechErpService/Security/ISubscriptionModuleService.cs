using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISubscriptionModuleService
{
    Task<PagedResult<SubscriptionModule>> GetModulesAsync(string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SubscriptionModule?> GetModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default);
    Task<SubscriptionModule> CreateModuleAsync(SubscriptionModuleUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SubscriptionModule?> UpdateModuleAsync(int subscriptionModuleId, SubscriptionModuleUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default);
}
