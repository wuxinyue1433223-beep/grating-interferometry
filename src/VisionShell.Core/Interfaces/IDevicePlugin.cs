// 文件职责：设备插件抽象：插件初始化与工厂输出契约。

using Microsoft.Extensions.Logging;

namespace VisionShell.Core.Interfaces;

/// <summary>
/// Root interface for a device plugin entry point.
/// </summary>
// 类型职责：插件入口接口。
public interface IDevicePlugin
{
    /// <summary>
    /// Initializes the plugin with host services.
    /// </summary>
    /// <param name="loggerFactory">Host provided logger factory.</param>
    void Initialize(ILoggerFactory loggerFactory);

    /// <summary>
    /// Gets the factories for devices provided by this plugin.
    /// </summary>
    IEnumerable<IDeviceFactory> GetDeviceFactories();
}
