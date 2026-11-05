// 文件职责：位移台能力抽象：定义轴向移动、回零、停机等控制接口。

namespace VisionShell.Core.Interfaces;

// 类型职责：位移台轴向枚举。
public enum StageAxis
{
    X, Y, Z, R
}

// 类型职责：默认移动模式枚举（绝对/相对）。
public enum DefaultMoveMode
{
    Absolute,
    Relative
}

/// <summary>
/// Interface for a motion stage controller.
/// </summary>
// 类型职责：位移台设备接口。
public interface IStage : IDevice
{
    bool IsEnabled { get; }
    double Position { get; }
    
    // Capability flags? Or just methods that might throw/log if unsupported.

    Task EnableAsync(bool state);
    Task HomeAsync();
    Task MoveAsync(double value, DefaultMoveMode mode = DefaultMoveMode.Absolute);
    Task StopAsync();
    
    /// <summary>
    /// Get the current position (if not auto-updating via polling/events).
    /// </summary>
    Task<double> GetPositionAsync();
}
