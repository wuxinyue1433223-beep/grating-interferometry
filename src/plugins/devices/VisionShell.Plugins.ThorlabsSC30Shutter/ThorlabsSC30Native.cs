using System.Runtime.InteropServices;
using System.Text;

namespace VisionShell.Plugins.ThorlabsSC30Shutter;

internal static class ThorlabsSC30Native
{
    private const string DllName = "TL_DeviceSDK.dll";

    internal const int NoChannel = 0;
    internal const int Channel1 = 1;

    internal const int ModuleVendorId = 2;
    internal const int ModuleProductId = 3;

    internal const int DeviceSn = 5;
    internal const int DeviceName = 6;

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetError(IntPtr objHandle, StringBuilder infoBuffer, int infoBufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void OpenPluginManager();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NumAvailablePluginModules();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetPluginModule(int moduleIndex);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void GetModuleProperty(IntPtr moduleHandle, int propertySel, StringBuilder infoBuffer, ref int infoBufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NumDevices(IntPtr moduleHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetPluginDevice(IntPtr moduleHandle, int deviceIndex);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void GetDeviceProperty(IntPtr deviceHandle, int propertySel, StringBuilder infoBuffer, ref int infoBufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool OpenDevice(IntPtr deviceHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CloseDevice(IntPtr deviceHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr GetParameterHandle(IntPtr deviceHandle, string parameterName);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void GetParameterValueFromDevice(IntPtr hDev, IntPtr hParam, int channelId, byte[] valueOut, int sizeOfValueOut);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetParameterValueToDevice(IntPtr hDev, IntPtr hParam, int channelId, byte[] valueIn, int sizeOfValueIn);

    internal static string GetLastErrorMessage(IntPtr objHandle)
    {
        var buffer = new StringBuilder(512);
        var status = GetError(objHandle, buffer, buffer.Capacity);
        if (status == 0)
        {
            return string.Empty;
        }

        return buffer.ToString().Trim();
    }

    internal static string ReadModuleProperty(IntPtr moduleHandle, int property)
    {
        var size = 512;
        var buffer = new StringBuilder(size);
        GetModuleProperty(moduleHandle, property, buffer, ref size);
        return buffer.ToString().Trim();
    }

    internal static string ReadDeviceProperty(IntPtr deviceHandle, int property)
    {
        var size = 512;
        var buffer = new StringBuilder(size);
        GetDeviceProperty(deviceHandle, property, buffer, ref size);
        return buffer.ToString().Trim();
    }

    internal static int ParseHexValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var normalized = text.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(2);
        }

        return int.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? value
            : 0;
    }
}
