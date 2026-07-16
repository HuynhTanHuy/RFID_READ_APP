using CareHR.RfidGateway.Utils;

namespace CareHR.RfidGateway.UI;

public sealed class LogViewerForm : Form
{
    private readonly string _logDirectory;
    private readonly TextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
        WordWrap = false,
        Font = new Font("Consolas", 9f)
    };

    public LogViewerForm(string logDirectory)
    {
        _logDirectory = logDirectory;
        Text = "CareHR RFID Gateway — Log";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 500);
        ShowInTaskbar = true;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6) };
        var btnRefresh = new Button { Text = "Refresh", AutoSize = true };
        btnRefresh.Click += (_, _) => LoadLog();
        top.Controls.Add(btnRefresh);

        Controls.Add(_text);
        Controls.Add(top);
        Load += (_, _) => LoadLog();
    }

    private void LoadLog()
    {
        try
        {
            var file = LogExport.FindLatestLogFile(_logDirectory);
            if (file is null)
            {
                _text.Text = "No log file found.";
                return;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            _text.Text = reader.ReadToEnd();
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _text.Text = ex.Message;
        }
    }
}
