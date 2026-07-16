using CareHR.RfidGateway.Api;
using CareHR.RfidGateway.Models;
using CareHR.RfidGateway.Reader;
using CareHR.RfidGateway.Services;

namespace CareHR.RfidGateway.UI;

public sealed class StatusForm : Form
{
    private readonly GatewayState _state;
    private readonly RfidReader _reader;
    private readonly RfidGatewayWorker _worker;
    private readonly CareHrApiClient _apiClient;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private readonly Label _lblSdk = new() { AutoSize = true };
    private readonly Label _lblReader = new() { AutoSize = true };
    private readonly Label _lblApi = new() { AutoSize = true };
    private readonly Label _lblInventory = new() { AutoSize = true };
    private readonly Label _lblLastEpc = new() { AutoSize = true };
    private readonly Label _lblLastApi = new() { AutoSize = true };
    private readonly Label _lblReconnect = new() { AutoSize = true };
    private readonly Label _lblError = new() { AutoSize = true, MaximumSize = new Size(420, 0) };

    private readonly Label _lblTestResult = new()
    {
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(6),
        MinimumSize = new Size(420, 80)
    };

    private readonly Label _lblOneTag = new()
    {
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(6),
        MinimumSize = new Size(420, 60)
    };

    private readonly DataGridView _gridApiTest = CreateApiTestGrid();
    private readonly Button _btnTestApi = new() { Text = "Test API", AutoSize = true };

