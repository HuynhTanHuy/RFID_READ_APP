using CareHR.RfidGateway.Configuration;
using CareHR.RfidGateway.Sdk;
using CareHR.RfidGateway.Utils;

namespace CareHR.RfidGateway.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettingsStore _store;
    private readonly GatewayOptionsAccessor _options;

    private readonly TextBox _txtIp = new() { Width = 220 };
    private readonly NumericUpDown _numPort = new() { Minimum = 1, Maximum = 65535, Width = 220 };
    private readonly TextBox _txtApiBase = new() { Width = 320 };
    private readonly TextBox _txtToken = new() { Width = 320 };
    private readonly TextBox _txtReaderCode = new() { Width = 220 };
    private readonly TextBox _txtHospitalCode = new() { Width = 220 };
    private readonly CheckBox _chkAutoStart = new() { Text = "Auto start with Windows", AutoSize = true };
    private readonly CheckBox _chkRfPowerApply = new() { Text = "Apply on connect", AutoSize = true };
    private readonly NumericUpDown _numRfPower = new() { Minimum = 0, Maximum = 33, Width = 80 };

    public SettingsForm(AppSettingsStore store, GatewayOptionsAccessor options)
    {
        _store = store;
        _options = options;

        Text = "CareHR RFID Gateway — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 390);
        ShowInTaskbar = true;
        AppBranding.ApplyFormIcon(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var logoRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        logoRow.Controls.Add(AppBranding.CreateLogoPictureBox(36));
        root.Controls.Add(logoRow, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(int row, string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        AddRow(0, "Reader IP", _txtIp);
        AddRow(1, "Reader Port", _numPort);
        AddRow(2, "API Base URL", _txtApiBase);
        AddRow(3, "API Token", _txtToken);
        AddRow(4, "Reader Code", _txtReaderCode);
        AddRow(5, "Hospital Code", _txtHospitalCode);

        var rfPowerRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        rfPowerRow.Controls.Add(_numRfPower);
        rfPowerRow.Controls.Add(_chkRfPowerApply);
        AddRow(6, "RF Power (dBm)", rfPowerRow);

        layout.Controls.Add(_chkAutoStart, 1, 7);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        btnSave.Click += (_, _) => Save();
        buttons.Controls.Add(btnSave);
        buttons.Controls.Add(btnCancel);
        layout.Controls.Add(buttons, 1, 8);

        root.Controls.Add(layout, 0, 1);
        Controls.Add(root);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        LoadValues();
    }

    private void LoadValues()
    {
        var cfg = _options.Current;
        _txtIp.Text = cfg.ReaderIp;
        _numPort.Value = Math.Clamp(cfg.ReaderPort, 1, 65535);
        _txtApiBase.Text = cfg.ApiBaseUrl;
        _txtToken.Text = cfg.ApiToken;
        _txtReaderCode.Text = cfg.ReaderCode;
        _txtHospitalCode.Text = cfg.HospitalCode;
        _chkAutoStart.Checked = cfg.AutoStartWithWindows || AutoStartHelper.IsEnabled();
        _chkRfPowerApply.Checked = cfg.RfPower.HasValue;
        _numRfPower.Value = cfg.RfPower ?? 26;
        _numRfPower.Enabled = _chkRfPowerApply.Checked;
        _chkRfPowerApply.CheckedChanged += (_, _) => _numRfPower.Enabled = _chkRfPowerApply.Checked;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_txtIp.Text))
        {
            MessageBox.Show(this, "Reader IP is required.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtApiBase.Text))
        {
            MessageBox.Show(this, "API Base URL is required.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_chkRfPowerApply.Checked)
        {
            var power = (byte)_numRfPower.Value;
            if (!UhfPrimeSdk.IsValidRfPowerDbm(power))
            {
                MessageBox.Show(
                    this,
                    $"RF Power must be between {UhfPrimeSdk.RfPowerMinDbm} and {UhfPrimeSdk.RfPowerMaxDbm} dBm.",
                    "Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        var current = _options.Current;
        var updated = new GatewayOptions
        {
            ReaderIp = _txtIp.Text.Trim(),
            ReaderPort = (int)_numPort.Value,
            RfPower = _chkRfPowerApply.Checked ? (byte)_numRfPower.Value : null,
            ApiBaseUrl = _txtApiBase.Text.Trim(),
            ApiEventsPath = current.ApiEventsPath,
            ApiToken = _txtToken.Text.Trim(),
            ReaderCode = _txtReaderCode.Text.Trim(),
            HospitalCode = _txtHospitalCode.Text.Trim(),
            DeviceId = current.DeviceId,
            Direction = current.Direction,
            ConnectTimeoutMs = current.ConnectTimeoutMs,
            ReconnectIntervalMs = current.ReconnectIntervalMs,
            InventoryPollTimeoutMs = current.InventoryPollTimeoutMs,
            DebounceSeconds = current.DebounceSeconds,
            ApiRetryCount = current.ApiRetryCount,
            ApiRetryBackoffMs = current.ApiRetryBackoffMs,
            AutoStartWithWindows = _chkAutoStart.Checked,
            LogDirectory = current.LogDirectory
        };

        try
        {
            AutoStartHelper.SetEnabled(updated.AutoStartWithWindows);
            _store.Save(updated);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
