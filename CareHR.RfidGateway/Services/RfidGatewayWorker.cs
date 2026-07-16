using CareHR.RfidGateway.Api;
using CareHR.RfidGateway.Configuration;
using CareHR.RfidGateway.Models;
using CareHR.RfidGateway.Reader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CareHR.RfidGateway.Services;

public sealed class RfidGatewayWorker(
    RfidReader reader,
    CareHrApiClient apiClient,
    TagDebouncer debouncer,
    GatewayState state,
    AppSettingsStore settingsStore,
    GatewayOptionsAccessor options,
    ILogger<RfidGatewayWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _restartGate = new(0, 1);
    private int _pauseCount;
    private readonly object _pauseLock = new();
    private volatile bool _pausedIdle;
    private DateTimeOffset _lastPresenceCleanup = DateTimeOffset.MinValue;
    private static readonly TimeSpan PresenceCleanupInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Application Started");
        settingsStore.SettingsChanged += OnSettingsChanged;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await WaitIfPausedAsync(stoppingToken).ConfigureAwait(false);

                if (!await EnsureConnectedAsync(stoppingToken).ConfigureAwait(false)
                    || stoppingToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    reader.StartInventory();
                    state.SetInventory(InventoryHealth.Running);

                    while (!stoppingToken.IsCancellationRequested && !RestartRequested())
                    {
                        await WaitIfPausedAsync(stoppingToken).ConfigureAwait(false);
                        if (RestartRequested())
                        {
                            break;
                        }

                        TagRead? tag;
                        try
                        {
                            tag = reader.TryReadTag();
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Read loop error");
                            state.SetLastError(ex.Message);
                            state.SetReader(ReaderHealth.Disconnected, ex.Message);
                            state.SetInventory(InventoryHealth.Stopped);
                            SafeDisconnect();
                            await DelayReconnectAsync(stoppingToken).ConfigureAwait(false);
                            break;
                        }

                        if (tag is null)
                        {
                            TryCleanupExpiredPresence();
                            await Task.Delay(20, stoppingToken).ConfigureAwait(false);
                            continue;
                        }

                        state.SetLastEpc(tag.Epc);
                        logger.LogInformation(
                            "Tag Read EPC={Epc} RSSI={Rssi} Antenna={Antenna}",
                            tag.Epc,
                            tag.Rssi,
                            tag.Antenna);

                        debouncer.UpdateLastSeen(tag.Epc, options.Current.PresenceTimeoutSeconds);

                        if (!debouncer.ShouldAccept(tag.Epc, options.Current.DebounceSeconds))
                        {
                            continue;
                        }

                        if (!debouncer.ShouldSendPresence(tag.Epc, options.Current.PresenceTimeoutSeconds))
                        {
                            continue;
                        }

                        await apiClient.SendTagAsync(tag, stoppingToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    SafeStopInventory();
                }

                if (RestartRequested())
                {
                    ConsumeRestart();
                    logger.LogInformation("Restart Reader requested");
                    debouncer.ClearPresence();
                    SafeDisconnect();
                    state.SetReader(ReaderHealth.Disconnected);
                }
            }
        }
        finally
        {
            settingsStore.SettingsChanged -= OnSettingsChanged;
            SafeStopInventory();
            SafeDisconnect();
            state.SetInventory(InventoryHealth.Stopped);
            state.SetReader(ReaderHealth.Disconnected);
        }
    }

    public void RequestRestart()
    {
        try
        {
            if (_restartGate.CurrentCount == 0)
            {
                _restartGate.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // already signaled
        }
    }

    public IDisposable PauseBackgroundLoop()
    {
        lock (_pauseLock)
        {
            _pauseCount++;
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!_pausedIdle && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        return new PauseHandle(this);
    }

    private void ResumeBackgroundLoop()
    {
        lock (_pauseLock)
        {
            if (_pauseCount > 0)
            {
                _pauseCount--;
            }
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (!IsPaused())
        {
            _pausedIdle = false;
            return;
        }

        SafeStopInventory();
        while (IsPaused() && !cancellationToken.IsCancellationRequested)
        {
            _pausedIdle = true;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        _pausedIdle = false;
    }

    private bool IsPaused()
    {
        lock (_pauseLock)
        {
            return _pauseCount > 0;
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (reader.IsConnected)
        {
            state.SetReader(ReaderHealth.Connected);
            return true;
        }

        if (!reader.EnsureSdkLoaded())
        {
            state.SetSdk(SdkHealth.Failed, reader.SdkLoadError);
            state.SetReader(ReaderHealth.Disconnected, reader.SdkLoadError);
            await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        state.SetSdk(SdkHealth.Loaded);
        state.SetSdkVersion(reader.SdkVersion);
        state.SetReader(ReaderHealth.Reconnecting);

        try
        {
            reader.Connect();
            var info = reader.GetDeviceInfo();
            state.SetFirmware(info.Firmware);
            state.SetReader(ReaderHealth.Connected);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reconnect");
            state.SetReader(ReaderHealth.Reconnecting, ex.Message);
            state.SetLastError(ex.Message);
            SafeDisconnect();
            await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task DelayReconnectAsync(CancellationToken cancellationToken)
    {
        state.IncrementReconnect();
        logger.LogWarning("Reconnect");
        var delay = Math.Max(500, options.Current.ReconnectIntervalMs);
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        logger.LogInformation("Settings Changed");
        RequestRestart();
    }

    private bool RestartRequested() => _restartGate.CurrentCount > 0;

    private void ConsumeRestart()
    {
        if (_restartGate.CurrentCount > 0)
        {
            _restartGate.Wait(0);
        }
    }

    private void SafeStopInventory()
    {
        try
        {
            reader.StopInventory();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "StopInventory ignored");
        }

        state.SetInventory(InventoryHealth.Stopped);
    }

    private void SafeDisconnect()
    {
        try
        {
            reader.Disconnect();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Disconnect ignored");
        }
    }

    private void TryCleanupExpiredPresence()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPresenceCleanup < PresenceCleanupInterval)
        {
            return;
        }

        _lastPresenceCleanup = now;
        debouncer.RemoveExpiredPresence(options.Current.PresenceTimeoutSeconds);
    }

    private sealed class PauseHandle(RfidGatewayWorker owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ResumeBackgroundLoop();
                owner.RequestRestart();
            }
        }
    }
}
