namespace CareHR.RfidGateway.Configuration;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string ReaderIp { get; set; } = "192.168.1.200";
    public int ReaderPort { get; set; } = 2022;
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";
    public string ApiEventsPath { get; set; } = "/rfid/events";
    public string ApiToken { get; set; } = string.Empty;
    public string ReaderCode { get; set; } = "GATE-01";
    public string HospitalCode { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int Direction { get; set; } = 1;
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int ReconnectIntervalMs { get; set; } = 3000;
    public int InventoryPollTimeoutMs { get; set; } = 200;
    public int DebounceSeconds { get; set; } = 5;
    public int PresenceTimeoutSeconds { get; set; } = 30;
    public int ApiRetryCount { get; set; } = 3;
    public int ApiRetryBackoffMs { get; set; } = 1000;
    public bool AutoStartWithWindows { get; set; }
    public string LogDirectory { get; set; } = "logs";
}
