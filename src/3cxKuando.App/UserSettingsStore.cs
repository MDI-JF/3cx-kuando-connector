using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using KuandoConnector.Core;

namespace KuandoConnector.App;

internal sealed class UserSettingsStore
{
    private readonly string _dir;
    private readonly Action<bool> _configureAutostart;

    // Injection is limited to local file/registry effects so behavior can be
    // tested without modifying the CI runner's real user profile or startup.
    internal UserSettingsStore(string? directory = null, Action<bool>? configureAutostart = null)
    {
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3cxKuando");
        _configureAutostart = configureAutostart ?? ConfigureAutostart;
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");
    private string SecretPath => Path.Combine(_dir, "secret.dat");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        return AppSettings.FromJson(File.ReadAllText(SettingsPath));
    }

    public bool HasSecret => File.Exists(SecretPath);

    public string LoadSecret()
    {
        var protectedBytes = File.ReadAllBytes(SecretPath);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plainBytes); }
        finally { CryptographicOperations.ZeroMemory(plainBytes); }
    }

    public void Save(AppSettings settings, string? newSecret)
    {
        settings.Validate();
        if (!HasSecret && string.IsNullOrEmpty(newSecret))
            throw new InvalidOperationException("A 3CX API secret is required.");
        Directory.CreateDirectory(_dir);
        // Never put credentials, access tokens or other sensitive values in settings.json.
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, SettingsPath, true);
        if (!string.IsNullOrEmpty(newSecret))
        {
            byte[] plain = Encoding.UTF8.GetBytes(newSecret);
            try { File.WriteAllBytes(SecretPath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        _configureAutostart(settings.StartAtLogin);
    }

    public void RefreshAutostart(bool enabled) => _configureAutostart(enabled);

    private static void ConfigureAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        const string name = "3cxKuando";
        if (!enabled) { key?.DeleteValue(name, false); return; }
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Could not determine the application path.");
        key?.SetValue(name, "\"" + executable + "\"", RegistryValueKind.String);
    }
}
