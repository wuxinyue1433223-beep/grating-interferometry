// 文件职责：Feature 设备需求抽象：声明业务角色与设备类型匹配规则。

using System.Collections.Generic;

namespace VisionShell.Core.Interfaces;

// 类型职责：Feature 所需单个角色定义。
public record FeatureRole(string Name, string DeviceType);

// 类型职责：Feature 设备需求接口。
public interface IFeatureDeviceRequirements
{
    /// <summary>
    /// Unique feature id (e.g., "OptiTrack")
    /// </summary>
    string FeatureId { get; }

    /// <summary>
    /// Roles required by this feature (ordered or named roles) with required device type identifier (e.g., "ICamera", "IStage", "IDevice")
    /// </summary>
    IReadOnlyList<FeatureRole> Roles { get; }
}
