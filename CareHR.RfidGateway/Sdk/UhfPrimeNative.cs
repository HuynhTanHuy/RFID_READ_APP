using System.Runtime.InteropServices;

namespace CareHR.RfidGateway.Sdk;

/// <summary>
/// Native P/Invoke surface for UHFPrimeReader.dll.
/// All DllImport / StructLayout / IntPtr usage for this SDK stays in the Sdk folder.
/// </summary>
internal static class UhfPrimeNative
{
    internal const int ErrorSuccess = 0x00;
    internal const int ErrorOpenFailed = -254;
    internal const int ErrorCmdNoTag = -249;
    internal const int ErrorCmdCommTimeout = -238;
    internal const int ErrorDllDisconnect = -233;
    internal const int ErrorDllUnconnect = -234;

    internal const string SdkVersionLabel = "UHFPrimeReader";

    [DllImport("UHFPrimeReader.dll", CharSet = CharSet.Ansi)]
    internal static extern int OpenNetConnection(out IntPtr handler, string ip, ushort port, uint timeoutMs);

    [DllImport("UHFPrimeReader.dll")]
    internal static extern int CloseDevice(IntPtr handler);

    [DllImport("UHFPrimeReader.dll")]
    internal static extern int GetDevicePara(IntPtr handler, out NativeDevicePara devicePara);

    [DllImport("UHFPrimeReader.dll")]
    internal static extern int InventoryContinue(IntPtr handler, byte invCount, uint invParam);

    [DllImport("UHFPrimeReader.dll")]
    internal static extern int InventoryStop(IntPtr handler, ushort timeout);

    [DllImport("UHFPrimeReader.dll")]
    internal static extern int GetTagUii(IntPtr handler, out NativeTagInfo tagInfo, ushort timeout);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTagInfo
    {
        private ushort _no;
        private short _rssi;
        private byte _antenna;
        private byte _channel;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        private byte[] _crc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        private byte[] _pc;

        private byte _codeLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
        private byte[] _code;

        public short Rssi => _rssi;
        public byte Antenna => _antenna;
        public byte CodeLength => _codeLength;
        public byte[] Code => _code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDevicePara
    {
        private byte _deviceAddr;
        private byte _rfidPro;
        private byte _workMode;
        private byte _interface;
        private byte _baudRate;
        private byte _wgSet;
        private byte _ant;
        private byte _region;
        private ushort _startFreI;
        private ushort _startFreD;
        private ushort _stepFre;
        private byte _cn;
        private byte _rfidPower;
        private byte _inventoryArea;
        private byte _qValue;
        private byte _session;
        private byte _acsAddr;
        private byte _acsDataLen;
        private byte _filterTime;
        private byte _triggleTime;
        private byte _buzzerTime;
        private byte _internalTime;

        public byte Addr => _deviceAddr;
        public byte Protocol => _rfidPro;
        public byte WorkMode => _workMode;
        public byte Region => _region;
        public byte Power => _rfidPower;
    }
}
