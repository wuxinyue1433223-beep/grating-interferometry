// 文件职责：设备描述抽象：用于映射界面展示可选设备条目。

namespace VisionShell.Core.Interfaces;

// 类型职责：设备描述接口。
public interface IDeviceDescriptor
{
    /// <summary>
    /// Unique identifier for discovery/persistence (e.g. Serial Number, or "sample-camera")
    /// </summary>
    string DescriptorId { get; }

    /// <summary>
    /// User friendly name for display
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// The high-level type identifier (e.g. "ICamera", "IStage")
    /// </summary>
    string DeviceTypeIdentifier { get; }

    /// <summary>
    /// The specific factory device type string to use for instantiation.
    /// </summary>
    string FactoryType { get; }

    /// <summary>
    /// If true, multiple instances can be created from this single descriptor (e.g. Simulated/Sample devices).
    /// If false, this represents a unique physical resource (e.g. specific USB camera).
    /// </summary>
    bool IsCloneable { get; }
}
