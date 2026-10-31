using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// A server-to-server client from Authentication:Integration:Clients. Only the SHA-256 of its secret
/// is kept. ActingUserId is the user recorded as created_by/updated_by for its changes, and the
/// organizations created under it are the only ones the client may change.
/// </summary>
public sealed record IntegrationClient(
    string ClientId, byte[] SecretSha256, int ActingUserId, IReadOnlySet<string> Scopes);

/// <summary>
/// The configured integration clients, read once at startup. Removing a client and restarting
/// revokes it at once, including its unexpired tokens (the integration endpoints look it up again).
/// </summary>
public sealed partial class IntegrationClientStore
{
    private static readonly byte[] UnknownClientHash = new byte[SHA256.HashSizeInBytes];

    private readonly Dictionary<string, IntegrationClient> clients;

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

            // Ownership follows the acting user, so two clients sharing one could change each other's organizations.
            if (clients.Values.Any(client => client.ActingUserId == actingUserId))
                throw new InvalidOperationException(
                    $"{setting}:ActingUserId {actingUserId} is already used by another integration client.");

            var scopes = entry.GetSection("Scopes").Get<string[]>() ?? [];
            if (scopes.Length == 0 || scopes.Any(scope => !IntegrationScopes.All.Contains(scope)))
                throw new InvalidOperationException(
                    $"{setting}:Scopes must list one or more of: {string.Join(", ", IntegrationScopes.All)}.");

            clients.Add(clientId, new IntegrationClient(
                clientId, secretSha256, actingUserId, scopes.ToHashSet(StringComparer.Ordinal)));
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

    /// <summary>The configured client with this ID, or null when it was removed.</summary>
    public IntegrationClient? Find(string? clientId)
        => clientId is not null && clients.TryGetValue(clientId, out var client) ? client : null;

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
