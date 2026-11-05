// 文件职责：设备工厂抽象：定义发现与创建设备实例的统一入口。

using Microsoft.Extensions.Logging;

namespace VisionShell.Core.Interfaces;

/// <summary>
/// Factory for creating device instances of a specific type.
/// </summary>
// 类型职责：设备工厂接口。
public interface IDeviceFactory
{
    /// <summary>
    /// Unique identifier for the device type this factory creates.
    /// </summary>
    string DeviceType { get; }

    /// <summary>
    /// Display name for the device type.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// If true, multiple instances can be created from this factory concurrently (e.g. simulation).
    /// Defaults to false for physical hardware.
    /// </summary>
    bool IsCloneable => false;

    /// <summary>
    /// Validates if the device can be created/connected.
    /// </summary>
    Task<bool> DiscoverAsync(CancellationToken token = default);

    /// <summary>
    /// Creates a new instance of the device.
    /// </summary>
    IDevice CreateDevice(ILogger logger);
}

/// <summary>
/// Optional provider for factories that can enumerate discovered device descriptors (e.g., physical devices).
/// </summary>
// 类型职责：可选能力接口：提供细粒度设备描述列表。
public interface IDeviceDescriptorProvider
{
    IEnumerable<IDeviceDescriptor> GetDescriptors();
}
