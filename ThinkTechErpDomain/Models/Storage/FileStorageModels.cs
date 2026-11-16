namespace ThinkTechErpDomain.Models.Storage;

public static class FileStorageRules
{
    /// <summary>Files for public.product.image_url.</summary>
    public const string ProductFolder = "product";

    /// <summary>Files for public.organization.logo_url.</summary>
    public const string OrganizationFolder = "organization";

    /// <summary>Files for public."User"."photoUrl".</summary>
    public const string UserFolder = "user";

    /// <summary>
    /// The folder (FilePartialPath) each kind of file is uploaded to in the ThinkTech storage API. Fixed here, not
    /// in the settings, so a file's address never changes with the configuration.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PartialPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ProductFolder] = "/product/",
        [OrganizationFolder] = "/organization/",
        [UserFolder] = "/user/"
    };
}

/// <summary>A file kept in the ThinkTech storage API, at {folder}/{file name}.</summary>
public sealed class StoredFile
{
    /// <summary>{folder}/{file name}, the storage API's filePath, e.g. product/7964b4d0...832.jpg.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Where the file is read from. Save it in the record's column (image_url, logo_url or photoUrl).</summary>
    public string? Url { get; set; }

    /// <summary>product, organization or user.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>The stored name: the storage API's id (32 hex characters) and the uploaded file's extension.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The name the file was uploaded with; the storage API doesn't keep it.</summary>
    public string? OriginalFileName { get; set; }

    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime? UploadedAt { get; set; }
}

/// <summary>
/// The storage API can't be reached, or refused this API (wrong FileStorage settings). Answered as 503: it is not
/// the caller's mistake, and trying again later may work.
/// </summary>
public sealed class FileStorageUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
