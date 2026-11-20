using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpDomain.Models.Storage;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// Maps service errors (security, inventory, purchase) to 400 (validation) and 409 (conflict), file storage
/// that can't be reached or refuses this API to 503, and an SMS gateway that fails to 502, with an { error } body.
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
            FileStorageUnavailableException exception => new ObjectResult(new { error = exception.Message })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            },
            SmsGatewayException exception => new ObjectResult(new { error = exception.Message })
            {
                StatusCode = StatusCodes.Status502BadGateway
            },
            _ => null
        };
        context.ExceptionHandled = context.Result is not null;
    }
}
