using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// Picks the organization a storefront request works in and keeps it for the action: a customer token's org
/// claim (and its customer), or the organization of the integration client (client_id claim →
/// security_organization_client.org_object_id, cached briefly). 401 without either, 403 when the client has
/// no active organization.
/// </summary>
public sealed class ThinkCartScopedAttribute() : TypeFilterAttribute(typeof(ThinkCartScopeFilter));

public sealed class ThinkCartScopeFilter(IThinkCartOrganizationResolver organizations) : IAsyncActionFilter
{
    private const string OrganizationKey = "ThinkCartOrganizationId";
    private const string CustomerKey = "ThinkCartCustomerId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.HasClaim(ThinkCartPolicies.TokenUseClaim, ThinkCartPolicies.CustomerTokenUse))
        {
            if (!int.TryParse(user.FindFirst(ThinkCartPolicies.OrganizationClaim)?.Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var organizationId) ||
                !user.TryGetUserId(out var customerId))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            context.HttpContext.Items[OrganizationKey] = organizationId;
            context.HttpContext.Items[CustomerKey] = customerId;
        }
        else
        {
            var clientId = user.FindFirst(IntegrationPolicies.ClientIdClaim)?.Value;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var organizationId = await organizations.ResolveClientOrganizationAsync(
                clientId, context.HttpContext.RequestAborted);
            if (organizationId is null)
            {
                context.Result = new ObjectResult(new
                {
                    error = "This API client isn't linked to an active organization. Set its organization in Security → Clients."
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            context.HttpContext.Items[OrganizationKey] = organizationId.Value;
        }

        await next();
    }

    public static int GetOrganizationId(HttpContext httpContext)
        => httpContext.Items[OrganizationKey] is int organizationId
            ? organizationId
            : throw new InvalidOperationException("The action has no [ThinkCartScoped] attribute.");

    public static int? GetCustomerId(HttpContext httpContext)
        => httpContext.Items[CustomerKey] is int customerId ? customerId : null;
}

/// <summary>
/// Answers an invalid request body or query with the ERP error shape { error } instead of a validation
/// problem document. Runs before [ApiController]'s own model-state check (order -2000).
/// </summary>
public sealed class ThinkCartModelStateAttribute : ActionFilterAttribute
{
    public ThinkCartModelStateAttribute() => Order = -3000;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
            return;

        var (field, error) = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => (Field: entry.Key, Error: entry.Value!.Errors[0]))
            .First();
        var message = string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? $"The value of {field} is invalid."
            : error.ErrorMessage;
        context.Result = new BadRequestObjectResult(new { error = message });
    }
}

/// <summary>Shared plumbing for the ThinkCart controllers: the organization, the signed-in customer and { error } results.</summary>
[ApiController]
[SecurityExceptionFilter]
[ThinkCartScoped]
[ThinkCartModelState]
public abstract class ThinkCartControllerBase : ControllerBase
{
    /// <summary>The organization whose catalogue, stock and customers the request uses.</summary>
    protected int OrganizationId => ThinkCartScopeFilter.GetOrganizationId(HttpContext);

    /// <summary>The signed-in customer (supplier_id), or null for a storefront (integration) token.</summary>
    protected int? CustomerId => ThinkCartScopeFilter.GetCustomerId(HttpContext);

    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected NotFoundObjectResult NotFoundError(string message) => NotFound(new { error = message });
}
