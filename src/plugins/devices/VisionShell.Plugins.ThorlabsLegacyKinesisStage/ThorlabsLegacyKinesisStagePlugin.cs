using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsLegacyKinesisStage;

public sealed class ThorlabsLegacyKinesisStagePlugin : IDevicePlugin
{
    private ILoggerFactory? _loggerFactory;

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IEnumerable<IDeviceFactory> GetDeviceFactories()
    {
        if (_loggerFactory == null) throw new InvalidOperationException("Plugin not initialized");
        yield return new ThorlabsLegacyKinesisStageFactory(_loggerFactory);
    }
}
