using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public interface IUserOrganizationService
{
    /// <summary>Organizations the user can work in; every active one for organization managers (Super Admin).</summary>
    Task<IReadOnlyList<UserOrganization>> GetAccessibleAsync(int userId, bool canManageOrganizations, CancellationToken cancellationToken = default);

    /// <summary>The requested organization (or the user's default) when the user may use it; null when they have none.</summary>
    Task<int?> ResolveAsync(int userId, bool canManageOrganizations, int? requestedOrganizationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserOrganization>?> GetLinkedAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserOrganization>?> ReplaceAsync(int userId, UserOrganizationsUpdateRequest request, int actingUserId,
        CancellationToken cancellationToken = default);
}

public sealed class UserOrganizationService(IUserOrganizationRepository repository, IOrganizationService organizations)
    : IUserOrganizationService
{
    public Task<IReadOnlyList<UserOrganization>> GetAccessibleAsync(
        int userId, bool canManageOrganizations, CancellationToken cancellationToken = default)
        => repository.GetAccessibleAsync(userId, canManageOrganizations, cancellationToken);

    public async Task<int?> ResolveAsync(
        int userId, bool canManageOrganizations, int? requestedOrganizationId, CancellationToken cancellationToken = default)
    {
        var accessible = await repository.GetAccessibleAsync(userId, canManageOrganizations, cancellationToken);
        if (requestedOrganizationId is not null)
            return accessible.Any(o => o.OrganizationId == requestedOrganizationId)
                ? requestedOrganizationId
                : throw new BusinessRuleException("You don't have access to this organization.");
        return (accessible.FirstOrDefault(o => o.IsDefault) ?? accessible.FirstOrDefault())?.OrganizationId;
    }

    public async Task<IReadOnlyList<UserOrganization>?> GetLinkedAsync(int userId, CancellationToken cancellationToken = default)
        => await repository.UserExistsAsync(userId, cancellationToken)
            ? await repository.GetLinkedAsync(userId, cancellationToken)
            : null;

    public async Task<IReadOnlyList<UserOrganization>?> ReplaceAsync(
        int userId, UserOrganizationsUpdateRequest request, int actingUserId, CancellationToken cancellationToken = default)
    {
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            return null;

        var ids = request.OrganizationIds.Where(id => id > 0).Distinct().ToList();
        var known = (await organizations.GetOrganizationsAsync(cancellationToken)).Select(o => o.Id).ToHashSet();
        if (ids.Any(id => !known.Contains(id)))
            throw new BusinessRuleException("One of the organizations doesn't exist.");
        if (request.DefaultOrganizationId is not null && !ids.Contains(request.DefaultOrganizationId.Value))
            throw new BusinessRuleException("The default organization must be one of the user's organizations.");

        await repository.ReplaceLinksAsync(userId, ids, request.DefaultOrganizationId, actingUserId, cancellationToken);
        return await repository.GetLinkedAsync(userId, cancellationToken);
    }
}
