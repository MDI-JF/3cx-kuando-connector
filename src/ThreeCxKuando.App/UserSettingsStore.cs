using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ThreeCxKuando.Core;

namespace ThreeCxKuando.App;

internal sealed class UserSettingsStore
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3CXKuandoConnector");
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
        if (!HasSecret) throw new InvalidOperationException("Ein 3CX API-Secret muss hinterlegt werden.");
        ConfigureAutostart(settings.StartAtLogin);
    }

    private static void ConfigureAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        const string name = "ThreeCxKuandoConnector";
        if (!enabled) { key?.DeleteValue(name, false); return; }
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Pfad der Anwendung konnte nicht bestimmt werden.");
        key?.SetValue(name, "\"" + executable + "\"", RegistryValueKind.String);
    }
}
