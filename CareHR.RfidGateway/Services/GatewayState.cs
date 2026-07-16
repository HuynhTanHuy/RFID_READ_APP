using CareHR.RfidGateway.Models;

namespace CareHR.RfidGateway.Services;

public sealed class GatewayState
{
    private readonly object _gate = new();
    private GatewayStatus _snapshot = new();

    public event EventHandler? Changed;

    public GatewayStatus Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void SetSdk(SdkHealth health, string? error = null)
    {
        Update(s => s with
        {
            Sdk = health,
            LastError = error ?? s.LastError,
            SdkVersion = s.SdkVersion
        });
    }

    public void SetSdkVersion(string sdkVersion)
    {
        Update(s => s with { SdkVersion = sdkVersion });
    }

    public void SetReader(ReaderHealth health, string? error = null)
    {
        Update(s => s with
        {
            Reader = health,
            LastError = error ?? (health == ReaderHealth.Connected ? string.Empty : s.LastError)
        });
    }

    public void SetInventory(InventoryHealth health)
    {
        Update(s => s with { Inventory = health });
    }

    public void SetApi(ApiHealth health, DateTimeOffset? lastApiTime = null)
    {
        Update(s => s with
        {
            Api = health,
            LastApiTime = lastApiTime ?? s.LastApiTime
        });
    }

    public void SetLastEpc(string epc)
    {
        Update(s => s with { LastEpc = epc });
    }

    public void IncrementReconnect()
    {
        Update(s => s with { ReconnectCount = s.ReconnectCount + 1 });
    }

    public void SetFirmware(string firmware)
    {
        Update(s => s with { Firmware = firmware });
    }

    public void SetLastError(string error)
    {
        Update(s => s with { LastError = error });
    }

    private void Update(Func<GatewayStatus, GatewayStatus> mutator)
    {
        lock (_gate)
        {
            _snapshot = mutator(_snapshot);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
