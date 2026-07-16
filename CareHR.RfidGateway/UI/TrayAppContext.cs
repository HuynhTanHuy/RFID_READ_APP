using CareHR.RfidGateway.Configuration;
using CareHR.RfidGateway.Services;
using CareHR.RfidGateway.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CareHR.RfidGateway.UI;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly IHost _host;
    private readonly NotifyIcon _tray;
    private readonly GatewayState _state;
    private Form? _statusForm;
    private Form? _settingsForm;
    private Form? _logForm;
    private Form? _aboutForm;

    public TrayAppContext(IHost host)
    {
        _host = host;
        _state = host.Services.GetRequiredService<GatewayState>();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Status", null, (_, _) => ShowStatus());
        menu.Items.Add("Restart Reader", null, (_, _) => RestartReader());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("View Log", null, (_, _) => ShowLog());
        menu.Items.Add("Export Log", null, (_, _) => ExportLog());
        menu.Items.Add("About", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        _tray = new NotifyIcon
        {
            Text = "CareHR RFID Gateway",
            Icon = AppBranding.AppIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowStatus();

        _state.Changed += (_, _) => UpdateTooltip();
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var s = _state.Snapshot;
        var text = $"CareHR RFID Gateway\nReader: {s.Reader}\nAPI: {s.Api}\nSDK: {s.Sdk}";
        _tray.Text = text.Length <= 63 ? text : text[..63];
    }

    private void ShowStatus()
    {
        if (_statusForm is { IsDisposed: false })
        {
            _statusForm.Activate();
            return;
        }

        _statusForm = _host.Services.GetRequiredService<StatusForm>();
        _statusForm.FormClosed += (_, _) => _statusForm = null;
        _statusForm.Show();
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = _host.Services.GetRequiredService<SettingsForm>();
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private void ShowLog()
    {
        if (_logForm is { IsDisposed: false })
        {
            _logForm.Activate();
            return;
        }

        var options = _host.Services.GetRequiredService<GatewayOptionsAccessor>().Current;
        var logDir = ResolveLogDirectory(options.LogDirectory);
        _logForm = new LogViewerForm(logDir);
        _logForm.FormClosed += (_, _) => _logForm = null;
        _logForm.Show();
    }

    private void ShowAbout()
    {
        if (_aboutForm is { IsDisposed: false })
        {
            _aboutForm.Activate();
            return;
        }

        _aboutForm = new AboutForm(_state);
        _aboutForm.FormClosed += (_, _) => _aboutForm = null;
        _aboutForm.ShowDialog();
    }

    private void ExportLog()
    {
        try
        {
            var options = _host.Services.GetRequiredService<GatewayOptionsAccessor>().Current;
            var logDir = ResolveLogDirectory(options.LogDirectory);
            using var dialog = new SaveFileDialog
            {
                Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
                FileName = $"CareHR-RfidGateway-{DateTime.Now:yyyyMMdd-HHmmss}.log"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            LogExport.ExportLatest(logDir, dialog.FileName);
            _tray.ShowBalloonTip(2000, "CareHR RFID Gateway", "Log exported.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export Log", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestartReader()
    {
        var worker = _host.Services.GetRequiredService<RfidGatewayWorker>();
        worker.RequestRestart();
        _tray.ShowBalloonTip(1500, "CareHR RFID Gateway", "Restart Reader requested.", ToolTipIcon.Info);
    }

    private async Task ExitAsync()
    {
        _tray.Visible = false;
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string ResolveLogDirectory(string configured)
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }
}
