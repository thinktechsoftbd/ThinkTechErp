using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// Inventory and Purchase data belongs to one organization. This filter picks it from the
/// X-Organization-Id header (or the user's default organization), checks the user may work in it, and
/// keeps it for the action (HttpContext.OrganizationId()). Answers 403 when the user has none.
/// </summary>
public sealed class OrganizationScopedAttribute() : TypeFilterAttribute(typeof(OrganizationScopeFilter));

public sealed class OrganizationScopeFilter(IUserOrganizationService organizations) : IAsyncActionFilter
{
    public const string HeaderName = "X-Organization-Id";
    private const string ItemKey = "OrganizationId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!user.TryGetUserId(out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        int? requested = null;
        if (context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header))
        {
            if (!int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                context.Result = new BadRequestObjectResult(new { error = $"{HeaderName} must be an organization id." });
                return;
            }
            requested = parsed;
        }

        var canManageOrganizations = user.HasClaim(SecurityPolicies.PermissionClaim, SecurityRules.ManageOrganizationPermission);
        int? organizationId;
        try
        {
            organizationId = await organizations.ResolveAsync(userId, canManageOrganizations, requested,
                context.HttpContext.RequestAborted);
        }
        catch (BusinessRuleException exception)
        {
            context.Result = new ObjectResult(new { error = exception.Message }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        if (organizationId is null)
        {
            context.Result = new ObjectResult(new
            {
                error = "Your account isn't linked to an organization yet. Ask an administrator to add you to one."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        context.HttpContext.Items[ItemKey] = organizationId.Value;
        await next();
    }

    public static int Get(HttpContext httpContext)
        => httpContext.Items[ItemKey] is int organizationId
            ? organizationId
            : throw new InvalidOperationException("The action has no [OrganizationScoped] attribute.");
}

public static class OrganizationScopeExtensions
{
    /// <summary>The organization the request works in (set by [OrganizationScoped]).</summary>
    public static int OrganizationId(this HttpContext httpContext) => OrganizationScopeFilter.Get(httpContext);
}

/// <summary>Shared plumbing for organization-scoped controllers.</summary>
[ApiController]
[OrganizationScoped]
[SecurityExceptionFilter]
public abstract class OrganizationControllerBase : ControllerBase
{
    protected int OrganizationId => HttpContext.OrganizationId();

    protected int UserId => User.TryGetUserId(out var userId)
        ? userId
        : throw new BusinessRuleException("The access token has no user.");

    protected ActionResult DeleteResult(DeleteOutcome outcome, string inUseMessage) => outcome switch
    {
        DeleteOutcome.Deleted => NoContent(),
        DeleteOutcome.NotFound => NotFound(),
        _ => Conflict(new { error = inUseMessage })
    };

    protected ActionResult<T> OkOrNotFound<T>(T? value) where T : class
        => value is null ? NotFound() : Ok(value);
}
