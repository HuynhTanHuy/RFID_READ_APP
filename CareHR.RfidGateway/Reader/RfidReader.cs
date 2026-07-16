using CareHR.RfidGateway.Configuration;
using CareHR.RfidGateway.Models;
using CareHR.RfidGateway.Sdk;
using Microsoft.Extensions.Logging;

namespace CareHR.RfidGateway.Reader;

/// <summary>
/// Reader operations. Does not own reconnect policy — that lives in RfidGatewayWorker.
/// </summary>
public sealed class RfidReader(UhfPrimeSdk sdk, GatewayOptionsAccessor options, ILogger<RfidReader> logger)
{
    private readonly object _opLock = new();

    public bool IsConnected => sdk.IsConnected;
    public bool IsInventoryRunning => sdk.IsInventoryRunning;
    public bool IsSdkLoaded => sdk.IsLoaded;
    public string SdkVersion => sdk.SdkVersion;
    public string? SdkLoadError => sdk.LoadError;

    public bool EnsureSdkLoaded() => sdk.EnsureLoaded();

    public void Connect()
    {
        lock (_opLock)
        {
            var cfg = options.Current;
            if (!sdk.EnsureLoaded())
            {
                throw new InvalidOperationException(sdk.LoadError ?? "SDK failed to load.");
            }

            sdk.Connect(cfg.ReaderIp, (ushort)cfg.ReaderPort, (uint)Math.Max(1, cfg.ConnectTimeoutMs));
            var info = sdk.GetDeviceInfo();
            logger.LogInformation(
                "Reader verified. WorkMode={WorkMode}, Region={Region}, Power={Power}",
                info.WorkMode,
                info.Region,
                info.Power);
        }
    }

    public ReaderDeviceInfo GetDeviceInfo()
    {
        lock (_opLock)
        {
            return sdk.GetDeviceInfo();
        }
    }

    public void Disconnect()
    {
        lock (_opLock)
        {
            try
            {
                if (sdk.IsInventoryRunning)
                {
                    sdk.StopInventory();
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "StopInventory during Disconnect ignored");
            }

            sdk.Disconnect();
        }
    }

    public void StartInventory()
    {
        lock (_opLock)
        {
            sdk.StartInventory();
        }
    }

    public void StopInventory()
    {
        lock (_opLock)
        {
            sdk.StopInventory();
        }
    }

    public TagRead? TryReadTag()
    {
            var timeout = (ushort)Math.Clamp(options.Current.InventoryPollTimeoutMs, 50, 5000);
        lock (_opLock)
        {
            return sdk.TryReadTag(timeout);
        }
    }

    public ReaderDeviceInfo TestReader()
    {
        lock (_opLock)
        {
            var wasInventory = sdk.IsInventoryRunning;
            if (wasInventory)
            {
                sdk.StopInventory();
            }

            try
            {
                if (!sdk.IsConnected)
                {
                    var cfg = options.Current;
                    sdk.Connect(cfg.ReaderIp, (ushort)cfg.ReaderPort, (uint)Math.Max(1, cfg.ConnectTimeoutMs));
                }

                var info = sdk.GetDeviceInfo();
                return info with
                {
                    Status = sdk.IsConnected ? "Connected" : "Disconnected"
                };
            }
            finally
            {
                if (wasInventory && sdk.IsConnected)
                {
                    try
                    {
                        sdk.StartInventory();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to resume inventory after Test Reader");
                    }
                }
            }
        }
    }

    public TagRead? ReadOneTag(TimeSpan waitFor, CancellationToken cancellationToken)
    {
        lock (_opLock)
        {
            var wasInventory = sdk.IsInventoryRunning;
            if (!sdk.IsConnected)
            {
                var cfg = options.Current;
                sdk.Connect(cfg.ReaderIp, (ushort)cfg.ReaderPort, (uint)Math.Max(1, cfg.ConnectTimeoutMs));
            }

            if (!wasInventory)
            {
                sdk.StartInventory();
            }

            try
            {
                var deadline = DateTime.UtcNow + waitFor;
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tag = sdk.TryReadTag(200);
                    if (tag is not null)
                    {
                        return tag;
                    }
                }

                return null;
            }
            finally
            {
                if (!wasInventory)
                {
                    try
                    {
                        sdk.StopInventory();
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "StopInventory after Read One Tag ignored");
                    }
                }
            }
        }
    }

    public bool IsDisconnectError(Exception ex) => sdk.IsDisconnectError(ex);
}
