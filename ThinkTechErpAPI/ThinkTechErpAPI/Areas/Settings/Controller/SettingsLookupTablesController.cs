using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

[ApiController]
[Route("api/settings/lookup-tables")]
[Authorize(Policy = SettingsPolicies.View)]
[SecurityExceptionFilter]
public sealed class SettingsLookupTablesController(ILookupService lookupService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<LookupTable>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LookupTable>>> GetLookupTables(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await lookupService.GetTablesAsync(search, isActive, limit, offset, cancellationToken));

    [HttpGet("{lookupTableId:long}")]
    [ProducesResponseType(typeof(LookupTable), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LookupTable>> GetLookupTable(long lookupTableId, CancellationToken cancellationToken)
    {
        var lookupTable = await lookupService.GetTableAsync(lookupTableId, cancellationToken);
        return lookupTable is null ? NotFound() : Ok(lookupTable);
    }

    [HttpPost]
    [Authorize(Policy = SettingsPolicies.Manage)]
    [ProducesResponseType(typeof(LookupTable), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LookupTable>> CreateLookupTable(
        [FromBody] LookupTableUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var lookupTable = await lookupService.CreateTableAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetLookupTable), new { lookupTableId = lookupTable.Id }, lookupTable);
    }

    [HttpPut("{lookupTableId:long}")]
    [Authorize(Policy = SettingsPolicies.Manage)]
    [ProducesResponseType(typeof(LookupTable), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LookupTable>> UpdateLookupTable(
        long lookupTableId,
        [FromBody] LookupTableUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var lookupTable = await lookupService.UpdateTableAsync(lookupTableId, request, userId, cancellationToken);
        return lookupTable is null ? NotFound() : Ok(lookupTable);
    }

    [HttpDelete("{lookupTableId:long}")]
    [Authorize(Policy = SettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteLookupTable(long lookupTableId, CancellationToken cancellationToken)
        => await lookupService.DeleteTableAsync(lookupTableId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The lookup table still has values. Delete them first, or deactivate the table." })
        };
}
