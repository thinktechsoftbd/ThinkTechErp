using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class SubscriptionModuleService(ISubscriptionModuleRepository modules) : ISubscriptionModuleService
{
    public Task<PagedResult<SubscriptionModule>> GetModulesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => modules.GetModulesAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), isActive, limit, offset, cancellationToken);

    public Task<SubscriptionModule?> GetModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default)
        => modules.GetModuleAsync(subscriptionModuleId, cancellationToken);

    public async Task<SubscriptionModule> CreateModuleAsync(
        SubscriptionModuleUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, null, cancellationToken);
        var moduleId = await SecurityDbErrors.ExecuteAsync(() => modules.CreateModuleAsync(request, userId, cancellationToken));
        return await modules.GetModuleAsync(moduleId, cancellationToken)
            ?? throw new InvalidOperationException("Created subscription module could not be read back.");
    }

    public async Task<SubscriptionModule?> UpdateModuleAsync(
        int subscriptionModuleId, SubscriptionModuleUpsertRequest request, int userId,
        CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, subscriptionModuleId, cancellationToken);
        if (!await SecurityDbErrors.ExecuteAsync(() =>
                modules.UpdateModuleAsync(subscriptionModuleId, request, userId, cancellationToken)))
            return null;

        return await modules.GetModuleAsync(subscriptionModuleId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default)
        => modules.DeleteModuleAsync(subscriptionModuleId, cancellationToken);

    private async Task NormalizeAsync(
        SubscriptionModuleUpsertRequest request, int? subscriptionModuleId, CancellationToken cancellationToken)
    {
        request.ModuleName = request.ModuleName.Trim();
        request.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        if (await modules.NameExistsAsync(request.ModuleName, subscriptionModuleId, cancellationToken))
            throw new SecurityConflictException("A subscription module with this name already exists.");
    }
}
