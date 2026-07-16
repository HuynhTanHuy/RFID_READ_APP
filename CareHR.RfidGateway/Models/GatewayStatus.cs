namespace CareHR.RfidGateway.Models;

public enum SdkHealth
{
    Unknown,
    Loaded,
    Failed
}

public enum ReaderHealth
{
    Disconnected,
    Connected,
    Reconnecting
}

public enum ApiHealth
{
    Unknown,
    Online,
    Offline
}

public enum InventoryHealth
{
    Stopped,
    Running
}

public sealed record GatewayStatus
{
    public SdkHealth Sdk { get; init; } = SdkHealth.Unknown;
    public ReaderHealth Reader { get; init; } = ReaderHealth.Disconnected;
    public ApiHealth Api { get; init; } = ApiHealth.Unknown;
    public InventoryHealth Inventory { get; init; } = InventoryHealth.Stopped;
    public string LastEpc { get; init; } = string.Empty;
    public DateTimeOffset? LastApiTime { get; init; }
    public int ReconnectCount { get; init; }
    public string LastError { get; init; } = string.Empty;
    public string Firmware { get; init; } = string.Empty;
    public string SdkVersion { get; init; } = "UHFPrimeReader";
}
