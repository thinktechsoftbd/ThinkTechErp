using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpRepository.Settings;

public interface IDivisionRepository
{
    Task<PagedResult<Division>> GetDivisionsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Division?> GetDivisionAsync(short divisionId, CancellationToken cancellationToken = default);
    Task<bool> DivisionNameExistsAsync(string name, short? excludeDivisionId, CancellationToken cancellationToken = default);
    Task<short> CreateDivisionAsync(DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateDivisionAsync(
        short divisionId, DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteDivisionAsync(short divisionId, CancellationToken cancellationToken = default);
}
