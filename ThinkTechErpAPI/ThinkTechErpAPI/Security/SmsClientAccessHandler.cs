using Microsoft.AspNetCore.Authorization;

namespace ThinkTechErpAPI.Security;

public sealed class SmsClientAccessHandler : AuthorizationHandler<SmsClientAccessRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SmsClientAccessRequirement requirement)
    {
        if (context.User.IsInRole("sms_admin"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.IsInRole("sms_client") &&
            context.Resource is HttpContext httpContext &&
            httpContext.Request.RouteValues.TryGetValue("clientId", out var routeClientId))
        {
            var tokenClientId = context.User.FindFirst("client_id")?.Value;
            if (!string.IsNullOrWhiteSpace(tokenClientId) &&
                string.Equals(tokenClientId, routeClientId?.ToString(), StringComparison.Ordinal))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
