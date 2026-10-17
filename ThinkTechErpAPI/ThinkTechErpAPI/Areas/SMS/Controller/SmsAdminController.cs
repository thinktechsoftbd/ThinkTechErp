using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpService.Sms;
using ThinkTechErpAPI.Security;

namespace ThinkTechErpAPI.Areas.SMS.Controller;

[ApiController]
[Route("api/sms/admin")]
[Authorize(Policy = SmsSecurityPolicies.Admin)]
[EnableRateLimiting(SmsSecurityPolicies.RateLimit)]
public sealed class SmsAdminController(ISmsService smsService) : ControllerBase
{
    [HttpGet("balance")]
    [ProducesResponseType(typeof(SmsPlatformBalance), StatusCodes.Status200OK)]
    public async Task<ActionResult<SmsPlatformBalance>> GetPlatformBalance(CancellationToken cancellationToken)
        => Ok(await smsService.GetPlatformBalanceAsync(cancellationToken));

    [HttpGet("clients")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsClient>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SmsClient>>> GetClients(
        [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await smsService.GetClientsAsync(limit, offset, cancellationToken));

    [HttpGet("clients/{clientId}")]
    [ProducesResponseType(typeof(SmsClient), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SmsClient>> GetClient(string clientId, CancellationToken cancellationToken)
    {
        var client = await smsService.GetClientAsync(clientId, cancellationToken);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpPost("clients")]
    [ProducesResponseType(typeof(SmsClient), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SmsClient>> CreateClient(
        [FromBody] SmsClientCreateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await smsService.CreateClientAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetClient), new { clientId = client.ClientId }, client);
        }
        catch (SmsConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPatch("clients/{clientId}")]
    [ProducesResponseType(typeof(SmsClient), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SmsClient>> UpdateClient(
        string clientId,
        [FromBody] SmsClientUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await smsService.UpdateClientAsync(clientId, request, cancellationToken);
            return client is null ? NotFound() : Ok(client);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("purchases")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsAdnPurchase>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SmsAdnPurchase>>> GetPurchases(
        [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await smsService.GetPurchasesAsync(limit, offset, cancellationToken));

    [HttpPost("purchases")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordPurchase(
        [FromBody] SmsPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var transactionId = await smsService.RecordPurchaseAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetPurchases), new { limit = 100, offset = 0 }, new { transactionId });
        }
        catch (SmsConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpGet("allocations")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsClientAllocation>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SmsClientAllocation>>> GetAllocations(
        [FromQuery] string? clientId,
        [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await smsService.GetAllocationsAsync(clientId, limit, offset, cancellationToken));

    [HttpPost("clients/{clientId}/allocations")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Allocate(
        string clientId,
        [FromBody] SmsAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var client = await smsService.GetClientAsync(clientId, cancellationToken);
        if (client is null)
        {
            return NotFound(new { error = "SMS client was not found." });
        }

        try
        {
            var transactionId = await smsService.AllocateAsync(
                clientId, client.DisplayName, request, cancellationToken);
            return CreatedAtAction(nameof(GetAllocations), new { clientId, limit = 100, offset = 0 },
                new { transactionId, clientId, request.SmsQuantity });
        }
        catch (SmsConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpGet("transactions")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsBalanceTransaction>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SmsBalanceTransaction>>> GetTransactions(
        [FromQuery] string? clientId,
        [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await smsService.GetTransactionsAsync(clientId, limit, offset, cancellationToken));
}
