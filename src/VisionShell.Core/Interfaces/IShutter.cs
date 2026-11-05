// 文件职责：机械快门抽象：提供开/关控制能力。

namespace VisionShell.Core.Interfaces;

// 类型职责：机械快门控制接口。
public interface IShutter : IDevice
{
    bool IsOpen { get; }

    Task OpenAsync(CancellationToken token = default);
    Task CloseAsync(CancellationToken token = default);
    Task SetOpenAsync(bool isOpen, CancellationToken token = default);
}
