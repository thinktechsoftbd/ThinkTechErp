namespace ThinkTechErpService.Storage;

/// <summary>The FileStorage configuration section: the ThinkTech storage API files are kept in.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>The storage API's address, e.g. https://storage.thinktechsoft.com.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>This API's client at the storage API (it needs the storage.read and storage.write scopes).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client's secret. Keep it out of appsettings: use user-secrets in development and the
    /// FileStorage__ClientSecret environment variable on the server.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Address files are read from, e.g. https://api.example.com/api/files. Empty: this API's own api/files, on the
    /// address the upload came in on.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Largest file accepted, in MB (1 to <see cref="FileStorageService.MaxFileSizeMbLimit"/>).</summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>Extensions that may be uploaded, e.g. ".jpg". Empty means <see cref="FileStorageService.DefaultExtensions"/>.</summary>
    public string[]? AllowedExtensions { get; set; }
}
