using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// A server-to-server client from PostgreSQL. Only the SHA-256 of its secret
/// is kept. ActingUserId is the user recorded as created_by/updated_by for its changes, and the
/// organizations created under it are the only ones the client may change.
/// </summary>
public sealed record IntegrationClient(
    string ClientId, byte[] SecretSha256, int ActingUserId, IReadOnlySet<string> Scopes);

/// <summary>
/// Reads integration clients from PostgreSQL for each credential check and protected request, so
/// deactivation and scope changes take effect immediately. The configuration snapshot remains
/// available only for an explicit rollback construction through FromConfiguration.
/// </summary>
public sealed partial class IntegrationClientStore
{
    private static readonly byte[] UnknownClientHash = new byte[SHA256.HashSizeInBytes];

    private readonly Dictionary<string, IntegrationClient> clients;
    private readonly IOrganizationClientRepository? repository;

    /// <summary>Creates a database-backed store so client changes take effect without an API restart.</summary>
    public IntegrationClientStore(IOrganizationClientRepository repository)
    {
        this.repository = repository;
        clients = new Dictionary<string, IntegrationClient>(StringComparer.Ordinal);
    }

    private IntegrationClientStore(Dictionary<string, IntegrationClient> clients) => this.clients = clients;

    public static IntegrationClientStore FromConfiguration(IConfigurationSection clientsSection)
    {
        var clients = new Dictionary<string, IntegrationClient>(StringComparer.Ordinal);
        foreach (var entry in clientsSection.GetChildren())
        {
            var clientId = entry.Key;
            var setting = $"Authentication:Integration:Clients:{clientId}";
            if (!ClientIdPattern().IsMatch(clientId))
                throw new InvalidOperationException(
                    $"Integration client ID '{clientId}' must be 3-64 letters, digits or underscores.");

            var secretSha256 = TryFromHex(entry["SecretSha256"]);
            if (secretSha256?.Length != SHA256.HashSizeInBytes)
                throw new InvalidOperationException(
                    $"{setting}:SecretSha256 must be the 64-character hex SHA-256 of the client secret.");

            if (!int.TryParse(entry["ActingUserId"], NumberStyles.None, CultureInfo.InvariantCulture, out var actingUserId) ||
                actingUserId <= 0)
                throw new InvalidOperationException(
                    $"{setting}:ActingUserId must be the ID of the dedicated user recorded for this client's changes.");

            var scopes = (entry.GetSection("Scopes").Get<string[]>() ?? []).ToHashSet(StringComparer.Ordinal);
            if (scopes.Count == 0 || scopes.Any(scope => !IntegrationScopes.All.Contains(scope)))
                throw new InvalidOperationException(
                    $"{setting}:Scopes must list one or more of: {string.Join(", ", IntegrationScopes.All)}.");

            // Ownership follows the acting user, so two clients sharing one could change each other's
            // organizations. Read-only clients (e.g. the website) own nothing and may share one.
            if (scopes.Overlaps(IntegrationScopes.OwnOrganizations) &&
                clients.Values.Any(client => client.ActingUserId == actingUserId &&
                                             client.Scopes.Overlaps(IntegrationScopes.OwnOrganizations)))
                throw new InvalidOperationException(
                    $"{setting}:ActingUserId {actingUserId} is already used by another integration client.");

            clients.Add(clientId, new IntegrationClient(clientId, secretSha256, actingUserId, scopes));
        }

        return new IntegrationClientStore(clients);
    }

    /// <summary>The client with this ID and secret, or null.</summary>
    public IntegrationClient? Authenticate(string clientId, string clientSecret)
    {
        var known = clients.TryGetValue(clientId, out var client);
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret));

        // Compare for unknown IDs too, so the response time doesn't reveal which client IDs exist.
        var matches = CryptographicOperations.FixedTimeEquals(secretHash, client?.SecretSha256 ?? UnknownClientHash);
        return known && matches ? client : null;
    }

    /// <summary>Authenticates an active database client, or uses the legacy configuration snapshot when constructed for rollback.</summary>
    public async Task<IntegrationClient?> AuthenticateAsync(
        string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        if (repository is null)
            return Authenticate(clientId, clientSecret);

        var stored = ClientIdPattern().IsMatch(clientId)
            ? await repository.GetClientCredentialsAsync(clientId, cancellationToken)
            : null;
        var active = stored is { IsActive: true };
        var expectedHash = active ? TryFromHex(stored!.SecretSha256Hash) ?? UnknownClientHash : UnknownClientHash;
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret));
        var matches = CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
        if (!active || !matches || !HasValidScopes(stored!.Scopes))
            return null;

        return new IntegrationClient(
            stored.ClientName, expectedHash, stored.ActingUserId, stored.Scopes.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The configured client with this ID, or null when it was removed.</summary>
    public IntegrationClient? Find(string? clientId)
        => clientId is not null && clients.TryGetValue(clientId, out var client) ? client : null;

    /// <summary>Reads the current active client record from PostgreSQL, or the rollback snapshot.</summary>
    public async Task<IntegrationClient?> FindAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        if (repository is null)
            return Find(clientId);
        if (clientId is null || !ClientIdPattern().IsMatch(clientId))
            return null;

        var stored = await repository.GetClientCredentialsAsync(clientId, cancellationToken);
        var secretHash = stored is null ? null : TryFromHex(stored.SecretSha256Hash);
        if (stored is not { IsActive: true } || !HasValidScopes(stored.Scopes) ||
            secretHash?.Length != SHA256.HashSizeInBytes)
            return null;

        return new IntegrationClient(
            stored.ClientName, secretHash, stored.ActingUserId, stored.Scopes.ToHashSet(StringComparer.Ordinal));
    }

    private static bool HasValidScopes(IEnumerable<string> scopes)
        => scopes.Any() && scopes.All(IntegrationScopes.All.Contains);

    private static byte[]? TryFromHex(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? null : Convert.FromHexString(value.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,64}$")]
    private static partial Regex ClientIdPattern();
}
