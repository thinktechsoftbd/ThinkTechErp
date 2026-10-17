using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpService.Sms;
using ThinkTechErpAPI.Security;

namespace ThinkTechErpAPI.Areas.SMS.Controller;

[ApiController]
[Route("api/sms/clients/{clientId}")]
[Authorize(Policy = SmsSecurityPolicies.ClientOrAdmin)]
[EnableRateLimiting(SmsSecurityPolicies.RateLimit)]
public sealed class SmsClientController(ISmsService smsService) : ControllerBase
{
    [HttpGet("balance")]
    [ProducesResponseType(typeof(SmsClient), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SmsClient>> GetBalance(string clientId, CancellationToken cancellationToken)
    {
        var client = await smsService.GetClientAsync(clientId, cancellationToken);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpGet("usage")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsUsageRecord>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SmsUsageRecord>>> GetUsage(
        string clientId,
        [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await smsService.GetUsageAsync(clientId, limit, offset, cancellationToken));

    [HttpPost("usage")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordUsage(
        string clientId,
        [FromBody] SmsUsageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var transactionId = await smsService.RecordUsageAsync(clientId, request, cancellationToken);
            return CreatedAtAction(nameof(GetUsage), new { clientId, limit = 100, offset = 0 },
                new { transactionId, clientId, request.SmsQuantity, request.RequestReference });
        }
        catch (SmsConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}
