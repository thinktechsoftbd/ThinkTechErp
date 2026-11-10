using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Storage;
using ThinkTechErpService.Storage;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>
/// Files a record links to: a product's image_url, an organization's logo_url and a user's photoUrl. They are
/// stored at {FileStorage:RootPath}/{organization slug}/{product|organization|user}/{50 random characters}.{extension}
/// (/srv/storage/public/... on the server). Any signed-in user may upload to, and delete from, the organization
/// they work in (X-Organization-Id, else their default); no permission is needed. Reading needs no sign-in, so
/// a file's URL works in an &lt;img&gt; tag; the random names keep files from being found. Uploading doesn't
/// change any record: save the returned url in the record's column through its usual endpoint.
/// </summary>
[Route("api/files")]
public sealed class FileStorageController(IFileStorageService storage) : ErpControllerBase
{
    // Hard cap on the whole multipart request; FileStorage:MaxFileSizeMb is the limit for the file itself.
    private const long MaxRequestBytes = (FileStorageService.MaxFileSizeMbLimit + 1) * 1024L * 1024L;

    private const string NoFileError = "There is no file at this address.";

    /// <summary>
    /// Uploads a file (multipart/form-data, field "file") for <c>folder</c>: product, organization or user. It
    /// gets a new random name and keeps its extension. The response's url is what to save in the record.
    /// </summary>
    [HttpPost("{folder}")]
    [OrganizationScoped]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType(typeof(StoredFile), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StoredFile>> UploadFile(string folder,
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken)
    {
        await using var content = request.File.OpenReadStream();
        var stored = await storage.SaveAsync(
            HttpContext.OrganizationId(), folder, request.File.FileName, content, request.File.Length, cancellationToken);

        var address = new { organizationSlug = stored.OrganizationSlug, folder = stored.Folder, fileName = stored.FileName };
        stored.Url ??= Url.ActionLink(nameof(ReadFile), values: address);
        return CreatedAtAction(nameof(ReadFile), address, stored);
    }

    /// <summary>Returns a stored file. No sign-in needed. Supports Range requests.</summary>
    [HttpGet("{organizationSlug}/{folder}/{fileName}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult ReadFile(string organizationSlug, string folder, string fileName)
    {
        var file = storage.Open(organizationSlug, folder, fileName);
        if (file is null)
            return NotFound(new { error = NoFileError });

        // Stored files are served as data; never let one run as a page on the API's origin.
        Response.Headers.ContentSecurityPolicy = "sandbox";
        // Names are never reused, so an address always holds the same file; a deleted one may stay cached this long.
        Response.Headers.CacheControl = "public, max-age=604800";
        var result = File(file.Content, file.File.ContentType, enableRangeProcessing: true);
        result.LastModified = new DateTimeOffset(file.File.LastModified, TimeSpan.Zero);
        return result;
    }

    /// <summary>
    /// Deletes a stored file in the organization the request works in. It doesn't change the record that
    /// links to it: clear or replace that column through the record's endpoint.
    /// </summary>
    [HttpDelete("{organizationSlug}/{folder}/{fileName}")]
    [OrganizationScoped]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(
        string organizationSlug, string folder, string fileName, CancellationToken cancellationToken)
        => await storage.DeleteAsync(HttpContext.OrganizationId(), organizationSlug, folder, fileName, cancellationToken) switch
        {
            FileDeleteOutcome.Deleted => NoContent(),
            FileDeleteOutcome.OtherOrganization => StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "The file belongs to another organization. Switch to that organization to delete it."
            }),
            _ => NotFound(new { error = NoFileError })
        };
}

public sealed class FileUploadRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}
