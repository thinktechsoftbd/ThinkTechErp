using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Settings;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.Settings;

public sealed partial class ShippingZoneService(IShippingZoneRepository zones) : IShippingZoneService
{
    public Task<PagedResult<ShippingZone>> GetShippingZonesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => zones.GetShippingZonesAsync(Clean(search), isActive, limit, offset, cancellationToken);

    public Task<ShippingZone?> GetShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default)
        => zones.GetShippingZoneAsync(shippingZoneId, cancellationToken);

    public async Task<ShippingZone> CreateShippingZoneAsync(
        ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await zones.ShippingZoneCodeExistsAsync(request.Code, null, cancellationToken))
            throw new SecurityConflictException("A shipping zone with this code already exists.");

        var shippingZoneId = await SecurityDbErrors.ExecuteAsync(() =>
            zones.CreateShippingZoneAsync(request, cancellationToken));
        return await zones.GetShippingZoneAsync(shippingZoneId, cancellationToken)
            ?? throw new InvalidOperationException("Created shipping zone could not be read back.");
    }

    public async Task<ShippingZone?> UpdateShippingZoneAsync(
        short shippingZoneId, ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await zones.ShippingZoneCodeExistsAsync(request.Code, shippingZoneId, cancellationToken))
            throw new SecurityConflictException("A shipping zone with this code already exists.");

        if (!await SecurityDbErrors.ExecuteAsync(() =>
                zones.UpdateShippingZoneAsync(shippingZoneId, request, cancellationToken)))
            return null;

        return await zones.GetShippingZoneAsync(shippingZoneId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default)
        => zones.DeleteShippingZoneAsync(shippingZoneId, cancellationToken);

    private static void Normalize(ShippingZoneUpsertRequest request)
    {
        request.Code = request.Code.Trim();
        request.Name = request.Name.Trim();
        if (!CodePattern().IsMatch(request.Code))
            throw new SecurityValidationException("Code: use lowercase letters, digits and underscores, e.g. inside_dhaka.");
        if (request.Name.Length == 0)
            throw new SecurityValidationException("Name is required.");
        if (request.MinDeliveryDays is short min && request.MaxDeliveryDays is short max && min > max)
            throw new SecurityValidationException("Minimum delivery days can't be more than the maximum.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(ShippingZoneRules.CodePattern)]
    private static partial Regex CodePattern();
}
