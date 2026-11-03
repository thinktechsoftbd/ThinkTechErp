using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpService.Accounting;

namespace ThinkTechErpAPI.Areas.Accounting.Controller;

/// <summary>
/// The chart of accounts tree on the acc_* tables, as in MediDesk: Parent Class → Account Class →
/// Main Class → Map Class → Sub Class. One chart for every organization; parent and account classes are fixed,
/// main, map and sub classes are added (with the next code), renamed and deleted. kind is main, map or sub.
/// </summary>
[Route("api/accounting/account-classes")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingAccountClassesController(IAccountClassService accountClasses) : ErpControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountClassNode>>> GetTree(CancellationToken cancellationToken)
        => Ok(await accountClasses.GetTreeAsync(cancellationToken));

    /// <summary>Adds a main class under an account class, a map class under a main class, or a sub class under a map class.</summary>
    [HttpPost]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<AccountClassNode>> Create([FromBody] AccountClassCreateRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await accountClasses.CreateAsync(request, cancellationToken));

    [HttpPut("{kind}/{id:int}")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<AccountClassNode>> Rename(
        [StringLength(10)] string kind, int id, [FromBody] AccountClassRenameRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await accountClasses.RenameAsync(kind, id, request, cancellationToken));

    [HttpDelete("{kind}/{id:int}")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<IActionResult> Delete([StringLength(10)] string kind, int id, CancellationToken cancellationToken)
        => DeleteResult(await accountClasses.DeleteAsync(kind, id, cancellationToken),
            $"Cannot delete: this {AccountClassKind.Label(kind)} is used in vouchers.");
}
