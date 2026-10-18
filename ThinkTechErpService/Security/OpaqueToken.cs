using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ThinkTechErpService.Security;

/// <summary>Random refresh and password-reset tokens. Only their SHA-256 hashes are stored.</summary>
internal static class OpaqueToken
{
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
