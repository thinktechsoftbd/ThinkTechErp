using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

[ApiController]
[Route("api/settings/shipping-zones")]
[Authorize(Policy = ShippingZonePolicies.View)]
[SecurityExceptionFilter]
public sealed class SettingShippingZoneController(IShippingZoneService shippingZoneService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ShippingZone>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ShippingZone>>> GetShippingZones(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await shippingZoneService.GetShippingZonesAsync(search, isActive, limit, offset, cancellationToken));

    [HttpGet("{shippingZoneId:int:range(1,32767)}")]
    [ProducesResponseType(typeof(ShippingZone), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShippingZone>> GetShippingZone(short shippingZoneId, CancellationToken cancellationToken)
    {
        var zone = await shippingZoneService.GetShippingZoneAsync(shippingZoneId, cancellationToken);
        return zone is null ? NotFound() : Ok(zone);
    }

    [HttpPost]
    [Authorize(Policy = ShippingZonePolicies.Manage)]
    [ProducesResponseType(typeof(ShippingZone), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShippingZone>> CreateShippingZone(
        [FromBody] ShippingZoneUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var zone = await shippingZoneService.CreateShippingZoneAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetShippingZone), new { shippingZoneId = zone.Id }, zone);
    }

    [HttpPut("{shippingZoneId:int:range(1,32767)}")]
    [Authorize(Policy = ShippingZonePolicies.Manage)]
    [ProducesResponseType(typeof(ShippingZone), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShippingZone>> UpdateShippingZone(
        short shippingZoneId,
        [FromBody] ShippingZoneUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var zone = await shippingZoneService.UpdateShippingZoneAsync(shippingZoneId, request, cancellationToken);
        return zone is null ? NotFound() : Ok(zone);
    }

    [HttpDelete("{shippingZoneId:int:range(1,32767)}")]
    [Authorize(Policy = ShippingZonePolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteShippingZone(short shippingZoneId, CancellationToken cancellationToken)
        => await shippingZoneService.DeleteShippingZoneAsync(shippingZoneId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new
            {
                error = "The shipping zone is in use (by districts, areas, shipping rates or orders). Deactivate it instead."
            })
        };
}
