// 文件职责：设备管理抽象：统一插件扫描、设备注册与检索能力。

using System.Collections.Generic;
using System.Threading.Tasks;

namespace VisionShell.Core.Interfaces;

// 类型职责：设备管理接口。
public interface IDeviceManager
{
    IEnumerable<IDeviceFactory> GetAvailableFactories();
    IEnumerable<IDeviceDescriptor> GetDiscoveredDescriptors();
    Task ScanPluginsAsync();
    Task RefreshDevicesAsync(CancellationToken token = default);
    
    TDevice CreateDevice<TDevice>(string deviceType) where TDevice : IDevice;
    
    // Manage active device instances
    void RegisterDevice(IDevice device);
    void UnregisterDevice(IDevice device);
    IEnumerable<T> GetDevices<T>() where T : IDevice;
    Task DisconnectAllAsync();
    Task ShutdownAsync();
}
