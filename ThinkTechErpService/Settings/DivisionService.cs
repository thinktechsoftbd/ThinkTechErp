using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Settings;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.Settings;

public sealed class DivisionService(IDivisionRepository divisions) : IDivisionService
{
    public Task<PagedResult<Division>> GetDivisionsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => divisions.GetDivisionsAsync(Clean(search), isActive, limit, offset, cancellationToken);

    public Task<Division?> GetDivisionAsync(short divisionId, CancellationToken cancellationToken = default)
        => divisions.GetDivisionAsync(divisionId, cancellationToken);

    public async Task<Division> CreateDivisionAsync(
        DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await divisions.DivisionNameExistsAsync(request.Name, null, cancellationToken))
            throw new SecurityConflictException("A division with this name already exists.");

        var divisionId = await SecurityDbErrors.ExecuteAsync(() =>
            divisions.CreateDivisionAsync(request, userId, cancellationToken));
        return await divisions.GetDivisionAsync(divisionId, cancellationToken)
            ?? throw new InvalidOperationException("Created division could not be read back.");
    }

    public async Task<Division?> UpdateDivisionAsync(
        short divisionId, DivisionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await divisions.DivisionNameExistsAsync(request.Name, divisionId, cancellationToken))
            throw new SecurityConflictException("A division with this name already exists.");

        if (!await SecurityDbErrors.ExecuteAsync(() =>
                divisions.UpdateDivisionAsync(divisionId, request, userId, cancellationToken)))
            return null;

        return await divisions.GetDivisionAsync(divisionId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteDivisionAsync(short divisionId, CancellationToken cancellationToken = default)
        => divisions.DeleteDivisionAsync(divisionId, cancellationToken);

    private static void Normalize(DivisionUpsertRequest request)
    {
        request.Name = request.Name.Trim();
        request.NameBn = Clean(request.NameBn);
        if (request.Name.Length == 0)
            throw new SecurityValidationException("Name is required.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
