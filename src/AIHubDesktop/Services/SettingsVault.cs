using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIHubDesktop.Models;

namespace AIHubDesktop.Services;

public static class SettingsVault
{
    private static readonly string AppDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AIHubDesktop");

    private static readonly string SettingsPath =
        Path.Combine(AppDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static StoredSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new StoredSettings();
            }

            string json = File.ReadAllText(SettingsPath);

            return JsonSerializer.Deserialize<StoredSettings>(
                       json,
                       JsonOptions)
                   ?? new StoredSettings();
        }
        catch
        {
            return new StoredSettings();
        }
    }

    public static void Save(StoredSettings settings)
    {
        Directory.CreateDirectory(AppDirectory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        File.WriteAllText(SettingsPath, json);
    }

    public static string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        byte[] input = Encoding.UTF8.GetBytes(plainText);

        byte[] encrypted = ProtectedData.Protect(
            input,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string encryptedText)
    {
        if (string.IsNullOrWhiteSpace(encryptedText))
        {
            return string.Empty;
        }

        try
        {
            byte[] encrypted = Convert.FromBase64String(encryptedText);

            byte[] decrypted = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }
}
