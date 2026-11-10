using ThinkTechErpDomain.Models.Storage;

namespace ThinkTechErpService.Storage;

/// <summary>An open file: its details and a stream the caller disposes.</summary>
public sealed record StoredFileContent(StoredFile File, Stream Content);

public interface IFileStorageService
{
    /// <summary>
    /// Stores an uploaded file at {organization slug}/{folder}/{50 random characters}{its extension}. An
    /// organization without a slug is given one from its English name first.
    /// </summary>
    /// <param name="folder">product, organization or user (<see cref="FileStorageRules.Folders"/>).</param>
    /// <exception cref="ThinkTechErpDomain.Models.Common.BusinessRuleException">
    /// The folder, file type or size isn't allowed, or the organization's slug can't name a folder.
    /// </exception>
    Task<StoredFile> SaveAsync(int organizationId, string folder, string originalFileName, Stream content, long length,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file for reading, or returns null when there is none at that address.</summary>
    StoredFileContent? Open(string organizationSlug, string folder, string fileName);

    /// <summary>Deletes a stored file, when it is in the folder of the organization the request works in.</summary>
    Task<FileDeleteOutcome> DeleteAsync(int organizationId, string organizationSlug, string folder, string fileName,
        CancellationToken cancellationToken = default);
}
