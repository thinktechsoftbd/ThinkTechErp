using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ThinkTechErpDomain.Models.Storage;

namespace ThinkTechErpService.Storage;

/// <summary>
/// This API's access token for the storage API (POST api/v1/auth/token with the client id and secret), shared by
/// every request and renewed a minute before it expires, or as soon as the storage API stops accepting it.
/// </summary>
public sealed class FileStorageTokenCache(ILogger<FileStorageTokenCache> logger)
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim gate = new(1, 1);

    // One reference, so readers outside the lock always see a token with its own renewal time.
    private CachedToken? current;

    /// <param name="rejected">A token the storage API just refused: renewed unless another request already has.</param>
    public async Task<string> GetAsync(
        HttpClient http, FileStorageOptions settings, string? rejected, CancellationToken cancellationToken)
    {
        if (Usable(rejected) is { } token)
            return token;

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Usable(rejected) is { } renewed)
                return renewed;

            var issued = await RequestAsync(http, settings, cancellationToken);
            current = issued;
            return issued.Token;
        }
        finally
        {
            gate.Release();
        }
    }

    private string? Usable(string? rejected)
        => current is { } cached && cached.Token != rejected && DateTimeOffset.UtcNow < cached.RenewAt ? cached.Token : null;

    private async Task<CachedToken> RequestAsync(HttpClient http, FileStorageOptions settings, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync("api/v1/auth/token", JsonContent.Create(new Dictionary<string, string>
            {
                ["ClientId"] = settings.ClientId,
                ["ClientSecret"] = settings.ClientSecret
            }), cancellationToken);
        }
        catch (Exception exception) when (FileStorageService.IsUnreachable(exception, cancellationToken))
        {
            logger.LogWarning(exception, "The storage API at {BaseUrl} can't be reached for a token.", http.BaseAddress);
            throw new FileStorageUnavailableException(FileStorageService.UnreachableMessage, exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            {
                logger.LogError("The storage API refused client {ClientId}: check FileStorage:ClientId and ClientSecret.",
                    settings.ClientId);
                throw new FileStorageUnavailableException(FileStorageService.NotSetUpMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("The storage API answered {Status} to a token request.", (int)response.StatusCode);
                throw new FileStorageUnavailableException(FileStorageService.UnreachableMessage);
            }

            TokenResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            }
            catch (JsonException exception)
            {
                logger.LogError(exception, "The storage API's token response isn't JSON.");
                throw new FileStorageUnavailableException(FileStorageService.UnreachableMessage, exception);
            }

            if (string.IsNullOrEmpty(body?.AccessToken))
            {
                logger.LogError("The storage API's token response has no access_token.");
                throw new FileStorageUnavailableException(FileStorageService.UnreachableMessage);
            }

            var lifetime = TimeSpan.FromSeconds(Math.Max(body.ExpiresIn, 0));
            var renewIn = lifetime > RenewBefore * 2 ? lifetime - RenewBefore : lifetime / 2;
            return new CachedToken(body.AccessToken, DateTimeOffset.UtcNow + renewIn);
        }
    }

    private sealed record CachedToken(string Token, DateTimeOffset RenewAt);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        /// <summary>Seconds.</summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
