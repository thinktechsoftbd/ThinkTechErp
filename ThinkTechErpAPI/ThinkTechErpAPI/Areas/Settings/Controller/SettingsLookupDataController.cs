using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

[ApiController]
[Route("api/settings/lookup-data")]
[Authorize(Policy = SettingsPolicies.View)]
[SecurityExceptionFilter]
public sealed class SettingsLookupDataController(ILookupService lookupService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<LookupData>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LookupData>>> GetLookupData(
        [FromQuery, Range(1, long.MaxValue)] long? lookupTableId,
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await lookupService.GetDataAsync(lookupTableId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{lookupDataId:long}")]
    [ProducesResponseType(typeof(LookupData), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LookupData>> GetLookupDataItem(long lookupDataId, CancellationToken cancellationToken)
    {
        var lookupData = await lookupService.GetDataItemAsync(lookupDataId, cancellationToken);
        return lookupData is null ? NotFound() : Ok(lookupData);
    }

    [HttpPost]
    [Authorize(Policy = SettingsPolicies.Manage)]
    [ProducesResponseType(typeof(LookupData), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LookupData>> CreateLookupData(
        [FromBody] LookupDataUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var lookupData = await lookupService.CreateDataAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetLookupDataItem), new { lookupDataId = lookupData.Id }, lookupData);
    }

    [HttpPut("{lookupDataId:long}")]
    [Authorize(Policy = SettingsPolicies.Manage)]
    [ProducesResponseType(typeof(LookupData), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LookupData>> UpdateLookupData(
        long lookupDataId,
        [FromBody] LookupDataUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var lookupData = await lookupService.UpdateDataAsync(lookupDataId, request, userId, cancellationToken);
        return lookupData is null ? NotFound() : Ok(lookupData);
    }

    [HttpDelete("{lookupDataId:long}")]
    [Authorize(Policy = SettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteLookupData(long lookupDataId, CancellationToken cancellationToken)
        => await lookupService.DeleteDataAsync(lookupDataId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The value is in use (for example as a subscription's currency). Deactivate it instead." })
        };
}
