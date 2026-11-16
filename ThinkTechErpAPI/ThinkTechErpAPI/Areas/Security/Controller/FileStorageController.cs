using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Storage;
using ThinkTechErpService.Storage;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>
/// Files a record links to: a product's image_url, an organization's logo_url and a user's photoUrl. They are kept
/// in the ThinkTech storage API (FileStorage settings) under /product/, /organization/ or /user/, with the storage
/// API's random name. Any signed-in user may upload. Reading needs no sign-in, so a file's URL works in an
/// &lt;img&gt; tag: this API fetches the file from the storage API, whose files need a token, and the random names
/// keep files from being found. Uploading doesn't change any record: save the returned url in the record's
/// column through its usual endpoint. Storage that can't be reached or refuses this API answers 503.
/// </summary>
[Route("api/files")]
public sealed class FileStorageController(IFileStorageService storage) : ErpControllerBase
{
    // Hard cap on the whole multipart request; FileStorage:MaxFileSizeMb is the limit for the file itself.
    private const long MaxRequestBytes = (FileStorageService.MaxFileSizeMbLimit + 1) * 1024L * 1024L;

    /// <summary>
    /// Uploads a file (multipart/form-data, field "file") for <c>folder</c>: product, organization or user. The
    /// storage API gives it a new random name and keeps its extension. The response's url is what to save in the record.
    /// </summary>
    [HttpPost("{folder}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType(typeof(StoredFile), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StoredFile>> UploadFile(string folder,
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken)
    {
        await using var content = request.File.OpenReadStream();
        var stored = await storage.SaveAsync(folder, request.File.FileName, content, request.File.Length, cancellationToken);

        var address = new { folder = stored.Folder, fileName = stored.FileName };
        stored.Url ??= Url.ActionLink(nameof(ReadFile), values: address);
        return CreatedAtAction(nameof(ReadFile), address, stored);
    }

    /// <summary>Returns a stored file, fetched from the storage API. No sign-in needed.</summary>
    [HttpGet("{folder}/{fileName}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReadFile(string folder, string fileName, CancellationToken cancellationToken)
    {
        var file = await storage.OpenAsync(folder, fileName, cancellationToken);
        if (file is null)
            return NotFound(new { error = "There is no file at this address." });

        // Stored files are served as data; never let one run as a page on the API's origin.
        Response.Headers.ContentSecurityPolicy = "sandbox";
        // Names are never reused, so an address always holds the same file: browsers may keep it a week.
        Response.Headers.CacheControl = "public, max-age=604800";
        if (file.File.Size > 0)
            Response.ContentLength = file.File.Size;

        var result = File(file.Content, file.File.ContentType);
        if (file.File.UploadedAt is { } uploadedAt)
            result.LastModified = new DateTimeOffset(uploadedAt, TimeSpan.Zero);
        return result;
    }
}

public sealed class FileUploadRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}
