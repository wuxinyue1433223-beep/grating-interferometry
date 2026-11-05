using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsSC30Shutter;

public sealed class ThorlabsSC30ShutterPlugin : IDevicePlugin
{
    private ILoggerFactory? _loggerFactory;

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IEnumerable<IDeviceFactory> GetDeviceFactories()
    {
        if (_loggerFactory == null)
        {
            throw new InvalidOperationException("Plugin not initialized");
        }

        yield return new ThorlabsSC30ShutterFactory(_loggerFactory);
    }
}
