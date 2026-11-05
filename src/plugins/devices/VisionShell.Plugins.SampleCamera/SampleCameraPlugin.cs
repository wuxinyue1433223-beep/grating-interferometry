// 文件职责：示例相机插件入口。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.SampleCamera;

// 类型职责：示例相机插件入口。
public class SampleCameraPlugin : IDevicePlugin
{
    private ILoggerFactory? _loggerFactory;

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IEnumerable<IDeviceFactory> GetDeviceFactories()
    {
        if (_loggerFactory == null) throw new InvalidOperationException("Plugin not initialized");
        
        yield return new SampleCameraFactory(_loggerFactory);
    }
}
