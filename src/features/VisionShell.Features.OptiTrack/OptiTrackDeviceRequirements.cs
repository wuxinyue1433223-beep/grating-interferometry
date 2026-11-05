// 文件职责：声明 OptiTrack 功能所需角色与设备类型约束。

using System.Collections.Generic;
using VisionShell.Core.Interfaces;

namespace VisionShell.Features.OptiTrack;

// 类型职责：定义 OptiTrack 所需角色与硬件类型。
public class OptiTrackDeviceRequirements : IFeatureDeviceRequirements
{
    public string FeatureId => "OptiTrack";

    // Map each role to a required device interface/type identifier.
    // Use "ICamera", "IStage", or "IDevice" (generic) as identifiers.
    public IReadOnlyList<FeatureRole> Roles { get; } = new List<FeatureRole>
    {
        new FeatureRole("Actuator1", "IStage"),
        new FeatureRole("Actuator2", "IStage"),
        new FeatureRole("HorizontalStage", "IStage"),
        new FeatureRole("VerticalStage", "IStage"),
        new FeatureRole("MechanicalShutter", "IShutter"),
        new FeatureRole("AngleCamera", "ICamera"),
        new FeatureRole("SpotCamera", "ICamera")
    };
}
