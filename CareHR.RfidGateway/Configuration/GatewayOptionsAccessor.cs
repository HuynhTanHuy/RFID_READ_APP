namespace CareHR.RfidGateway.Configuration;

/// <summary>
/// In-memory options used at runtime. Updated immediately on Settings Save.
/// </summary>
public sealed class GatewayOptionsAccessor
{
    private GatewayOptions _current = new();
    private readonly object _gate = new();

    public GatewayOptions Current
    {
        get
        {
            lock (_gate)
            {
                return Clone(_current);
            }
        }
    }

    public void Replace(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            _current = Clone(options);
        }
    }

    private static GatewayOptions Clone(GatewayOptions source) => new()
    {
        ReaderIp = source.ReaderIp,
        ReaderPort = source.ReaderPort,
        ApiBaseUrl = source.ApiBaseUrl,
        ApiEventsPath = source.ApiEventsPath,
        ApiToken = source.ApiToken,
        ReaderCode = source.ReaderCode,
        HospitalCode = source.HospitalCode,
        DeviceId = source.DeviceId,
        Direction = source.Direction,
        ConnectTimeoutMs = source.ConnectTimeoutMs,
        ReconnectIntervalMs = source.ReconnectIntervalMs,
        InventoryPollTimeoutMs = source.InventoryPollTimeoutMs,
        DebounceSeconds = source.DebounceSeconds,
        PresenceTimeoutSeconds = source.PresenceTimeoutSeconds,
        ApiRetryCount = source.ApiRetryCount,
        ApiRetryBackoffMs = source.ApiRetryBackoffMs,
        AutoStartWithWindows = source.AutoStartWithWindows,
        LogDirectory = source.LogDirectory
    };
}
