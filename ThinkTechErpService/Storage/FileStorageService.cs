using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Storage;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Storage;

/// <summary>
/// Files at {RootPath}/{organization slug}/{folder}/{name}. The folder is product, organization or user, and
/// the name is 50 random characters (a-z, 0-9) and the uploaded file's extension, so callers never choose a
/// name and none can be guessed. Every part is checked against a strict pattern and every resolved path must
/// stay inside RootPath. Writes go to a temporary file first and are moved into place, so a half-written file
/// is never visible.
/// </summary>
public sealed partial class FileStorageService : IFileStorageService
{
    /// <summary>Upper bound for FileStorage:MaxFileSizeMb; the upload endpoint caps requests near it.</summary>
    public const int MaxFileSizeMbLimit = 50;

    public static readonly IReadOnlyList<string> DefaultExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".ico",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt"
    ];

    private const string NameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789";

    // public.organization.organization_slug is varchar(100); a made slug leaves room for the "-{id}" that
    // EnsureOrganizationSlugAsync adds when another organization has it.
    private const int MaxMadeSlugLength = 88;

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly IOrganizationRepository organizations;
    private readonly string root;
    private readonly string rootWithSeparator;
    private readonly string? publicBaseUrl;
    private readonly int maxFileSizeMb;
    private readonly HashSet<string> allowedExtensions;

    public FileStorageService(
        IOptions<FileStorageOptions> options, IHostEnvironment environment, IOrganizationRepository organizations)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.RootPath))
            throw new InvalidOperationException($"{FileStorageOptions.SectionName}:RootPath must be set.");
        if (settings.MaxFileSizeMb is < 1 or > MaxFileSizeMbLimit)
            throw new InvalidOperationException(
                $"{FileStorageOptions.SectionName}:MaxFileSizeMb must be between 1 and {MaxFileSizeMbLimit}.");

        this.organizations = organizations;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(environment.ContentRootPath, settings.RootPath)));
        rootWithSeparator = root + Path.DirectorySeparatorChar;
        publicBaseUrl = string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ? null : settings.PublicBaseUrl.Trim().TrimEnd('/');
        maxFileSizeMb = settings.MaxFileSizeMb;
        allowedExtensions = (settings.AllowedExtensions is { Length: > 0 } configured ? configured : DefaultExtensions)
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => "." + extension.Trim().TrimStart('.').ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<StoredFile> SaveAsync(
        int organizationId, string folder, string originalFileName, Stream content, long length,
        CancellationToken cancellationToken = default)
    {
        if (!FileStorageRules.Folders.Contains(folder))
            throw new BusinessRuleException(
                $"Files can be stored for {string.Join(", ", FileStorageRules.Folders.Order())}, not \"{folder}\".");

        var extension = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
        if (!ExtensionPattern().IsMatch(extension) || !allowedExtensions.Contains(extension))
            throw new BusinessRuleException(
                $"Files of type \"{(extension.Length > 0 ? extension : "(none)")}\" can't be stored. " +
                $"Allowed: {string.Join(", ", allowedExtensions.Order())}.");

        var maxBytes = maxFileSizeMb * 1024L * 1024L;
        if (length <= 0)
            throw new BusinessRuleException("The file is empty.");
        if (length > maxBytes)
            throw new BusinessRuleException($"The file is larger than {maxFileSizeMb} MB.");

        var slug = await GetOrMakeSlugAsync(organizationId, cancellationToken);
        var directory = ToFullPath(slug, folder);
        Directory.CreateDirectory(directory);

        string fileName;
        string fullPath;
        do
        {
            fileName = RandomNumberGenerator.GetString(NameCharacters, FileStorageRules.FileNameLength) + extension;
            fullPath = Path.Combine(directory, fileName);
        }
        while (File.Exists(fullPath));

        var tempPath = Path.Combine(directory, $".upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, FileOptions.Asynchronous))
            {
                await CopyWithLimitAsync(content, target, maxBytes, cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return Describe(slug, folder, new FileInfo(fullPath));
    }

    public StoredFileContent? Open(string organizationSlug, string folder, string fileName)
    {
        if (!IsStoredFileAddress(organizationSlug, folder, fileName))
            return null;

        var info = new FileInfo(ToFullPath(organizationSlug, folder, fileName));
        if (!info.Exists)
            return null;

        var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new StoredFileContent(Describe(organizationSlug, folder, info), stream);
    }

    public async Task<FileDeleteOutcome> DeleteAsync(
        int organizationId, string organizationSlug, string folder, string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!IsStoredFileAddress(organizationSlug, folder, fileName))
            return FileDeleteOutcome.NotFound;

        var organization = await organizations.GetOrganizationSlugAsync(organizationId, cancellationToken);
        if (!string.Equals(organization?.Slug, organizationSlug, StringComparison.Ordinal))
            return FileDeleteOutcome.OtherOrganization;

        var fullPath = ToFullPath(organizationSlug, folder, fileName);
        if (!File.Exists(fullPath))
            return FileDeleteOutcome.NotFound;

        File.Delete(fullPath);
        return FileDeleteOutcome.Deleted;
    }

    private async Task<string> GetOrMakeSlugAsync(int organizationId, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetOrganizationSlugAsync(organizationId, cancellationToken)
            ?? throw new BusinessRuleException("The organization doesn't exist.");

        // The API doesn't set slugs when it creates organizations, so the first file gives one from the English
        // name, made the way the existing ones are ("M/S Akhi Enterprise" -> "m-s-akhi-enterprise").
        var slug = organization.Slug
            ?? await organizations.EnsureOrganizationSlugAsync(organizationId, MakeSlug(organization.NameEn), cancellationToken)
            ?? throw new InvalidOperationException($"Organization {organizationId} still has no slug.");

        if (!SlugPattern().IsMatch(slug))
            throw new BusinessRuleException(
                $"The organization's slug \"{slug}\" can't name a folder: use letters, digits, '-' and '_', starting with a letter or digit.");

        return slug;
    }

    private static string MakeSlug(string name)
    {
        var slug = NonSlugCharacters().Replace(name.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > MaxMadeSlugLength)
            slug = slug[..MaxMadeSlugLength].TrimEnd('-');

        return slug.Length > 0 ? slug : "organization";
    }

    private static bool IsStoredFileAddress(string organizationSlug, string folder, string fileName)
        => SlugPattern().IsMatch(organizationSlug)
           && FileStorageRules.Folders.Contains(folder)
           && StoredNamePattern().IsMatch(fileName);

    private string ToFullPath(params string[] parts)
    {
        var fullPath = Path.GetFullPath(Path.Combine([root, .. parts]));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootWithSeparator, comparison))
            throw new BusinessRuleException("The path must stay inside the storage folder.");

        return fullPath;
    }

    private StoredFile Describe(string slug, string folder, FileInfo info) => new()
    {
        // Every part is URL-safe (checked by the patterns), so the address needs no escaping.
        Path = $"{slug}/{folder}/{info.Name}",
        Url = publicBaseUrl is null ? null : $"{publicBaseUrl}/{slug}/{folder}/{info.Name}",
        OrganizationSlug = slug,
        Folder = folder,
        FileName = info.Name,
        Size = info.Length,
        ContentType = ContentTypes.TryGetContentType(info.Name, out var contentType) ? contentType : "application/octet-stream",
        LastModified = info.LastWriteTimeUtc
    };

    // The declared length can be wrong, so the copy itself enforces the limit.
    private static async Task CopyWithLimitAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new BusinessRuleException($"The file is larger than {maxBytes / (1024 * 1024)} MB.");

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")]
    private static partial Regex SlugPattern();

    // FileStorageRules.FileNameLength characters, then the extension.
    [GeneratedRegex(@"^[a-z0-9]{50}\.[a-z0-9]{1,10}$")]
    private static partial Regex StoredNamePattern();

    [GeneratedRegex(@"^\.[a-z0-9]{1,10}$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();
}
