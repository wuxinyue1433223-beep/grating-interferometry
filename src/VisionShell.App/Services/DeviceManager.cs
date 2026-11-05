// 文件职责：设备总管：插件扫描、设备创建注册与统一生命周期管理。

// 设备总管：维护插件工厂列表、已激活设备列表，以及可供 UI 选择的设备描述信息。
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

// Vendor SDK is optional for the host; only referenced in a guarded way.

namespace VisionShell.App.Services;

// 类型职责：设备中心实现，统一管理工厂、设备与生命周期。
public class DeviceManager : IDeviceManager
{
    // 当插件没有提供“真实设备描述”时，用这个轻量描述兜底给映射界面展示。
    // 类型职责：设备描述兜底记录类型。
    private record DeviceDescriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

    private readonly ILogger<DeviceManager> _logger;
    private readonly PluginLoader _pluginLoader;
    private readonly List<IDeviceFactory> _factories = new();
    private readonly List<IDevice> _activeDevices = new();
    private readonly Dictionary<string, List<IDeviceDescriptor>> _factoryDescriptorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _descriptorRefreshSync = new();
    private readonly object _descriptorCacheSync = new();
    private readonly object _shutdownSync = new();
    private Task? _descriptorRefreshTask;
    private Task? _disconnectTask;
    private Task? _shutdownTask;
    private DateTime _descriptorRefreshCompletedUtc = DateTime.MinValue;
    private static readonly TimeSpan DescriptorCacheFreshness = TimeSpan.FromSeconds(10);

    public DeviceManager(ILogger<DeviceManager> logger, PluginLoader pluginLoader)
    {
        _logger = logger;
        _pluginLoader = pluginLoader;
    }

    public async Task ScanPluginsAsync()
    {
        _logger.LogInformation("正在扫描设备插件");

        // Plugin path may be under base/plugins or an ancestor/plugins (e.g. app/win-x86 -> app/plugins).
        var appPath = AppContext.BaseDirectory;
        var pluginsPath = ResolvePluginsPath(appPath) ?? Path.Combine(appPath, "plugins");

        _logger.LogInformation("使用插件目录：{PluginsPath}", pluginsPath);

        await Task.Run(() =>
        {
            var loaded = _pluginLoader.LoadPlugins(pluginsPath);
            lock (_factories)
            {
                _factories.Clear();
                _factories.AddRange(loaded);
            }
        });

        // 首次扫描后先完成一轮描述发现，避免映射界面/一键连接读取到工厂级兜底ID。
        _ = RefreshDevicesAsync();

        _logger.LogInformation("插件扫描完成，共发现 {Count} 个设备工厂", _factories.Count);
    }

