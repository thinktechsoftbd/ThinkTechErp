using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Dashboard;
using ThinkTechErpService.Dashboard;

namespace ThinkTechErpAPI.Areas.Dashboard.Controller;

/// <summary>
/// The home dashboard of the organization the request works in. Any signed-in user may ask; each section
/// (sales, purchases, warranty, and their rows in the recent transactions) is filled only when the user may
/// view that module (SALES.V, PURCHASE.V, WARRANTY.V), else it is null.
/// </summary>
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(IDashboardService dashboard, IAuthorizationService authorization)
    : OrganizationControllerBase
{
    /// <summary>
    /// Totals, series and lists for a period: this-month (default), last-month or this-year, each compared with
    /// the same stretch of the period before. <c>today</c> is today in the organization's time zone (default: UTC).
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(DashboardOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DashboardOverview>> GetOverview(
        [FromQuery, StringLength(20)] string? period, [FromQuery] DateOnly? today, CancellationToken cancellationToken)
    {
        var access = new DashboardAccess(
            await CanAsync(SalesPolicies.View),
            await CanAsync(PurchasePolicies.View),
            await CanAsync(WarrantyPolicies.View));
        return Ok(await dashboard.GetOverviewAsync(OrganizationId, period, today, access, cancellationToken));
    }

    private async Task<bool> CanAsync(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;
}
