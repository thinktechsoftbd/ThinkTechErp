using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>Menu administration. The signed-in user's own menu tree is GET api/security/auth/menus.</summary>
[ApiController]
[Route("api/security/menus")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityMenusController(ISecurityMenuService menuService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityMenu>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SecurityMenu>>> GetMenus(CancellationToken cancellationToken)
        => Ok(await menuService.GetMenusAsync(cancellationToken));

    [HttpGet("{menuId:int}")]
    [ProducesResponseType(typeof(SecurityMenu), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SecurityMenu>> GetMenu(int menuId, CancellationToken cancellationToken)
    {
        var menu = await menuService.GetMenuAsync(menuId, cancellationToken);
        return menu is null ? NotFound() : Ok(menu);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityMenu), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SecurityMenu>> CreateMenu(
        [FromBody] MenuUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var menu = await menuService.CreateMenuAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetMenu), new { menuId = menu.Id }, menu);
    }

    [HttpPut("{menuId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityMenu), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SecurityMenu>> UpdateMenu(
        int menuId,
        [FromBody] MenuUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var menu = await menuService.UpdateMenuAsync(menuId, request, cancellationToken);
        return menu is null ? NotFound() : Ok(menu);
    }

    [HttpDelete("{menuId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteMenu(int menuId, CancellationToken cancellationToken)
        => await menuService.DeleteMenuAsync(menuId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The menu still has submenus. Move or delete them first." })
        };
}
