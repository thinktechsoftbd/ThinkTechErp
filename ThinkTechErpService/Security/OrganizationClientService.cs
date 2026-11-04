using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class OrganizationClientService(IOrganizationClientRepository clients) : IOrganizationClientService
{
    private static readonly Regex SecretHashPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ScopePattern = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<PagedResult<OrganizationClient>> GetClientsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => clients.GetClientsAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), isActive, limit, offset, cancellationToken);

    public Task<PagedResult<OrganizationClientOrganizationOption>> GetOrganizationOptionsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
        => clients.GetOrganizationOptionsAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), limit, offset, cancellationToken);

    public Task<OrganizationClient?> GetClientAsync(int clientId, CancellationToken cancellationToken = default)
        => clients.GetClientAsync(clientId, cancellationToken);

    public async Task<OrganizationClient> CreateClientAsync(
        OrganizationClientUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, requireHash: true, excludeClientId: null, cancellationToken);
        var clientId = await SecurityDbErrors.ExecuteAsync(() =>
            clients.CreateClientAsync(request, userId, cancellationToken));
        return await clients.GetClientAsync(clientId, cancellationToken)
            ?? throw new InvalidOperationException("Created API client could not be read back.");
    }

    public async Task<OrganizationClient?> UpdateClientAsync(
        int clientId, OrganizationClientUpsertRequest request, int userId,
        CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, requireHash: false, excludeClientId: clientId, cancellationToken);
        if (!await SecurityDbErrors.ExecuteAsync(() =>
                clients.UpdateClientAsync(clientId, request, userId, cancellationToken)))
            return null;

        return await clients.GetClientAsync(clientId, cancellationToken);
    }

    public async Task<OrganizationClient?> SetClientActiveAsync(
        int clientId, bool isActive, int userId, CancellationToken cancellationToken = default)
    {
        if (!await clients.SetClientActiveAsync(clientId, isActive, userId, cancellationToken))
            return null;
        return await clients.GetClientAsync(clientId, cancellationToken);
    }

    private async Task NormalizeAsync(
        OrganizationClientUpsertRequest request, bool requireHash, int? excludeClientId,
        CancellationToken cancellationToken)
    {
        request.ClientName = request.ClientName.Trim();
        if (!Regex.IsMatch(request.ClientName, "^[A-Za-z0-9][A-Za-z0-9_]{2,63}$", RegexOptions.CultureInvariant))
            throw new SecurityValidationException("Client name must be 3 to 64 letters, numbers or underscores, and start with a letter or number.");

        request.SecretSha256Hash = string.IsNullOrWhiteSpace(request.SecretSha256Hash)
            ? null
            : request.SecretSha256Hash.Trim().ToLowerInvariant();
        if (requireHash && request.SecretSha256Hash is null)
            throw new SecurityValidationException("A SHA-256 client secret hash is required.");
        if (request.SecretSha256Hash is not null && !SecretHashPattern.IsMatch(request.SecretSha256Hash))
            throw new SecurityValidationException("Secret hash must be a 64-character SHA-256 value in hexadecimal.");

        request.Scopes = (request.Scopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (request.Scopes.Length is < 1 or > 50 || request.Scopes.Any(scope =>
                !ScopePattern.IsMatch(scope) || !OrganizationClientScopeRules.All.Contains(scope)))
            throw new SecurityValidationException("Provide 1 to 50 valid scopes, each up to 128 characters.");

        if (request.Scopes.Any(OrganizationClientScopeRules.OwnOrganizations.Contains) &&
            await clients.HasActingUserScopeConflictAsync(request.ActingUserId, excludeClientId, cancellationToken))
            throw new SecurityValidationException("This acting user already owns organizations for another integration client.");

        if (await clients.ClientNameExistsAsync(request.ClientName, excludeClientId, cancellationToken))
            throw new SecurityConflictException("An API client with this name already exists.");
    }
}
