using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpService.Settings;

namespace ThinkTechErpAPI.Areas.Settings.Controller;

/// <summary>
/// Drop-down values for any signed-in user (e.g. the currency list on the subscription form),
/// without the SETTING.V permission needed to manage lookups.
/// </summary>
[ApiController]
[Route("api/settings/lookup-options")]
[Authorize]
public sealed class SettingsLookupOptionsController(ILookupService lookupService) : ControllerBase
{
    [HttpGet("{lookupTableName}")]
    [ProducesResponseType(typeof(IReadOnlyList<LookupOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LookupOption>>> GetOptions(
        [StringLength(150)] string lookupTableName,
        CancellationToken cancellationToken)
        => Ok(await lookupService.GetOptionsAsync(lookupTableName, cancellationToken));
}
