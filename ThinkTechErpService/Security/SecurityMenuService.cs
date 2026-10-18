using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class SecurityMenuService(ISecurityMenuRepository menus) : ISecurityMenuService
{
    public Task<IReadOnlyList<SecurityMenu>> GetMenusAsync(CancellationToken cancellationToken = default)
        => menus.GetMenusAsync(cancellationToken);

    public Task<SecurityMenu?> GetMenuAsync(int menuId, CancellationToken cancellationToken = default)
        => menus.GetMenuAsync(menuId, cancellationToken);

    public async Task<SecurityMenu> CreateMenuAsync(MenuUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var menuId = await SecurityDbErrors.ExecuteAsync(() => menus.CreateMenuAsync(request, cancellationToken));
        return await menus.GetMenuAsync(menuId, cancellationToken)
            ?? throw new InvalidOperationException("Created menu could not be read back.");
    }

    public async Task<SecurityMenu?> UpdateMenuAsync(
        int menuId, MenuUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (request.ParentId is int parentId &&
            await menus.IsSameOrDescendantAsync(parentId, menuId, cancellationToken))
            throw new SecurityValidationException("A menu cannot be moved under itself or one of its submenus.");

        if (!await SecurityDbErrors.ExecuteAsync(() => menus.UpdateMenuAsync(menuId, request, cancellationToken)))
            return null;

        return await menus.GetMenuAsync(menuId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteMenuAsync(int menuId, CancellationToken cancellationToken = default)
        => menus.DeleteMenuAsync(menuId, cancellationToken);

    private static void Normalize(MenuUpsertRequest request)
    {
        request.Title = request.Title.Trim();
        request.Path = request.Path.Trim();
        request.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
    }
}
