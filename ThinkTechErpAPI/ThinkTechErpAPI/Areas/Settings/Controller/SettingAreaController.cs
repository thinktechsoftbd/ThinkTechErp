using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

[ApiController]
[Route("api/settings/areas")]
[Authorize(Policy = AreaPolicies.View)]
[SecurityExceptionFilter]
public sealed class SettingAreaController(IAreaService areaService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<Area>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<Area>>> GetAreas(
        [FromQuery, Range(1, short.MaxValue)] short? districtId,
        [FromQuery, StringLength(20)] string? areaType,
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await areaService.GetAreasAsync(districtId, areaType, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{areaId:int}")]
    [ProducesResponseType(typeof(Area), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Area>> GetArea(int areaId, CancellationToken cancellationToken)
    {
        var area = await areaService.GetAreaAsync(areaId, cancellationToken);
        return area is null ? NotFound() : Ok(area);
    }

    [HttpPost]
    [Authorize(Policy = AreaPolicies.Manage)]
    [ProducesResponseType(typeof(Area), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Area>> CreateArea(
        [FromBody] AreaUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var area = await areaService.CreateAreaAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetArea), new { areaId = area.Id }, area);
    }

    [HttpPut("{areaId:int}")]
    [Authorize(Policy = AreaPolicies.Manage)]
    [ProducesResponseType(typeof(Area), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Area>> UpdateArea(
        int areaId,
        [FromBody] AreaUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var area = await areaService.UpdateAreaAsync(areaId, request, userId, cancellationToken);
        return area is null ? NotFound() : Ok(area);
    }

    [HttpDelete("{areaId:int}")]
    [Authorize(Policy = AreaPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteArea(int areaId, CancellationToken cancellationToken)
        => await areaService.DeleteAreaAsync(areaId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The area is in use (for example by a supplier address). Deactivate it instead." })
        };

    /// <summary>Districts for the Area screen's picker and filter.</summary>
    [HttpGet("districts")]
    [ProducesResponseType(typeof(PagedResult<DistrictOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DistrictOption>>> GetDistricts(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await areaService.GetDistrictsAsync(search, limit, offset, cancellationToken));

    /// <summary>The active shipping zones an area can override its district's zone with.</summary>
    [HttpGet("shipping-zones")]
    [ProducesResponseType(typeof(IReadOnlyList<ShippingZoneOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShippingZoneOption>>> GetShippingZones(
        CancellationToken cancellationToken)
        => Ok(await areaService.GetShippingZonesAsync(cancellationToken));
}
