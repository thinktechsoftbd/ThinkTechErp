using Microsoft.AspNetCore.Mvc;
using ThinkTechErpDomain.Models;
using ThinkTechErpService.Website;

namespace ThinkTechErpAPI.Areas.Website.controller;

[ApiController]
[Route("api/website/{table}")]
public sealed class WebsiteTablesController(IWebsiteService websiteService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebsiteRecord>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebsiteRecord>>> GetAll(
        string table,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await websiteService.GetAllAsync(table, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WebsiteRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WebsiteRecord>> GetById(
        string table,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await websiteService.GetByIdAsync(table, id, cancellationToken);
            return record is null ? NotFound() : Ok(record);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(WebsiteRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WebsiteRecord>> Create(
        string table,
        [FromBody] WebsiteRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await websiteService.CreateAsync(table, record, cancellationToken);
            return CreatedAtAction(nameof(GetAll), new { table }, created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(WebsiteRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WebsiteRecord>> Update(
        string table,
        string id,
        [FromBody] WebsiteRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await websiteService.UpdateAsync(table, id, record, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string table,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await websiteService.DeleteAsync(table, id, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
