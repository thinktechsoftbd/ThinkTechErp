namespace ThinkTechErpService.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a password against a stored hash. A null hash (unknown account) still costs a full
    /// verification, so response times don't reveal which emails exist.
    /// </summary>
    bool Verify(string password, string? passwordHash);
}
