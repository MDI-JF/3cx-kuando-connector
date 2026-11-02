using System.Drawing;
using ThreeCxKuando.Core;

namespace ThreeCxKuando.App;

internal sealed class TrayContext : ApplicationContext
{
    private readonly UserSettingsStore _store = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = 500 };
    private CancellationTokenSource? _monitorCancel;
    private Task? _monitorTask;
    private AppSettings _settings = new();
    private int _changesRequested;
    private bool _editing;
    private string _lastPresence = "unbekannt";
    private string _lastCall = "unbekannt";
    private string _lastCallSent = "noch nichts";
    private string _lastError = "";

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Verbindung wird aufgebaut …") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Einstellungen …", null, (_, _) => OpenSettings());
        menu.Items.Add("Neu verbinden", null, (_, _) => StartMonitor());
        menu.Items.Add("Diagnoseprotokoll öffnen", null, (_, _) => DiagnosticLog.Open());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Text = "3CX kuando Connector",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _displayTimer.Tick += (_, _) => UpdateStatus();
        _displayTimer.Start();
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
            ? $"3CX: {_lastPresence} | Anruf: {_lastCall} | kuando Call: {_lastCallSent}"
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
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _monitorCancel = cancel;
        var settingsSnapshot = _store.Load();
        _monitorTask = Task.Run(() => MonitorAsync(settingsSnapshot, cancel.Token));
    }

    private async Task MonitorAsync(AppSettings settings, CancellationToken ct)
    {
        using var threeHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var hubHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var api = new ThreeCxApi(threeHttp, settings, _store.LoadSecret());
        var terminalSession = SystemInformation.TerminalServerSession;
        var hub = new KuandoHub(hubHttp, settings, terminalSession);
        var dedup = new EventDeduplicator();
        var callEvents = new CallEventState();
        hub.RegistrationRecovered += () =>
        {
            dedup.Reset();
            callEvents.ResetForReconnect();
            DiagnosticLog.Write("kuandoHUB", "Sources registered/refreshed");
        };
        using var callChanges = new SemaphoreSlim(0, 1);
        void Trigger() { if (Interlocked.Exchange(ref _changesRequested, 1) == 0) { try { callChanges.Release(); } catch (SemaphoreFullException) { } } }
        var wsTask = api.WatchCallEventsAsync(Trigger, ct);
        var lastCallFetch = DateTimeOffset.MinValue;
        string? lastCallValue = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? presenceValue = null;
                string? callValue = null;
                var problems = new List<string>();

                try
                {
                    var presence = await api.GetPresenceAsync(ct);
                    presenceValue = StatusLogic.NormalizePresence(presence.Profile);
                    if (presenceValue.Length == 0)
                        throw new InvalidOperationException("Unbekanntes 3CX-Präsenzprofil: " + presence.Profile);
                    _lastPresence = presenceValue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { problems.Add("Presence: " + ex.Message); }

                try
                {
                    if (Interlocked.Exchange(ref _changesRequested, 0) == 1 ||
                        DateTimeOffset.UtcNow - lastCallFetch > TimeSpan.FromSeconds(5))
                    {
                        lastCallValue = await api.GetCallStateAsync(ct);
                        lastCallFetch = DateTimeOffset.UtcNow;
                    }
                    callValue = lastCallValue;
                    if (callValue is not null)
                    {
                        if (_lastCall != callValue)
                            DiagnosticLog.Write("3CX Call detected", callValue);
                        _lastCall = callValue;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { problems.Add("Call Control: " + ex.Message); }

                try
                {
                    if (await hub.EnsureRegisteredAsync(ct))
                    {
                        if (presenceValue is not null &&
                            dedup.ShouldSend(KuandoProtocol.PresenceSource, presenceValue))
                        {
                            await hub.SendPresenceAsync(presenceValue, ct);
                            dedup.MarkSent(KuandoProtocol.PresenceSource, presenceValue);
                        }
                        if (callValue is not null && callEvents.ShouldPublish(callValue))
                        {
                            await hub.SendCallAsync(callValue, ct);
                            callEvents.MarkPublished(callValue); // never mark a failed HTTP request
                            _lastCallSent = callValue == "Idle" ? "Off" : callValue;
                            DiagnosticLog.Write("kuandoHUB 3CX Call sent", _lastCallSent);
                        }
                    }
                    else problems.Add("kuandoHUB nicht erreichbar: " + hub.EndpointLabel);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { problems.Add("kuandoHUB: " + ex.Message); }

                _lastError = string.Join(" | ", problems);
                // Never send an artificial Off/unknown color when 3CX cannot be read.
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { try { await wsTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
    }

    protected override void ExitThreadCore()
    {
        _lifetime.Cancel();
        _monitorCancel?.Cancel();
        _displayTimer.Stop();
        _displayTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
