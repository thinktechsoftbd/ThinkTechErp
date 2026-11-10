namespace ThinkTechErpDomain.Models.Storage;

public static class FileStorageRules
{
    /// <summary>Files for public.product.image_url.</summary>
    public const string ProductFolder = "product";

    /// <summary>Files for public.organization.logo_url.</summary>
    public const string OrganizationFolder = "organization";

    /// <summary>Files for public."User"."photoUrl".</summary>
    public const string UserFolder = "user";

    /// <summary>The folders under an organization's slug that files can be stored in.</summary>
    public static readonly IReadOnlySet<string> Folders = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductFolder, OrganizationFolder, UserFolder
    };

    /// <summary>A stored file's name is this many random characters (a-z, 0-9), then its extension.</summary>
    public const int FileNameLength = 50;
}

/// <summary>A stored file, at {organization slug}/{folder}/{file name} under the storage folder.</summary>
public sealed class StoredFile
{
    /// <summary>{organization slug}/{folder}/{file name}, e.g. thinktech/product/k3x...q9.jpg.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Where the file is read from. Save it in the record's column (image_url, logo_url or photoUrl).</summary>
    public string? Url { get; set; }

    public string OrganizationSlug { get; set; } = string.Empty;

    /// <summary>product, organization or user.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>The stored name: 50 random characters and the uploaded file's extension.</summary>
    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public enum FileDeleteOutcome
{
    Deleted,
    NotFound,

    /// <summary>The file is in another organization's folder than the one the request works in.</summary>
    OtherOrganization
}

/// <summary>An organization's English name and its slug (public.organization.organization_slug).</summary>
public sealed class OrganizationSlug
{
    public int OrganizationId { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string? Slug { get; set; }
}
