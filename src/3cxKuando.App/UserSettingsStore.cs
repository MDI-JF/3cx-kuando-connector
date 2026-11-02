using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using KuandoConnector.Core;

namespace KuandoConnector.App;

internal sealed class UserSettingsStore
{
    private static readonly string Root = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData);
    private readonly string _dir = Path.Combine(Root, "3cxKuando");

    public UserSettingsStore()
    {
        // Retain existing per-user settings and DPAPI-protected credentials from
        // development builds. DPAPI CurrentUser remains valid under the same account.
        string legacy = Path.Combine(Root, "3CXKuandoConnector");
        if (!Directory.Exists(legacy)) return;
        foreach (string name in new[] { "settings.json", "secret.dat" })
        {
            string source = Path.Combine(legacy, name);
            string destination = Path.Combine(_dir, name);
            if (!File.Exists(source) || File.Exists(destination)) continue;
            Directory.CreateDirectory(_dir);
            File.Copy(source, destination, overwrite: false);
        }
    }
    private string SettingsPath => Path.Combine(_dir, "settings.json");
    private string SecretPath => Path.Combine(_dir, "secret.dat");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
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
        if (!HasSecret) throw new InvalidOperationException("A 3CX API secret is required.");
        ConfigureAutostart(settings.StartAtLogin);
    }

    public void RefreshAutostart(bool enabled) => ConfigureAutostart(enabled);

    private static void ConfigureAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        const string name = "3cxKuando";
        // Remove the earlier autostart value to prevent duplicate launches.
        key?.DeleteValue("ThreeCxKuandoConnector", false);
        if (!enabled) { key?.DeleteValue(name, false); return; }
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Could not determine the application path.");
        key?.SetValue(name, "\"" + executable + "\"", RegistryValueKind.String);
    }
}
