using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

[ApiController]
[Route("api/settings/divisions")]
[Authorize(Policy = DivisionPolicies.View)]
[SecurityExceptionFilter]
public sealed class SettingDivisionController(IDivisionService divisionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<Division>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<Division>>> GetDivisions(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await divisionService.GetDivisionsAsync(search, isActive, limit, offset, cancellationToken));

    [HttpGet("{divisionId:int:range(1,32767)}")]
    [ProducesResponseType(typeof(Division), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Division>> GetDivision(short divisionId, CancellationToken cancellationToken)
    {
        var division = await divisionService.GetDivisionAsync(divisionId, cancellationToken);
        return division is null ? NotFound() : Ok(division);
    }

    [HttpPost]
    [Authorize(Policy = DivisionPolicies.Manage)]
    [ProducesResponseType(typeof(Division), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Division>> CreateDivision(
        [FromBody] DivisionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var division = await divisionService.CreateDivisionAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetDivision), new { divisionId = division.Id }, division);
    }

    [HttpPut("{divisionId:int:range(1,32767)}")]
    [Authorize(Policy = DivisionPolicies.Manage)]
    [ProducesResponseType(typeof(Division), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Division>> UpdateDivision(
        short divisionId,
        [FromBody] DivisionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var division = await divisionService.UpdateDivisionAsync(divisionId, request, userId, cancellationToken);
        return division is null ? NotFound() : Ok(division);
    }

    [HttpDelete("{divisionId:int:range(1,32767)}")]
    [Authorize(Policy = DivisionPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDivision(short divisionId, CancellationToken cancellationToken)
        => await divisionService.DeleteDivisionAsync(divisionId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The division still has districts. Deactivate it instead." })
        };
}
