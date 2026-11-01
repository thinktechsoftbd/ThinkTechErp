using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// Maps service errors (security, inventory, purchase) to 400 (validation) and 409 (conflict) with an
/// { error } body.
/// </summary>
public sealed class SecurityExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            SecurityValidationException exception => new BadRequestObjectResult(new { error = exception.Message }),
            SecurityConflictException exception => new ConflictObjectResult(new { error = exception.Message }),
            BusinessRuleException exception => new BadRequestObjectResult(new { error = exception.Message }),
            BusinessConflictException exception => new ConflictObjectResult(new { error = exception.Message }),
            _ => null
        };
        context.ExceptionHandled = context.Result is not null;
    }
}
