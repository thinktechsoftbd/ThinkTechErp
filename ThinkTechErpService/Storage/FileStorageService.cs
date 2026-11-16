using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Storage;

namespace ThinkTechErpService.Storage;

/// <summary>
/// Files in the ThinkTech storage API (FileStorage settings). A file is uploaded under its folder's partial path
/// (/product/, /organization/ or /user/), where the storage API names it {random id}.{extension}, and is read back
/// through this API, because the storage API's files need a token. The type and size are checked here first, and
/// only addresses of that shape are ever requested, so no caller can reach anything else in the storage account.
/// </summary>
public sealed partial class FileStorageService(
    HttpClient http,
    FileStorageTokenCache tokens,
    IOptions<FileStorageOptions> options,
    ILogger<FileStorageService> logger) : IFileStorageService
{
    /// <summary>Upper bound for FileStorage:MaxFileSizeMb; the upload endpoint caps requests near it.</summary>
    public const int MaxFileSizeMbLimit = 50;

    public static readonly IReadOnlyList<string> DefaultExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".ico",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt"
    ];

    internal const string UnreachableMessage = "File storage can't be reached right now. Try again in a moment.";
    internal const string NotSetUpMessage =
        "File storage isn't available: an administrator needs to check the API's FileStorage settings.";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public async Task<StoredFile> SaveAsync(
        string folder, string originalFileName, Stream content, long length, CancellationToken cancellationToken = default)
    {
        if (!FileStorageRules.PartialPaths.TryGetValue(folder, out var partialPath))
            throw new BusinessRuleException(
                $"Files can be stored for {string.Join(", ", FileStorageRules.PartialPaths.Keys.Order())}, not \"{folder}\".");

        var settings = Settings();
        var allowedExtensions = AllowedExtensions(settings);
        var fileName = Path.GetFileName(originalFileName ?? string.Empty);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ExtensionPattern().IsMatch(extension) || !allowedExtensions.Contains(extension))
            throw new BusinessRuleException(
                $"Files of type \"{(extension.Length > 0 ? extension : "(none)")}\" can't be stored. " +
                $"Allowed: {string.Join(", ", allowedExtensions.Order())}.");

        if (length <= 0)
            throw new BusinessRuleException("The file is empty.");
        if (length > settings.MaxFileSizeMb * 1024L * 1024L)
            throw new BusinessRuleException($"The file is larger than {settings.MaxFileSizeMb} MB.");
        if (!content.CanSeek)
            throw new ArgumentException("The content must be seekable, so it can be sent again.", nameof(content));

        var start = content.Position;
        using var response = await SendAsync(settings, () =>
        {
            content.Position = start;
            var file = new StreamContent(content);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentTypeOf(fileName));
            return new HttpRequestMessage(HttpMethod.Post, "api/v1/files")
            {
                Content = new MultipartFormDataContent
                {
                    { file, "File", fileName },
                    { new StringContent(partialPath), "FilePartialPath" }
                }
            };
        }, HttpCompletionOption.ResponseContentRead, cancellationToken);

        // The storage API's own checks (type, size, name), in case its settings are stricter than this API's.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge
            or HttpStatusCode.UnsupportedMediaType)
            throw new BusinessRuleException(await ProblemAsync(response, cancellationToken)
                ?? "The file storage didn't accept the file.");
        EnsureAvailable(response, "upload a file");

        UploadedFile? uploaded;
        try
        {
            uploaded = await response.Content.ReadFromJsonAsync<UploadedFile>(cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The storage API's upload response isn't JSON.");
            throw new FileStorageUnavailableException(UnreachableMessage, exception);
        }

        // Only an address this API can read back is worth saving in a record.
        var match = uploaded is null ? null : StoredPathPattern().Match(uploaded.FilePath ?? string.Empty);
        if (uploaded is null || match is not { Success: true } || match.Groups["folder"].Value != folder)
        {
            logger.LogError("The storage API stored an upload for {Folder} at {FilePath}.", folder, uploaded?.FilePath);
            throw new FileStorageUnavailableException(UnreachableMessage);
        }

        var storedName = match.Groups["name"].Value;
        return new StoredFile
        {
            Path = $"{folder}/{storedName}",
            Url = PublicUrl(settings, folder, storedName),
            Folder = folder,
            FileName = storedName,
            OriginalFileName = uploaded.OriginalFileName ?? fileName,
            Size = uploaded.SizeBytes,
            ContentType = ContentTypeOf(storedName),
            UploadedAt = uploaded.UploadedAtUtc?.UtcDateTime
        };
    }

    public async Task<StoredFileContent?> OpenAsync(string folder, string fileName, CancellationToken cancellationToken = default)
    {
        if (!FileStorageRules.PartialPaths.ContainsKey(folder) || !StoredNamePattern().IsMatch(fileName))
            return null;

        var settings = Settings();
        var response = await SendAsync(settings, () => new HttpRequestMessage(HttpMethod.Get, $"api/v1/files/{folder}/{fileName}"),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        try
        {
            EnsureAvailable(response, "read a file");
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var headers = response.Content.Headers;
        var file = new StoredFile
        {
            Path = $"{folder}/{fileName}",
            Url = PublicUrl(settings, folder, fileName),
            Folder = folder,
            FileName = fileName,
            Size = headers.ContentLength ?? 0,
            // From the name, as on upload: never trust a stored file to say what it is.
            ContentType = ContentTypeOf(fileName),
            UploadedAt = headers.LastModified?.UtcDateTime
        };
        // Disposing the stream ends the response and frees its connection.
        return new StoredFileContent(file, await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    internal static bool IsUnreachable(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>
    /// Sends a request with the shared token. A token the storage API no longer accepts (e.g. after its signing key
    /// changed) is renewed and the request sent once more, so <paramref name="createRequest"/> may run twice.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        FileStorageOptions settings, Func<HttpRequestMessage> createRequest, HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        string? rejected = null;
        while (true)
        {
            var token = await tokens.GetAsync(http, settings, rejected, cancellationToken);
            // Not disposed: an upload's content wraps the caller's stream, which the caller disposes.
            var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, completion, cancellationToken);
            }
            catch (Exception exception) when (IsUnreachable(exception, cancellationToken))
            {
                logger.LogWarning(exception, "The storage API at {BaseUrl} can't be reached.", http.BaseAddress);
                throw new FileStorageUnavailableException(UnreachableMessage, exception);
            }

            if (response.StatusCode != HttpStatusCode.Unauthorized || rejected is not null)
                return response;

            response.Dispose();
            rejected = token;
        }
    }

    private FileStorageOptions Settings()
    {
        var settings = options.Value;
        if (http.BaseAddress is null || string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            logger.LogError("File storage isn't configured: set FileStorage:BaseUrl (an http or https address), ClientId and ClientSecret.");
            throw new FileStorageUnavailableException(NotSetUpMessage);
        }

        if (settings.MaxFileSizeMb is < 1 or > MaxFileSizeMbLimit)
        {
            logger.LogError("FileStorage:MaxFileSizeMb must be between 1 and {Limit}.", MaxFileSizeMbLimit);
            throw new FileStorageUnavailableException(NotSetUpMessage);
        }

        return settings;
    }

    private void EnsureAvailable(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
            return;

        // 401 after a fresh token, or 403 (a missing scope): the client needs fixing, not a retry.
        var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        logger.LogError("The storage API answered {Status} when asked to {Action}.", (int)response.StatusCode, action);
        throw new FileStorageUnavailableException(refused ? NotSetUpMessage : UnreachableMessage);
    }

    private static HashSet<string> AllowedExtensions(FileStorageOptions settings)
        => (settings.AllowedExtensions is { Length: > 0 } configured ? configured : DefaultExtensions)
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => "." + extension.Trim().TrimStart('.').ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    // Folder and name match the patterns above, so the address needs no escaping.
    private static string? PublicUrl(FileStorageOptions settings, string folder, string fileName)
        => string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
            ? null
            : $"{settings.PublicBaseUrl.Trim().TrimEnd('/')}/{folder}/{fileName}";

    private static string ContentTypeOf(string fileName)
        => ContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";

    /// <summary>The storage API's problem details: the first field error, else the detail or title.</summary>
    private static async Task<string?> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<StorageProblem>(cancellationToken);
            return problem?.Errors?.Values.SelectMany(messages => messages).FirstOrDefault() ?? problem?.Detail ?? problem?.Title;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The storage API's POST api/v1/files response (the fields this API uses).</summary>
    private sealed class UploadedFile
    {
        public string? FilePath { get; set; }
        public string? OriginalFileName { get; set; }
        public long SizeBytes { get; set; }
        public DateTimeOffset? UploadedAtUtc { get; set; }
    }

    private sealed class StorageProblem
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }

    // The storage API's id is a GUID without dashes; the extension is lowercased on upload.
    [GeneratedRegex(@"^[0-9a-f]{32}\.[a-z0-9]{1,10}$")]
    private static partial Regex StoredNamePattern();

    [GeneratedRegex(@"^(?<folder>product|organization|user)/(?<name>[0-9a-f]{32}\.[a-z0-9]{1,10})$")]
    private static partial Regex StoredPathPattern();

    [GeneratedRegex(@"^\.[a-z0-9]{1,10}$")]
    private static partial Regex ExtensionPattern();
}
