using ThinkTechErpDomain.Models.Storage;

namespace ThinkTechErpService.Storage;

/// <summary>An open file: its details and a stream the caller disposes.</summary>
public sealed record StoredFileContent(StoredFile File, Stream Content);

public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file to the storage API under its folder's partial path (/product/, /organization/ or /user/). The
    /// storage API names it {random id}{its extension}.
    /// </summary>
    /// <param name="folder">product, organization or user (<see cref="FileStorageRules.PartialPaths"/>).</param>
    /// <param name="content">A seekable stream (an upload's), so it can be sent again with a renewed token.</param>
    /// <exception cref="ThinkTechErpDomain.Models.Common.BusinessRuleException">The folder, file type or size isn't allowed.</exception>
    /// <exception cref="FileStorageUnavailableException">The storage API can't be reached or refused this API.</exception>
    Task<StoredFile> SaveAsync(string folder, string originalFileName, Stream content, long length,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file for reading, or returns null when there is none at that address.</summary>
    /// <exception cref="FileStorageUnavailableException">The storage API can't be reached or refused this API.</exception>
    Task<StoredFileContent?> OpenAsync(string folder, string fileName, CancellationToken cancellationToken = default);
}
