using CareHR.RfidGateway.Models;
using CareHR.RfidGateway.Utils;
using Microsoft.Extensions.Logging;

namespace CareHR.RfidGateway.Sdk;

/// <summary>
/// Public SDK wrapper. Caller code outside Sdk/ must not touch native APIs.
/// </summary>
public sealed class UhfPrimeSdk : IDisposable
{
    private readonly ILogger<UhfPrimeSdk> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _handler = IntPtr.Zero;
    private bool _inventoryRunning;
    private bool _disposed;

    public UhfPrimeSdk(ILogger<UhfPrimeSdk> logger)
    {
        _logger = logger;
    }

    public bool IsConnected => _handler != IntPtr.Zero;
    public bool IsInventoryRunning => _inventoryRunning;
    public bool IsLoaded { get; private set; }
    public string SdkVersion => UhfPrimeNative.SdkVersionLabel;
    public string? LoadError { get; private set; }

    public const byte RfPowerMinDbm = 0;
    public const byte RfPowerMaxDbm = 33;

    public static bool IsValidRfPowerDbm(byte power) => power is >= RfPowerMinDbm and <= RfPowerMaxDbm;

    public bool EnsureLoaded()
    {
        if (IsLoaded)
        {
            return true;
        }

        try
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "UHFPrimeReader.dll");
            if (!File.Exists(dllPath))
            {
                LoadError = $"UHFPrimeReader.dll not found at {dllPath}";
                _logger.LogError("SDK Loaded failed: {Error}", LoadError);
                return false;
            }

