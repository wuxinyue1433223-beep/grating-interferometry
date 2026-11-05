// 文件职责：示例相机工厂，提供可快速验证流程的模拟相机。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.SampleCamera;

// 类型职责：示例相机工厂。
public class SampleCameraFactory : IDeviceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public SampleCameraFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DeviceType => "simulated-camera";
    public string DisplayName => "Simulated Camera";    public bool IsCloneable => true;
    public Task<bool> DiscoverAsync(CancellationToken token = default)
    {
        // Simulation: Always find one device
        return Task.FromResult(true);
    }

    public IDevice CreateDevice(ILogger logger)
    {
        // We can ignore the passed logger if we want to create a specific one
        // or wrap/use it. Let's use it for the device.
        return new SimulatedCamera(logger);
    }
}
