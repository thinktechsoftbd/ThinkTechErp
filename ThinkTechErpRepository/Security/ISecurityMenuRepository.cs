using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISecurityMenuRepository
{
    Task<IReadOnlyList<SecurityMenu>> GetMenusAsync(CancellationToken cancellationToken = default);
    Task<SecurityMenu?> GetMenuAsync(int menuId, CancellationToken cancellationToken = default);
    Task<int> CreateMenuAsync(MenuUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateMenuAsync(int menuId, MenuUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteMenuAsync(int menuId, CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="menuId"/> is <paramref name="rootMenuId"/> or one of its submenus.</summary>
    Task<bool> IsSameOrDescendantAsync(int menuId, int rootMenuId, CancellationToken cancellationToken = default);
}
