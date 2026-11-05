// 文件职责：设备基础抽象：统一连接状态、生命周期和基础属性。

namespace VisionShell.Core.Interfaces;

// 类型职责：设备状态枚举。
public enum DeviceState
{
    Disconnected,
    Connected,
    Error,
    Busy
}

/// <summary>
/// Base interface for all hardware devices.
/// </summary>
// 类型职责：设备基础接口。
public interface IDevice : IDisposable
{
    // Unique, stable identifier for the device (prefer hardware/SDK provided value).
    string DeviceId { get; }
    string Name { get; }
    DeviceState State { get; }
    
    event EventHandler<DeviceState> StateChanged;
    event EventHandler<string> ErrorOccurred;

    Task ConnectAsync(CancellationToken token = default);
    Task DisconnectAsync();
}