    private string? ResolvePluginsPath(string appPath)
    {
        var normalized = appPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new DirectoryInfo(normalized);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Probe current and up to 4 ancestors to support multiple run layouts.
        for (var depth = 0; depth < 5 && current != null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "plugins");
            if (visited.Add(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public Task RefreshDevicesAsync(CancellationToken token = default)
    {
        lock (_descriptorRefreshSync)
        {
            if (_descriptorRefreshTask?.IsCompleted == true
                && DateTime.UtcNow - _descriptorRefreshCompletedUtc < DescriptorCacheFreshness
                && HasCachedDescriptors())
            {
                _logger.LogDebug("复用刚完成的设备发现结果，避免连接前重复枚举硬件");
                return Task.CompletedTask;
            }

            if (_descriptorRefreshTask == null || _descriptorRefreshTask.IsCompleted)
            {
                _descriptorRefreshTask = RefreshDescriptorsCoreAsync(token);
            }

            return _descriptorRefreshTask;
        }
    }

    private bool HasCachedDescriptors()
    {
        lock (_descriptorCacheSync)
        {
            return _factoryDescriptorCache.Values.Any(static descriptors => descriptors.Count > 0);
        }
    }

    private async Task RefreshDescriptorsCoreAsync(CancellationToken token)
    {
        List<IDeviceFactory> factories;
        lock (_factories) factories = _factories.ToList();

        foreach (var factory in factories)
        {
            try
            {
                await factory.DiscoverAsync(token);
                var descriptors = (factory as IDeviceDescriptorProvider)?.GetDescriptors()?.ToList();

                lock (_descriptorCacheSync)
                {
                    if (descriptors != null)
                    {
                        _factoryDescriptorCache[factory.DeviceType] = descriptors;
                    }
                    else
                    {
                        _factoryDescriptorCache.Remove(factory.DeviceType);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备工厂 {Type} 执行发现失败", factory.DeviceType);
                lock (_descriptorCacheSync)
                {
                    _factoryDescriptorCache.Remove(factory.DeviceType);
                }
            }
        }

        _descriptorRefreshCompletedUtc = DateTime.UtcNow;
    }

    public IEnumerable<IDeviceDescriptor> GetDiscoveredDescriptors()
    {
        var list = new List<IDeviceDescriptor>();
        Dictionary<string, List<IDeviceDescriptor>> descriptorSnapshot;
        lock (_descriptorCacheSync)
        {
            descriptorSnapshot = _factoryDescriptorCache.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        lock (_factories)
        {
            foreach (var factory in _factories)
            {
                if (descriptorSnapshot.TryGetValue(factory.DeviceType, out var cached) && cached.Count > 0)
                {
                    list.AddRange(cached);
                    continue;
                }

                if (factory is IDeviceDescriptorProvider)
                {
                    continue;
                }

                var typeId = "IDevice";
                if (factory.DeviceType.Contains("camera", StringComparison.OrdinalIgnoreCase)) typeId = "ICamera";
                else if (factory.DeviceType.Contains("stage", StringComparison.OrdinalIgnoreCase)) typeId = "IStage";
                else if (factory.DeviceType.Contains("shutter", StringComparison.OrdinalIgnoreCase)) typeId = "IShutter";

                list.Add(new DeviceDescriptor(
                    factory.DeviceType,
                    factory.DisplayName,
                    typeId,
                    factory.DeviceType,
                    factory.IsCloneable));
            }
        }

        return list;
    }

    public IEnumerable<IDeviceFactory> GetAvailableFactories()
    {
        lock (_factories) return _factories.ToList();
    }

    public TDevice CreateDevice<TDevice>(string deviceType) where TDevice : IDevice
    {
        IDeviceFactory? factory;
        lock (_factories)
        {
            factory = _factories.FirstOrDefault(f => f.DeviceType == deviceType);
        }

        if (factory == null)
        {
            throw new ArgumentException($"No factory found for device type '{deviceType}'");
        }

        var device = factory.CreateDevice(_logger);

        // If caller is requesting a Thorlabs stage and gave us a specific descriptor id in place of factoryType,
        // it's expected they use descriptor.FactoryType for creation. So no change here.

        if (device is not TDevice typedDevice)
        {
            throw new InvalidOperationException($"Factory created device of type {device.GetType().Name}, expected {typeof(TDevice).Name}");
        }

        return typedDevice;
    }

    public void RegisterDevice(IDevice device)
    {
        lock (_activeDevices) _activeDevices.Add(device);
        _logger.LogInformation("已注册设备：{Id}（{Name}）", device.DeviceId, device.Name);
    }

    public void UnregisterDevice(IDevice device)
    {
        lock (_activeDevices) _activeDevices.Remove(device);
        device.Dispose();
            _logger.LogInformation("已注销设备：{Id}", device.DeviceId);
    }

    public Task ShutdownAsync()
    {
        lock (_shutdownSync)
        {
            if (_shutdownTask == null || _shutdownTask.IsCompleted)
            {
                _shutdownTask = ShutdownCoreAsync();
            }

            return _shutdownTask;
        }
    }

    public Task DisconnectAllAsync()
    {
        lock (_shutdownSync)
        {
            if (_disconnectTask == null || _disconnectTask.IsCompleted)
            {
                _disconnectTask = DisconnectAllCoreAsync();
            }

            return _disconnectTask;
        }
    }

    private async Task DisconnectAllCoreAsync()
    {
        List<IDevice> devices;
        lock (_activeDevices)
        {
            devices = _activeDevices.ToList();
        }

        var ordered = devices
            .Select((device, index) => new { Device = device, Index = index })
            .OrderBy(item => GetShutdownPriority(item.Device))
            .ThenByDescending(item => item.Index)
            .Select(item => item.Device)
            .ToList();

        foreach (var device in ordered)
        {
            if (device.State == DeviceState.Disconnected)
            {
                continue;
            }

            try
            {
                _logger.LogInformation("开始断开设备连接：{Id}（{Type}）", device.DeviceId, device.GetType().Name);
                await Task.Run(async () =>
                {
                    if (device is IStage stage)
                    {
                        try { await stage.StopAsync(); } catch { }
                    }

                    await device.DisconnectAsync();
                });
                _logger.LogInformation("设备连接已断开并保留实例：{Id}", device.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开设备 {Id} 时发生异常", device.DeviceId);
            }
        }
    }

    private async Task ShutdownCoreAsync()
    {
        List<IDevice> toShutdown;
        lock (_activeDevices)
        {
            toShutdown = _activeDevices.ToList();
        }

        var ordered = toShutdown
            .Select((device, index) => new { Device = device, Index = index })
            .OrderBy(item => GetShutdownPriority(item.Device))
            .ThenByDescending(item => item.Index)
            .Select(item => item.Device)
            .ToList();

        foreach (var device in ordered)
        {
            try
            {
                _logger.LogInformation("开始关闭设备：{Id}（{Type}）", device.DeviceId, device.GetType().Name);
                await Task.Run(async () =>
                {
                    // Each hardware driver owns its official stop/disconnect/close order.
                    // Calling Stop here as well caused duplicate native waits during shutdown.
                    try { await device.DisconnectAsync(); } catch { }

                    UnregisterDevice(device);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭设备 {Id} 时发生异常", device.DeviceId);
            }
        }
    }

    private static int GetShutdownPriority(IDevice device)
    {
        if (device is ICamera) return 0;
        if (device.DeviceId.StartsWith("tl-sc30:", StringComparison.OrdinalIgnoreCase)) return 1;
        if (device.DeviceId.StartsWith("kinesis-legacy:", StringComparison.OrdinalIgnoreCase)) return 2;
        if (device is IStage) return 3;
        return 1;
    }

    public IEnumerable<T> GetDevices<T>() where T : IDevice
    {
        lock (_activeDevices)
        {
            return _activeDevices.OfType<T>().ToList();
        }
    }
}
