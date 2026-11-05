using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsSC30Bridge;

public sealed class ThorlabsSC30BridgePlugin : IDevicePlugin
{
    private ILoggerFactory? _loggerFactory;

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IEnumerable<IDeviceFactory> GetDeviceFactories()
    {
        if (_loggerFactory is null)
        {
            throw new InvalidOperationException("Plugin not initialized.");
        }

        yield return new ThorlabsSC30BridgeFactory(_loggerFactory);
    }
}