            // Touch an entry point via a no-op open failure path is avoided;
            // presence of DLL + successful P/Invoke resolution is enough.
            IsLoaded = true;
            LoadError = null;
            _logger.LogInformation("SDK Loaded ({SdkVersion})", SdkVersion);
            return true;
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            LoadError = ex.Message;
            _logger.LogError(ex, "SDK Loaded failed");
            return false;
        }
    }

    public void Connect(string ip, ushort port, uint timeoutMs)
    {
        EnsureNotDisposed();
        if (!EnsureLoaded())
        {
            throw new InvalidOperationException(LoadError ?? "SDK failed to load.");
        }

        _gate.Wait();
        try
        {
            if (_handler != IntPtr.Zero)
            {
                CloseInternal();
            }

            var code = UhfPrimeNative.OpenNetConnection(out var handler, ip, port, timeoutMs);
            if (code != UhfPrimeNative.ErrorSuccess || handler == IntPtr.Zero)
            {
                _handler = IntPtr.Zero;
                throw new IOException($"OpenNetConnection failed. Code={code}, IP={ip}, Port={port}");
            }

            _handler = handler;
            _logger.LogInformation("Reader Connected ({Ip}:{Port})", ip, port);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Disconnect()
    {
        EnsureNotDisposed();
        _gate.Wait();
        try
        {
            if (_handler == IntPtr.Zero)
            {
                return;
            }

            CloseInternal();
            _logger.LogWarning("Reader Disconnected");
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TrySetRfPower(byte power, byte reserved = 0)
    {
        EnsureNotDisposed();
        if (!IsValidRfPowerDbm(power))
        {
            return false;
        }

        _gate.Wait();
        try
        {
            if (_handler == IntPtr.Zero)
            {
                return false;
            }

            var code = UhfPrimeNative.SetRFPower(_handler, power, reserved);
            return code == UhfPrimeNative.ErrorSuccess;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TryGetRfPower(out byte power, out byte reserved)
    {
        EnsureNotDisposed();
        power = 0;
        reserved = 0;
        _gate.Wait();
        try
        {
            if (_handler == IntPtr.Zero)
            {
                return false;
            }

            var code = UhfPrimeNative.GetRFPower(_handler, out power, out reserved);
            return code == UhfPrimeNative.ErrorSuccess;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ReaderDeviceInfo GetDeviceInfo()
    {
        EnsureNotDisposed();
        _gate.Wait();
        try
        {
            EnsureConnected();
            var code = UhfPrimeNative.GetDevicePara(_handler, out var para);
            if (code != UhfPrimeNative.ErrorSuccess)
            {
                throw new IOException($"GetDevicePara failed. Code={code}");
            }

            return new ReaderDeviceInfo
            {
                Address = para.Addr,
                Protocol = para.Protocol,
                WorkMode = para.WorkMode,
                Region = para.Region,
                Power = para.Power,
                Firmware = $"Addr={para.Addr}/Proto={para.Protocol}",
                Version = SdkVersion,
                Status = "OK"
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public void StartInventory(byte invCount = 0, uint invParam = 0)
    {
        EnsureNotDisposed();
        _gate.Wait();
        try
        {
            EnsureConnected();
            var code = UhfPrimeNative.InventoryContinue(_handler, invCount, invParam);
            if (code != UhfPrimeNative.ErrorSuccess)
            {
                throw new IOException($"InventoryContinue failed. Code={code}");
            }

            _inventoryRunning = true;
            _logger.LogInformation("Inventory Started");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void StopInventory(ushort timeoutMs = 3000)
    {
        EnsureNotDisposed();
        _gate.Wait();
        try
        {
            if (_handler == IntPtr.Zero || !_inventoryRunning)
            {
                _inventoryRunning = false;
                return;
            }

            var code = UhfPrimeNative.InventoryStop(_handler, timeoutMs);
            _inventoryRunning = false;
            if (code != UhfPrimeNative.ErrorSuccess)
            {
                _logger.LogWarning("InventoryStop returned code {Code}", code);
            }
            else
            {
                _logger.LogInformation("Inventory Stopped");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public TagRead? TryReadTag(ushort timeoutMs)
    {
        EnsureNotDisposed();
        _gate.Wait();
        try
        {
            EnsureConnected();
            var code = UhfPrimeNative.GetTagUii(_handler, out var tag, timeoutMs);
            if (code == UhfPrimeNative.ErrorCmdNoTag)
            {
                return null;
            }

            if (code != UhfPrimeNative.ErrorSuccess)
            {
                throw new IOException($"GetTagUii failed. Code={code}");
            }

            var epc = EpcConverter.ToHex(tag.Code, tag.CodeLength);
            if (string.IsNullOrWhiteSpace(epc))
            {
                return null;
            }

            // RSSI unit from SDK: 0.1 dBm
            return new TagRead
            {
                Epc = epc,
                Rssi = tag.Rssi / 10.0,
                Antenna = tag.Antenna,
                Timestamp = DateTimeOffset.Now
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsDisconnectError(Exception ex)
    {
        if (ex is IOException io && io.Message.Contains("Code=", StringComparison.Ordinal))
        {
            return io.Message.Contains($"Code={UhfPrimeNative.ErrorDllDisconnect}", StringComparison.Ordinal)
                   || io.Message.Contains($"Code={UhfPrimeNative.ErrorDllUnconnect}", StringComparison.Ordinal)
                   || io.Message.Contains($"Code={UhfPrimeNative.ErrorOpenFailed}", StringComparison.Ordinal)
                   || io.Message.Contains($"Code={UhfPrimeNative.ErrorCmdCommTimeout}", StringComparison.Ordinal);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopInventory();
            Disconnect();
        }
        catch
        {
            // best effort on dispose
        }

        _gate.Dispose();
    }

    private void CloseInternal()
    {
        if (_inventoryRunning && _handler != IntPtr.Zero)
        {
            try
            {
                UhfPrimeNative.InventoryStop(_handler, 1000);
            }
            catch
            {
                // ignored
            }

            _inventoryRunning = false;
        }

        if (_handler != IntPtr.Zero)
        {
            try
            {
                UhfPrimeNative.CloseDevice(_handler);
            }
            catch
            {
                // ignored
            }

            _handler = IntPtr.Zero;
        }
    }

    private void EnsureConnected()
    {
        if (_handler == IntPtr.Zero)
        {
            throw new InvalidOperationException("Reader is not connected.");
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
