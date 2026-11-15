using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

[ApiController]
[Route("api/settings/districts")]
[Authorize(Policy = DistrictPolicies.View)]
[SecurityExceptionFilter]
public sealed class SettingDistrictController(IDistrictService districtService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<District>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<District>>> GetDistricts(
        [FromQuery, Range(1, short.MaxValue)] short? divisionId,
        [FromQuery, Range(1, short.MaxValue)] short? shippingZoneId,
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await districtService.GetDistrictsAsync(
            divisionId, shippingZoneId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{districtId:int:range(1,32767)}")]
    [ProducesResponseType(typeof(District), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<District>> GetDistrict(short districtId, CancellationToken cancellationToken)
    {
        var district = await districtService.GetDistrictAsync(districtId, cancellationToken);
        return district is null ? NotFound() : Ok(district);
    }

    [HttpPost]
    [Authorize(Policy = DistrictPolicies.Manage)]
    [ProducesResponseType(typeof(District), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<District>> CreateDistrict(
        [FromBody] DistrictUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var district = await districtService.CreateDistrictAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetDistrict), new { districtId = district.Id }, district);
    }

    [HttpPut("{districtId:int:range(1,32767)}")]
    [Authorize(Policy = DistrictPolicies.Manage)]
    [ProducesResponseType(typeof(District), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<District>> UpdateDistrict(
        short districtId,
        [FromBody] DistrictUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var district = await districtService.UpdateDistrictAsync(districtId, request, userId, cancellationToken);
        return district is null ? NotFound() : Ok(district);
    }

    [HttpDelete("{districtId:int:range(1,32767)}")]
    [Authorize(Policy = DistrictPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDistrict(short districtId, CancellationToken cancellationToken)
        => await districtService.DeleteDistrictAsync(districtId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new
            {
                error = "The district is in use (by its areas, a warehouse, a supplier address or an order). Deactivate it instead."
            })
        };

    /// <summary>Every division, inactive ones flagged, for the District screen's picker and filter.</summary>
    [HttpGet("divisions")]
    [ProducesResponseType(typeof(IReadOnlyList<DivisionOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DivisionOption>>> GetDivisionOptions(CancellationToken cancellationToken)
        => Ok(await districtService.GetDivisionOptionsAsync(cancellationToken));

    /// <summary>Every shipping zone, inactive ones flagged, for the District screen's picker and filter.</summary>
    [HttpGet("shipping-zones")]
    [ProducesResponseType(typeof(IReadOnlyList<ShippingZoneOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShippingZoneOption>>> GetShippingZoneOptions(
        CancellationToken cancellationToken)
        => Ok(await districtService.GetShippingZoneOptionsAsync(cancellationToken));
}
