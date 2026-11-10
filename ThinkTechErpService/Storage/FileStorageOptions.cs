namespace ThinkTechErpService.Storage;

/// <summary>The FileStorage configuration section.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Folder that holds every organization's files (/srv/storage/public on the server). A relative path is
    /// resolved against the API's content root (Development uses a local folder this way).
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Address RootPath is read from, e.g. https://api.example.com/api/files, or a site that serves the folder
    /// directly. Empty: this API's own api/files, on the address the upload came in on.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Largest file accepted, in MB (1 to <see cref="FileStorageService.MaxFileSizeMbLimit"/>).</summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>Extensions that may be uploaded, e.g. ".jpg". Empty means <see cref="FileStorageService.DefaultExtensions"/>.</summary>
    public string[]? AllowedExtensions { get; set; }
}
