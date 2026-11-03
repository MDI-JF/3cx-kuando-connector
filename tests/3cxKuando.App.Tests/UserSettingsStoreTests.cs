using System.Text;
using System.Text.Json;
using KuandoConnector.App;
using KuandoConnector.Core;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "3cxKuando-Tests", Guid.NewGuid().ToString("N"));
    private readonly List<bool> _autostart = [];

    private UserSettingsStore Store() => new(_dir, enabled => _autostart.Add(enabled));

    private static AppSettings Config() => new()
    {
        PbxUrl = "https://pbx.example.test",
        ClientId = "test-client",
        Extension = "100",
        StartAtLogin = false
    };

    [Fact]
    public void MissingSettingsReturnDefaultsWithoutCreatingFiles()
    {
        var store = Store();
        Assert.False(store.HasSecret);
        Assert.Equal(new LightStyle("Pulse", 100, 100, 0), store.Load().EventStyles["Hold"]);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void SaveEncryptsSecretAndPersistsOnlyNonSensitiveSettings()
    {
        var store = Store();
        var settings = Config();
        const string secret = "distinct-test-secret-value";
        store.Save(settings, secret);
        Assert.True(store.HasSecret);
        Assert.Equal(secret, store.LoadSecret());
        Assert.Equal(settings.ClientId, store.Load().ClientId);
        Assert.Equal(settings.EventStyles["Held"], store.Load().EventStyles["Held"]);
        Assert.Equal(new[] { false }, _autostart);
        string json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_dir, "secret.dat"))));
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json.tmp")));
    }

    [Fact]
    public void EmptyReplacementRetainsExistingDpapiSecret()
    {
        var store = Store();
        var first = Config();
        store.Save(first, "initial-secret");
        first.PollSeconds = 7;
        first.EventStyles["Connected"] = new LightStyle("Pulse", 10, 20, 30);
        store.Save(first, null);
        Assert.Equal("initial-secret", store.LoadSecret());
        Assert.Equal(7, store.Load().PollSeconds);
        Assert.Equal(first.EventStyles["Connected"], store.Load().EventStyles["Connected"]);
        store.RefreshAutostart(true);
        Assert.Equal(new[] { false, false, true }, _autostart);
    }

    [Fact]
    public void MissingSecretRejectsSaveWithoutCreatingPartialConfiguration()
    {
        var store = Store();
        Assert.Throws<InvalidOperationException>(() => store.Save(Config(), null));
        Assert.False(Directory.Exists(_dir));
        Assert.Empty(_autostart);
    }

    [Fact]
    public void InvalidConfigurationRejectsSaveBeforeWriting()
    {
        var store = Store();
        var settings = Config();
        settings.PbxUrl = "http://insecure.example.test";
        Assert.Throws<ArgumentException>(() => store.Save(settings, "secret"));
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void LoadMigratesPriorHoldDefaultsWithoutChangingCustomColors()
    {
        Directory.CreateDirectory(_dir);
        var previous = Config();
        previous.EventStyles["Hold"] = new LightStyle("Pulse", 100, 0, 0);
        previous.EventStyles["Held"] = new LightStyle("Blink", 12, 23, 34);
        var json = JsonSerializer.Serialize(previous)
            .Replace("\"SettingsSchemaVersion\":2,", "");
        File.WriteAllText(Path.Combine(_dir, "settings.json"), json);
        var loaded = Store().Load();
        Assert.Equal(new LightStyle("Pulse", 100, 100, 0), loaded.EventStyles["Hold"]);
        Assert.Equal(new LightStyle("Blink", 12, 23, 34), loaded.EventStyles["Held"]);
    }

    [Fact]
    public void InvalidJsonIsNotSilentlyReplacedByDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{invalid-json");
        Assert.Throws<JsonException>(() => Store().Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
