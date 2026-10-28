using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISubscriptionModuleRepository
{
    Task<PagedResult<SubscriptionModule>> GetModulesAsync(string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SubscriptionModule?> GetModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string moduleName, int? excludeSubscriptionModuleId, CancellationToken cancellationToken = default);
    Task<int> CreateModuleAsync(SubscriptionModuleUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateModuleAsync(int subscriptionModuleId, SubscriptionModuleUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default);
}
