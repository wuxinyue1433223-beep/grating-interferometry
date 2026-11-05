using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsSC30Shutter;

public sealed class ThorlabsSC30ShutterFactory : IDeviceFactory, IDeviceDescriptorProvider
{
    private const int TargetVendorId = 0x1313;
    private static readonly int[] TargetProductIds = [0x100C, 0x100E];

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ThorlabsSC30ShutterFactory> _logger;
    private readonly List<IDeviceDescriptor> _descriptors = new();
    private static readonly object PluginManagerSync = new();
    private static bool _pluginManagerOpened;

    private record Descriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

    public ThorlabsSC30ShutterFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ThorlabsSC30ShutterFactory>();
    }

    public string DeviceType => "thorlabs-sc30-shutter";
    public string DisplayName => "Thorlabs SC30 Shutter";
    public bool IsCloneable => false;

    public IEnumerable<IDeviceDescriptor> GetDescriptors() => _descriptors;

    public Task<bool> DiscoverAsync(CancellationToken token = default)
    {
        _descriptors.Clear();

        try
        {
            EnsurePluginManagerOpened();

            var moduleCount = ThorlabsSC30Native.NumAvailablePluginModules();
            for (var moduleIndex = 1; moduleIndex <= moduleCount; moduleIndex++)
            {
                token.ThrowIfCancellationRequested();

                var moduleHandle = ThorlabsSC30Native.GetPluginModule(moduleIndex);
                if (moduleHandle == IntPtr.Zero)
                {
                    continue;
                }

                var vid = ThorlabsSC30Native.ParseHexValue(ThorlabsSC30Native.ReadModuleProperty(moduleHandle, ThorlabsSC30Native.ModuleVendorId));
                var pid = ThorlabsSC30Native.ParseHexValue(ThorlabsSC30Native.ReadModuleProperty(moduleHandle, ThorlabsSC30Native.ModuleProductId));
                if (vid != TargetVendorId || !TargetProductIds.Contains(pid))
                {
                    continue;
                }

                var deviceCount = ThorlabsSC30Native.NumDevices(moduleHandle);
                for (var deviceIndex = 1; deviceIndex <= deviceCount; deviceIndex++)
                {
                    token.ThrowIfCancellationRequested();

                    var deviceHandle = ThorlabsSC30Native.GetPluginDevice(moduleHandle, deviceIndex);
                    if (deviceHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    var serial = ThorlabsSC30Native.ReadDeviceProperty(deviceHandle, ThorlabsSC30Native.DeviceSn);
                    if (string.IsNullOrWhiteSpace(serial))
                    {
                        continue;
                    }

                    var name = ThorlabsSC30Native.ReadDeviceProperty(deviceHandle, ThorlabsSC30Native.DeviceName);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "Thorlabs SC30 Shutter";
                    }

                    var descriptorId = $"tl-sc30:{serial}";
                    _descriptors.Add(new Descriptor(descriptorId, $"{name} ({serial})", "IShutter", DeviceType, false));
                }
            }

            _logger.LogInformation("SC30 设备发现共找到 {Count} 个快门设备", _descriptors.Count);
            return Task.FromResult(_descriptors.Count > 0);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(false);
        }
        catch (BadImageFormatException ex)
        {
            _logger.LogWarning(ex, "SC30 SDK 架构不匹配：当前进程为 {ProcessArch}，TL_DeviceSDK 为 x86；请使用 x86 宿主进程或进程外 x86 桥接", Environment.Is64BitProcess ? "x64" : "x86");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SC30 设备发现失败");
            return Task.FromResult(false);
        }
    }

    public IDevice CreateDevice(ILogger logger)
    {
        return new ThorlabsSC30Shutter(_loggerFactory.CreateLogger<ThorlabsSC30Shutter>());
    }

    internal static void EnsurePluginManagerOpened()
    {
        lock (PluginManagerSync)
        {
            if (_pluginManagerOpened)
            {
                return;
            }

            ThorlabsSC30Native.OpenPluginManager();
            _pluginManagerOpened = true;
        }
    }
}
