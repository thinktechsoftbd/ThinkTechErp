using System.Security.Cryptography;
using System.Text;

namespace ThinkTechErpAPI.Security;

// Development-only JWT signing key, created once per OS user and stored outside the
// repository (%LOCALAPPDATA%\ThinkTechErp on Windows, ~/.local/share/ThinkTechErp on
// Linux) so tokens stay valid across restarts. Never used outside Development.
public static class DevelopmentSigningKey
{
    private const int MinimumKeyBytes = 32;

    public static string GetOrCreate()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThinkTechErp");
        var path = Path.Combine(directory, "jwt-development-signing-key");

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (Encoding.UTF8.GetByteCount(existing) >= MinimumKeyBytes)
                return existing;
        }

        Directory.CreateDirectory(directory);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using (var writer = new StreamWriter(path, new UTF8Encoding(false), options))
            writer.Write(key);

        return key;
    }
}
