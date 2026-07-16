namespace CareHR.RfidGateway.Models;

public sealed class TagRead
{
    public required string Epc { get; init; }
    public double Rssi { get; init; }
    public byte Antenna { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}
