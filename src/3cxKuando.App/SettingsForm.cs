using KuandoConnector.Core;

namespace KuandoConnector.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _pbx = new() { Width = 300 };
    private readonly TextBox _client = new() { Width = 300 };
    private readonly TextBox _secret = new() { Width = 300, UseSystemPasswordChar = true };
    private readonly TextBox _extension = new() { Width = 300 };
    private readonly NumericUpDown _poll = new() { Width = 100, Minimum = 2, Maximum = 60 };
    private readonly CheckBox _autostart = new() { Text = "Start with Windows sign-in", AutoSize = true };
    private readonly Label _hint = new() { AutoSize = true, MaximumSize = new Size(490, 0), Text = "Enter a new secret to replace the stored one. It is encrypted for the current Windows user using DPAPI." };
    private readonly AppSettings _settings;
    public string? NewSecret => string.IsNullOrWhiteSpace(_secret.Text) ? null : _secret.Text;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "3cxKuando - Settings";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(550, 430);
        ClientSize = new Size(550, 450);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        _pbx.Text = settings.PbxUrl;
        _client.Text = settings.ClientId;
        _extension.Text = settings.Extension;
        _poll.Value = Math.Clamp(settings.PollSeconds, 2, 60);
        _autostart.Checked = settings.StartAtLogin;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(15),
            RowCount = 0
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, "3CX server (HTTPS)", _pbx);
        AddRow(layout, "API Client ID", _client);
        AddRow(layout, "API Client Secret", _secret);
        AddRow(layout, "Extension", _extension);
        AddRow(layout, "Presence polling (seconds)", _poll);
        AddRow(layout, "", _autostart);
        AddRow(layout, "", _hint);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var save = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
        save.Click += Save;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        AddRow(layout, "", actions);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 6) }, 0, row);
        control.Margin = new Padding(0, 3, 0, 8);
        table.Controls.Add(control, 1, row);
    }

    private void Save(object? sender, EventArgs e)
    {
        _settings.PbxUrl = _pbx.Text.Trim();
        _settings.ClientId = _client.Text.Trim();
        _settings.Extension = _extension.Text.Trim();
        _settings.PollSeconds = (int)_poll.Value;
        _settings.StartAtLogin = _autostart.Checked;
        try
        {
            _settings.Validate();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Invalid configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
