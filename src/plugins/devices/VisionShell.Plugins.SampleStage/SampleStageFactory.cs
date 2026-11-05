// 文件职责：示例位移台工厂，创建模拟轴设备。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.SampleStage;

// 类型职责：示例位移台工厂。
public class SampleStageFactory : IDeviceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public SampleStageFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DeviceType => "simulated-stage";
    public string DisplayName => "Simulated Stage Controller";
    public bool IsCloneable => true;

    public Task<bool> DiscoverAsync(CancellationToken token = default)
    {
        return Task.FromResult(true);
    }

    public IDevice CreateDevice(ILogger logger)
    {
        return new SimulatedStage(logger);
    }
}
