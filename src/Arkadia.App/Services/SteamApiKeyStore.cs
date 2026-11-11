using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Arkadia.App.Services;

public sealed class SteamApiKeyStore
{
    private readonly string _keyPath;

    public SteamApiKeyStore(string? keyPath = null)
    {
        _keyPath = Path.GetFullPath(keyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arkadia",
            "steam-api-key.dat"));
    }

    public string? Load()
    {
        if (!File.Exists(_keyPath))
        {
            return null;
        }

        var protectedKey = File.ReadAllBytes(_keyPath);
        var key = ProtectedData.Unprotect(protectedKey, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("The Steam Web API key cannot be empty.", nameof(apiKey));
        }

        var directory = Path.GetDirectoryName(_keyPath)
            ?? throw new InvalidOperationException("The Steam API key path has no parent directory.");
        Directory.CreateDirectory(directory);
        var plainKey = Encoding.UTF8.GetBytes(apiKey.Trim());
        var temporaryPath = $"{_keyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var protectedKey = ProtectedData.Protect(
                plainKey,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporaryPath, protectedKey);
            if (File.Exists(_keyPath))
            {
                File.Replace(temporaryPath, _keyPath, null);
            }
            else
            {
                File.Move(temporaryPath, _keyPath);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainKey);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        if (File.Exists(_keyPath))
        {
            File.Delete(_keyPath);
        }
    }
}
