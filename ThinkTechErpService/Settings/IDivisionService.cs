using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpService.Settings;

public interface IDivisionService
{
    Task<PagedResult<Division>> GetDivisionsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Division?> GetDivisionAsync(short divisionId, CancellationToken cancellationToken = default);
    Task<Division> CreateDivisionAsync(DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Division?> UpdateDivisionAsync(
        short divisionId, DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteDivisionAsync(short divisionId, CancellationToken cancellationToken = default);
}
