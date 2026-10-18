using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Security;

/// <summary>Maps security service errors to 400 (validation) and 409 (conflict) with an { error } body.</summary>
public sealed class SecurityExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            SecurityValidationException exception => new BadRequestObjectResult(new { error = exception.Message }),
            SecurityConflictException exception => new ConflictObjectResult(new { error = exception.Message }),
            _ => null
        };
        context.ExceptionHandled = context.Result is not null;
    }
}
