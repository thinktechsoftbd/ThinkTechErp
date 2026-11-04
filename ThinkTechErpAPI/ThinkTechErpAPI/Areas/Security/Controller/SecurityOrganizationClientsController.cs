using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/clients")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityOrganizationClientsController(IOrganizationClientService clientService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrganizationClient>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrganizationClient>>> GetClients(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await clientService.GetClientsAsync(search, isActive, limit, offset, cancellationToken));

    [HttpGet("organizations/options")]
    [ProducesResponseType(typeof(PagedResult<OrganizationClientOrganizationOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrganizationClientOrganizationOption>>> GetOrganizationOptions(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await clientService.GetOrganizationOptionsAsync(search, limit, offset, cancellationToken));

    [HttpGet("{clientId:int}")]
    [ProducesResponseType(typeof(OrganizationClient), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationClient>> GetClient(int clientId, CancellationToken cancellationToken)
    {
        var client = await clientService.GetClientAsync(clientId, cancellationToken);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(OrganizationClient), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationClient>> CreateClient(
        [FromBody] OrganizationClientUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var client = await clientService.CreateClientAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetClient), new { clientId = client.Id }, client);
    }

    [HttpPut("{clientId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(OrganizationClient), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationClient>> UpdateClient(
        int clientId,
        [FromBody] OrganizationClientUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var client = await clientService.UpdateClientAsync(clientId, request, userId, cancellationToken);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpPatch("{clientId:int}/active")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(OrganizationClient), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationClient>> SetClientActive(
        int clientId,
        [FromBody] OrganizationClientActiveRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var client = await clientService.SetClientActiveAsync(clientId, request.IsActive, userId, cancellationToken);
        return client is null ? NotFound() : Ok(client);
    }
}
