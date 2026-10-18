using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class SecurityRoleService(ISecurityRoleRepository roles) : ISecurityRoleService
{
    public Task<IReadOnlyList<SecurityRole>> GetRolesAsync(CancellationToken cancellationToken = default)
        => roles.GetRolesAsync(cancellationToken);

    public Task<SecurityRole?> GetRoleAsync(int roleId, CancellationToken cancellationToken = default)
        => roles.GetRoleAsync(roleId, cancellationToken);

    public async Task<SecurityRole> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, null, cancellationToken);
        var roleId = await SecurityDbErrors.ExecuteAsync(() => roles.CreateRoleAsync(request, cancellationToken));
        return await roles.GetRoleAsync(roleId, cancellationToken)
            ?? throw new InvalidOperationException("Created role could not be read back.");
    }

    public async Task<SecurityRole?> UpdateRoleAsync(
        int roleId, RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, roleId, cancellationToken);
        if (!await SecurityDbErrors.ExecuteAsync(() => roles.UpdateRoleAsync(roleId, request, cancellationToken)))
            return null;

        return await roles.GetRoleAsync(roleId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteRoleAsync(int roleId, CancellationToken cancellationToken = default)
        => roles.DeleteRoleAsync(roleId, cancellationToken);

    private async Task NormalizeAsync(RoleUpsertRequest request, int? roleId, CancellationToken cancellationToken)
    {
        request.Name = request.Name.Trim();
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (await roles.NameExistsAsync(request.Name, roleId, cancellationToken))
            throw new SecurityConflictException("A role with this name already exists.");
    }
}
