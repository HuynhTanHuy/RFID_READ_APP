using System.Reflection;
using CareHR.RfidGateway.Services;

namespace CareHR.RfidGateway.UI;

public sealed class AboutForm : Form
{
    public AboutForm(GatewayState state)
    {
        Text = "About CareHR RFID Gateway";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 240);
        ShowInTaskbar = true;
        AppBranding.ApplyFormIcon(this);

        var version = Assembly.GetExecutingAssembly()
                           .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                       ?? "1.0.0";

        var snapshot = state.Snapshot;
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16)
        };
        root.Controls.Add(AppBranding.CreateLogoPictureBox(48));
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text =
                $"CareHR RFID Gateway{Environment.NewLine}{Environment.NewLine}" +
                $"Gateway Version: {version}{Environment.NewLine}" +
                $"SDK Version: {snapshot.SdkVersion}{Environment.NewLine}" +
                $"Firmware: {(string.IsNullOrWhiteSpace(snapshot.Firmware) ? "-" : snapshot.Firmware)}"
        });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        Controls.Add(root);
        Controls.Add(ok);
        AcceptButton = ok;
    }
}
