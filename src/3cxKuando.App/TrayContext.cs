using System.Drawing;
using KuandoConnector.Core;

namespace KuandoConnector.App;

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
    private string _lastPresence = "unknown";
    private string _lastCall = "unknown";
    private string _lastCallSent = "none";
    private string _lastError = "";
    private string _lastDiagnosticHealth = "";

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Connecting...") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Reconnect", null, (_, _) => StartMonitor());
        menu.Items.Add("Open diagnostic log", null, (_, _) => DiagnosticLog.Open());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Text = "3cxKuando",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _displayTimer.Tick += (_, _) => UpdateStatus();
        _displayTimer.Start();
        DiagnosticLog.Write("Connector", "Started");
        try
        {
            _settings = _store.Load();
            if (!string.IsNullOrWhiteSpace(_settings.PbxUrl))
                _store.RefreshAutostart(_settings.StartAtLogin);
        }
        catch (Exception e)
        {
            ShowError("Could not read settings: " + e.Message);
            DiagnosticLog.Write("Configuration error", DiagnosticLog.SafeError(e));
        }
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
            ? $"3CX: {_lastPresence} | Call: {_lastCall} | kuando Call: {_lastCallSent}"
            : "Error: " + _lastError;
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
            catch (Exception e) { MessageBox.Show(e.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        finally { _editing = false; }
    }

    private void StartMonitor()
    {
        _monitorCancel?.Cancel();
        if (!_store.HasSecret) { ShowError("No API credentials have been saved."); return; }
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
        using var api = new PbxApiClient(threeHttp, settings, _store.LoadSecret());
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
                        throw new InvalidOperationException("Unrecognized 3CX presence profile: " + presence.Profile);
                    if (_lastPresence != presenceValue)
                        DiagnosticLog.Write("3CX Presence detected", presenceValue);
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

                // A completed call must not wait for a potentially timed-out
                // registration refresh. A failed Off remains pending for retry.
                if (callValue == "Idle" && callEvents.ShouldPublish("Idle"))
                {
                    try
                    {
                        await hub.SendCallAsync("Idle", ct);
                        callEvents.MarkPublished("Idle");
                        _lastCallSent = "Off";
                        DiagnosticLog.Write("kuandoHUB 3CX Call sent", "Off");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        problems.Add("kuandoHUB Call Off: " + DiagnosticLog.SafeError(ex));
                    }
                }

                try
                {
                    if (await hub.EnsureRegisteredAsync(ct))
                    {
                        // Failed presence requests must not block call signaling.
                        if (presenceValue is not null &&
                            dedup.ShouldSend(KuandoProtocol.Source, KuandoProtocol.PresenceEventType, presenceValue))
                        {
                            try
                            {
                                await hub.SendPresenceAsync(presenceValue, ct);
                                dedup.MarkSent(KuandoProtocol.Source, KuandoProtocol.PresenceEventType, presenceValue);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                            catch (Exception ex)
                            {
                                problems.Add("kuandoHUB Presence: " + DiagnosticLog.SafeError(ex));
                            }
                        }
                        if (callValue is not null && callValue != "Idle" && callEvents.ShouldPublish(callValue))
                        {
                            try
                            {
                                await hub.SendCallAsync(callValue, ct);
                                callEvents.MarkPublished(callValue); // successful POST only
                                _lastCallSent = callValue == "Idle" ? "Off" : callValue;
                                DiagnosticLog.Write("kuandoHUB 3CX Call sent", _lastCallSent);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                            catch (Exception ex)
                            {
                                problems.Add("kuandoHUB Call: " + DiagnosticLog.SafeError(ex));
                            }
                        }
                    }
                    else problems.Add("kuandoHUB unreachable: " + hub.EndpointLabel);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { problems.Add("kuandoHUB: " + DiagnosticLog.SafeError(ex)); }

                _lastError = string.Join(" | ", problems);
                var health = problems.Count == 0 ? "Connected" :
                    string.Join(" | ", problems.Select(problem =>
                        problem.StartsWith("kuandoHUB", StringComparison.Ordinal) ?
                        problem : problem.Split(':')[0]));
                if (health != _lastDiagnosticHealth)
                {
                    DiagnosticLog.Write("Health", health);
                    _lastDiagnosticHealth = health;
                }
                // Never send an artificial Off/unknown color when 3CX cannot be read.
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { try { await wsTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
    }

    protected override void ExitThreadCore()
    {
        DiagnosticLog.Write("Connector", "Stopped");
        _lifetime.Cancel();
        _monitorCancel?.Cancel();
        _displayTimer.Stop();
        _displayTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
