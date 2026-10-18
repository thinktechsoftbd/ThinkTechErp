using BCryptNet = BCrypt.Net.BCrypt;

namespace ThinkTechErpService.Security;

/// <summary>
/// bcrypt with work factor 12, the format existing users' hashes already use ($2a$12$), so hashes
/// stay interchangeable with any other application that reads security."User".
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    private static readonly Lazy<string> DummyHash =
        new(() => BCryptNet.HashPassword(Guid.NewGuid().ToString(), WorkFactor));

    public string Hash(string password) => BCryptNet.HashPassword(password, WorkFactor);

    public bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash) || !passwordHash.StartsWith("$2", StringComparison.Ordinal))
        {
            BCryptNet.Verify(password, DummyHash.Value);
            return false;
        }

        try
        {
            return BCryptNet.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
