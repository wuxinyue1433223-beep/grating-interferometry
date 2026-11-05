// 文件职责：Thorlabs 位移台插件入口。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsKinesisStage;

// 类型职责：Thorlabs 位移台插件入口。
public sealed class ThorlabsKinesisStagePlugin : IDevicePlugin
{
 private ILoggerFactory? _loggerFactory;

 public void Initialize(ILoggerFactory loggerFactory)
 {
 _loggerFactory = loggerFactory;
 }

 public IEnumerable<IDeviceFactory> GetDeviceFactories()
 {
 if (_loggerFactory == null) throw new InvalidOperationException("Plugin not initialized");
 yield return new ThorlabsStageFactory(_loggerFactory);
 }
}