    public StatusForm(
        GatewayState state,
        RfidReader reader,
        RfidGatewayWorker worker,
        CareHrApiClient apiClient)
    {
        _state = state;
        _reader = reader;
        _worker = worker;
        _apiClient = apiClient;

        Text = "CareHR RFID Gateway — Status";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 720);
        ShowInTaskbar = true;
        AppBranding.ApplyFormIcon(this);

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12)
        };

        root.Controls.Add(AppBranding.CreateLogoPictureBox(40));
        root.Controls.Add(Header("Health"));
        root.Controls.Add(_lblSdk);
        root.Controls.Add(_lblReader);
        root.Controls.Add(_lblApi);
        root.Controls.Add(_lblInventory);
        root.Controls.Add(_lblLastEpc);
        root.Controls.Add(_lblLastApi);
        root.Controls.Add(_lblReconnect);
        root.Controls.Add(_lblError);

        root.Controls.Add(Header("Test Reader"));
        var btnTest = new Button { Text = "Test Reader", AutoSize = true };
        btnTest.Click += async (_, _) => await RunTestReaderAsync();
        root.Controls.Add(btnTest);
        root.Controls.Add(_lblTestResult);

        root.Controls.Add(Header("Read One Tag"));
        var btnRead = new Button { Text = "Read One Tag (10s)", AutoSize = true };
        btnRead.Click += async (_, _) => await RunReadOneTagAsync();
        root.Controls.Add(btnRead);
        root.Controls.Add(_lblOneTag);

        root.Controls.Add(Header("Test API"));
        _btnTestApi.Click += async (_, _) => await RunTestApiAsync();
        root.Controls.Add(_btnTestApi);
        root.Controls.Add(_gridApiTest);

        Controls.Add(root);

        _timer.Tick += (_, _) => RefreshStatus();
        Shown += (_, _) =>
        {
            RefreshStatus();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Stop();
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, 12, 0, 4)
    };

    private void RefreshStatus()
    {
        var s = _state.Snapshot;
        _lblSdk.Text = $"SDK: {s.Sdk}";
        _lblReader.Text = $"Reader: {s.Reader}";
        _lblApi.Text = $"API: {s.Api}";
        _lblInventory.Text = $"Inventory: {s.Inventory}";
        _lblLastEpc.Text = $"Last EPC: {(string.IsNullOrWhiteSpace(s.LastEpc) ? "-" : s.LastEpc)}";
        _lblLastApi.Text = $"Last API Time: {(s.LastApiTime?.ToLocalTime().ToString("G") ?? "-")}";
        _lblReconnect.Text = $"Reconnect Count: {s.ReconnectCount}";
        _lblError.Text = string.IsNullOrWhiteSpace(s.LastError) ? "Last Error: -" : $"Last Error: {s.LastError}";
    }

    private async Task RunTestReaderAsync()
    {
        _lblTestResult.Text = "Testing reader (reuse session if connected)...";
        try
        {
            ReaderDeviceInfo info;
            using (_worker.PauseBackgroundLoop())
            {
                info = await Task.Run(() =>
                {
                    if (!_reader.EnsureSdkLoaded())
                    {
                        throw new InvalidOperationException(
                            _reader.SdkLoadError ?? "SDK failed to load.");
                    }

                    // Isolated connectivity check — IP/Port only. No API / ReaderCode / HospitalCode.
                    var openedByTest = false;
                    if (!_reader.IsConnected)
                    {
                        _reader.Connect();
                        openedByTest = true;
                    }

                    try
                    {
                        var device = _reader.GetDeviceInfo();
                        return device with { Status = "PASS" };
                    }
                    finally
                    {
                        // Only tear down a session that this test opened.
                        if (openedByTest)
                        {
                            _reader.Disconnect();
                        }
                    }
                }).ConfigureAwait(true);
            }

            // PauseHandle.Dispose → RequestRestart → Worker resumes inventory on existing or new session.
            _lblTestResult.Text =
                $"Result: PASS{Environment.NewLine}" +
                $"Firmware: {info.Firmware}{Environment.NewLine}" +
                $"Region: {info.Region}{Environment.NewLine}" +
                $"Power: {info.Power}{Environment.NewLine}" +
                $"WorkMode: {info.WorkMode}{Environment.NewLine}" +
                $"Version: {info.Version}{Environment.NewLine}" +
                $"Reader Status: {info.Status}";
            _state.SetFirmware(info.Firmware);
        }
        catch (Exception ex)
        {
            _lblTestResult.Text = $"Result: FAIL{Environment.NewLine}{ex.Message}";
        }
    }

    private async Task RunReadOneTagAsync()
    {
        _lblOneTag.Text = "Waiting for tag (10 seconds)...";
        try
        {
            TagRead? tag;
            using (_worker.PauseBackgroundLoop())
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                tag = await Task.Run(() =>
                {
                    if (!_reader.EnsureSdkLoaded())
                    {
                        throw new InvalidOperationException(
                            _reader.SdkLoadError ?? "SDK failed to load.");
                    }

                    var openedByTest = false;
                    var inventoryStartedByTest = false;

                    if (!_reader.IsConnected)
                    {
                        _reader.Connect();
                        openedByTest = true;
                    }

                    try
                    {
                        if (!_reader.IsInventoryRunning)
                        {
                            _reader.StartInventory();
                            inventoryStartedByTest = true;
                        }

                        try
                        {
                            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                            while (DateTime.UtcNow < deadline)
                            {
                                cts.Token.ThrowIfCancellationRequested();
                                var read = _reader.TryReadTag();
                                if (read is not null)
                                {
                                    return read;
                                }
                            }

                            return null;
                        }
                        finally
                        {
                            if (inventoryStartedByTest)
                            {
                                _reader.StopInventory();
                            }
                        }
                    }
                    finally
                    {
                        if (openedByTest)
                        {
                            _reader.Disconnect();
                        }
                    }
                }, cts.Token).ConfigureAwait(true);
            }

            // PauseHandle.Dispose → RequestRestart → Worker resumes inventory.
            if (tag is null)
            {
                _lblOneTag.Text = "No tag detected.";
                return;
            }

            _lblOneTag.Text =
                $"EPC: {tag.Epc}{Environment.NewLine}" +
                $"RSSI: {tag.Rssi:0.0} dBm{Environment.NewLine}" +
                $"Antenna: {tag.Antenna}{Environment.NewLine}" +
                $"Timestamp: {tag.Timestamp:O}";
        }
        catch (OperationCanceledException)
        {
            _lblOneTag.Text = "No tag detected (timeout).";
        }
        catch (Exception ex)
        {
            _lblOneTag.Text = $"Read failed: {ex.Message}";
        }
    }

    private async Task RunTestApiAsync()
    {
        _btnTestApi.Enabled = false;
        BindApiTestRows([("Status", "Testing GET /api/system/health...")]);
        try
        {
            var result = await _apiClient.TestApiAsync(CancellationToken.None).ConfigureAwait(true);
            BindApiTestResult(result);
        }
        catch (Exception ex)
        {
            BindApiTestRows([("Error", ex.Message)]);
            MessageBox.Show(this, ex.Message, "Test API", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnTestApi.Enabled = true;
        }
    }

    private void BindApiTestResult(SystemHealthResult r)
    {
        var rows = new List<(string Field, string Value)>
        {
            ("API", r.Api),
            ("Database", r.Database),
            ("Authentication", r.Authentication),
            ("HTTP", r.HttpStatusText),
            ("Latency", $"{r.LatencyMs} ms"),
            ("Server Time", r.ServerTime),
            ("Version", r.Version),
            ("Environment", r.Environment),
            ("Uptime", r.Uptime)
        };

        if (!string.IsNullOrWhiteSpace(r.ErrorDetail))
        {
            rows.Add(("Detail", r.ErrorDetail));
        }

        BindApiTestRows(rows);
    }

    private void BindApiTestRows(IEnumerable<(string Field, string Value)> rows)
    {
        _gridApiTest.Rows.Clear();
        foreach (var (field, value) in rows)
        {
            _gridApiTest.Rows.Add(field, value);
        }
    }

    private static DataGridView CreateApiTestGrid()
    {
        var grid = new DataGridView
        {
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            Width = 420,
            Height = 220,
            Margin = new Padding(0, 4, 0, 0),
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Field",
            HeaderText = "Field",
            FillWeight = 35
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            HeaderText = "Value",
            FillWeight = 65
        });
        return grid;
    }
}
