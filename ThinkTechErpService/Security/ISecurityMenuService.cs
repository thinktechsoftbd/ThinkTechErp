using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISecurityMenuService
{
    Task<IReadOnlyList<SecurityMenu>> GetMenusAsync(CancellationToken cancellationToken = default);
    Task<SecurityMenu?> GetMenuAsync(int menuId, CancellationToken cancellationToken = default);
    Task<SecurityMenu> CreateMenuAsync(MenuUpsertRequest request, CancellationToken cancellationToken = default);
    Task<SecurityMenu?> UpdateMenuAsync(int menuId, MenuUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteMenuAsync(int menuId, CancellationToken cancellationToken = default);
}
