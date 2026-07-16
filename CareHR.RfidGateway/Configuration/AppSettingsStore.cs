using System.Text.Json;
using System.Text.Json.Nodes;

namespace CareHR.RfidGateway.Configuration;

public sealed class AppSettingsStore(GatewayOptionsAccessor optionsAccessor)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    private readonly object _gate = new();

    public event EventHandler? SettingsChanged;

    public string SettingsPath => _settingsPath;

    public void Save(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (_gate)
        {
            JsonNode root;
            if (File.Exists(_settingsPath))
            {
                var text = File.ReadAllText(_settingsPath);
                root = JsonNode.Parse(text) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            root["Gateway"] = JsonSerializer.SerializeToNode(options, WriteOptions);
            File.WriteAllText(_settingsPath, root.ToJsonString(WriteOptions));
            optionsAccessor.Replace(options);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
