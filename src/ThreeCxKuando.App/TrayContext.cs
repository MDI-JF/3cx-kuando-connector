using System.Drawing;
using ThreeCxKuando.Core;

namespace ThreeCxKuando.App;

internal sealed class TrayContext : ApplicationContext
{
    private readonly UserSettingsStore _store = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _monitorCancel;
    private Task? _monitorTask;
    private AppSettings _settings = new();
    private int _changesRequested;
    private bool _editing;
    private string _lastPresence = "unbekannt";
    private string _lastCall = "unbekannt";
    private string _lastError = "";

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Verbindung wird aufgebaut …") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Einstellungen …", null, (_, _) => OpenSettings());
        menu.Items.Add("Neu verbinden", null, (_, _) => StartMonitor());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Text = "3CX kuando Connector",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        try { _settings = _store.Load(); }
        catch (Exception e) { ShowError("Einstellungen konnten nicht gelesen werden: " + e.Message); }
        if (string.IsNullOrWhiteSpace(_settings.PbxUrl) || !_store.HasSecret)
            OpenSettings();
        else StartMonitor();
    }

    private void ShowError(string text)
    {
        _lastError = text;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _statusItem.Text = string.IsNullOrWhiteSpace(_lastError)
            ? $"3CX: {_lastPresence} | Anruf: {_lastCall}"
            : "Fehler: " + _lastError;
        _tray.Text = _statusItem.Text.Length > 60 ? _statusItem.Text[..60] : _statusItem.Text;
    }

    private void OpenSettings()
    {
        if (_editing) return;
        _editing = true;
        try
        {
            var candidate = _store.Load();
            using var form = new SettingsForm(candidate);
            if (form.ShowDialog() != DialogResult.OK) return;
            try
            {
                _store.Save(candidate, form.NewSecret);
                _settings = candidate;
                _lastError = "";
                StartMonitor();
            }
            catch (Exception e) { MessageBox.Show(e.Message, "Speichern fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        finally { _editing = false; }
    }

    private void StartMonitor()
    {
        _monitorCancel?.Cancel();
        if (!_store.HasSecret) { ShowError("Keine Zugangsdaten gespeichert."); return; }
        try { _settings.Validate(); }
        catch (Exception e) { ShowError(e.Message); return; }
        _monitorCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var settingsSnapshot = _store.Load();
        _monitorTask = Task.Run(() => MonitorAsync(settingsSnapshot, _monitorCancel.Token));
    }

    private async Task MonitorAsync(AppSettings settings, CancellationToken ct)
    {
        using var threeHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var hubHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var api = new ThreeCxApi(threeHttp, settings, _store.LoadSecret());
        var terminalSession = SystemInformation.TerminalServerSession;
        var hub = new KuandoHub(hubHttp, settings, terminalSession);
        var dedup = new EventDeduplicator();
        hub.RegistrationRecovered += dedup.Reset;
        using var callChanges = new SemaphoreSlim(0, 1);
        void Trigger() { if (Interlocked.Exchange(ref _changesRequested, 1) == 0) { try { callChanges.Release(); } catch (SemaphoreFullException) { } } }
        var wsTask = api.WatchCallEventsAsync(Trigger, ct);
        var lastCallFetch = DateTimeOffset.MinValue;
        var lastCallValue = "Idle";
        var lastPresenceValue = "";
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var presence = await api.GetPresenceAsync(ct);
                    lastPresenceValue = StatusLogic.NormalizePresence(presence.Profile);
                    if (lastPresenceValue == "") throw new InvalidOperationException("Unbekanntes 3CX-Präsenzprofil: " + presence.Profile);
                    if (Interlocked.Exchange(ref _changesRequested, 0) == 1 ||
                        DateTimeOffset.UtcNow - lastCallFetch > TimeSpan.FromSeconds(5))
                    {
                        lastCallValue = await api.GetCallStateAsync(ct);
                        lastCallFetch = DateTimeOffset.UtcNow;
                    }
                    if (await hub.EnsureRegisteredAsync(ct))
                    {
                        if (dedup.ShouldSend(KuandoProtocol.PresenceSource, lastPresenceValue))
                        {
                            await hub.SendPresenceAsync(lastPresenceValue, ct);
                            dedup.MarkSent(KuandoProtocol.PresenceSource, lastPresenceValue);
                        }
                        if (dedup.ShouldSend(KuandoProtocol.CallSource, lastCallValue))
                        {
                            await hub.SendCallAsync(lastCallValue, ct);
                            dedup.MarkSent(KuandoProtocol.CallSource, lastCallValue);
                        }
                    }
                    _lastPresence = lastPresenceValue;
                    _lastCall = lastCallValue;
                    _lastError = "";
                    SafeUi(UpdateStatus);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    _lastError = e.Message;
                    SafeUi(UpdateStatus);
                    // Do not change kuando lights when 3CX is unreachable.
                }
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { try { await wsTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
    }

    private void SafeUi(Action action)
    {
        try
        {
            if (!_tray.Visible) return;
            if (Application.OpenForms.Count > 0)
            {
                var form = Application.OpenForms[0];
                if (form.IsHandleCreated) form.BeginInvoke(action);
            }
            // A NotifyIcon-only application has no Form; use the WinForms sync context.
            else _uiContext?.Post(_ => action(), null);
        }
        catch { }
    }

    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    protected override void ExitThreadCore()
    {
        _lifetime.Cancel();
        _monitorCancel?.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
