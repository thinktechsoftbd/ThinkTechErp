using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISubscriptionPlanService
{
    Task<PagedResult<SubscriptionPlan>> GetPlansAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan> CreatePlanAsync(
        SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> UpdatePlanAsync(
        int planId, SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePlanAsync(int planId, CancellationToken cancellationToken = default);
}
