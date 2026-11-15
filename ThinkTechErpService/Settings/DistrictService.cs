using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Settings;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.Settings;

public sealed class DistrictService(IDistrictRepository districts) : IDistrictService
{
    public Task<PagedResult<District>> GetDistrictsAsync(
        short? divisionId, short? shippingZoneId, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default)
        => districts.GetDistrictsAsync(divisionId, shippingZoneId, Clean(search), isActive, limit, offset, cancellationToken);

    public Task<District?> GetDistrictAsync(short districtId, CancellationToken cancellationToken = default)
        => districts.GetDistrictAsync(districtId, cancellationToken);

    public async Task<District> CreateDistrictAsync(
        DistrictUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await districts.DistrictNameExistsAsync(request.DivisionId, request.Name, null, cancellationToken))
            throw new SecurityConflictException("This division already has a district with this name.");

        var districtId = await SecurityDbErrors.ExecuteAsync(() =>
            districts.CreateDistrictAsync(request, userId, cancellationToken));
        return await districts.GetDistrictAsync(districtId, cancellationToken)
            ?? throw new InvalidOperationException("Created district could not be read back.");
    }

    public async Task<District?> UpdateDistrictAsync(
        short districtId, DistrictUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await districts.DistrictNameExistsAsync(request.DivisionId, request.Name, districtId, cancellationToken))
            throw new SecurityConflictException("This division already has a district with this name.");

        if (!await SecurityDbErrors.ExecuteAsync(() =>
                districts.UpdateDistrictAsync(districtId, request, userId, cancellationToken)))
            return null;

        return await districts.GetDistrictAsync(districtId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteDistrictAsync(short districtId, CancellationToken cancellationToken = default)
        => districts.DeleteDistrictAsync(districtId, cancellationToken);

    public Task<IReadOnlyList<DivisionOption>> GetDivisionOptionsAsync(CancellationToken cancellationToken = default)
        => districts.GetDivisionOptionsAsync(cancellationToken);

    public Task<IReadOnlyList<ShippingZoneOption>> GetShippingZoneOptionsAsync(CancellationToken cancellationToken = default)
        => districts.GetShippingZoneOptionsAsync(cancellationToken);

    private static void Normalize(DistrictUpsertRequest request)
    {
        request.Name = request.Name.Trim();
        request.NameBn = Clean(request.NameBn);
        if (request.Name.Length == 0)
            throw new SecurityValidationException("Name is required.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
