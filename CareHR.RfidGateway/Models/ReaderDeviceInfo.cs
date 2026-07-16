namespace CareHR.RfidGateway.Models;

public sealed record ReaderDeviceInfo
{
    public byte WorkMode { get; init; }
    public byte Region { get; init; }
    public byte Power { get; init; }
    public byte Protocol { get; init; }
    public byte Address { get; init; }
    public string Firmware { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
