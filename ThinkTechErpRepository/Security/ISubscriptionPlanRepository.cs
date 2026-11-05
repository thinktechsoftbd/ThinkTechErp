using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISubscriptionPlanRepository
{
    /// <summary>A page of plans, each with its features and their details.</summary>
    Task<PagedResult<SubscriptionPlan>> GetPlansAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default);
    Task<bool> PlanNameExistsAsync(string nameEn, int? excludePlanId, CancellationToken cancellationToken = default);
    /// <summary>The plan with its features and details, in one transaction.</summary>
    Task<int> CreatePlanAsync(SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// The plan, its features and their details, in one transaction: saved features and details keep their id and
    /// are updated in place, new ones are added, and those no longer listed are removed.
    /// </summary>
    Task<bool> UpdatePlanAsync(
        int planId, SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>The plan with its features and details.</summary>
    Task<DeleteOutcome> DeletePlanAsync(int planId, CancellationToken cancellationToken = default);
}
