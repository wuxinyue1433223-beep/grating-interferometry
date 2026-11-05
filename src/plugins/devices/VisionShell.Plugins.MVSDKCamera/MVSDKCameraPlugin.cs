// 文件职责：MVSDK 插件入口：向宿主暴露相机工厂。

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.MVSDKCamera
{
    /// <summary>
    /// MVSDK 相机插件入口，向主程序提供设备工厂。
    /// </summary>
    // 类型职责：MVSDK 插件入口，向宿主返回设备工厂。
    public class MVSDKCameraPlugin : IDevicePlugin
    {
        private ILoggerFactory? _loggerFactory;
        private ILogger<MVSDKCameraPlugin>? _logger;

        /// <summary>
        /// 初始化插件并建立日志通道。
        /// </summary>
        /// <param name="loggerFactory">由宿主注入的日志工厂。</param>
        public void Initialize(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
                throw new ArgumentNullException(nameof(loggerFactory));

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<MVSDKCameraPlugin>();

            // 输出一次插件自检信息，方便确认插件是否被宿主正确识别。
            var type = typeof(MVSDKCameraPlugin);

            _logger.LogInformation("MVSDK 插件初始化完成");
            _logger.LogInformation("当前类型: {TypeName}", type.FullName);
            _logger.LogInformation("命名空间: {Namespace}", type.Namespace);
            _logger.LogInformation("是否实现 IDevicePlugin: {Implements}",
                typeof(IDevicePlugin).IsAssignableFrom(type));
        }

        /// <summary>
        /// 返回该插件提供的设备工厂集合。
        /// </summary>
        /// <returns>设备工厂列表。</returns>
        public IEnumerable<IDeviceFactory> GetDeviceFactories()
        {
            if (_loggerFactory == null)
                throw new InvalidOperationException("插件尚未初始化，请先调用 Initialize。");

            _logger?.LogInformation("准备提供 MVSDK 相机工厂");

            // 当前插件只提供一个工厂，后续若支持多型号可扩展为多工厂返回。
            try
            {
                var factory = new MVSDKCameraFactory(_loggerFactory);
                _logger?.LogInformation("工厂创建成功，类型: {FactoryType}",
                    factory.GetType().FullName);

                return new List<IDeviceFactory> { factory };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "创建设备工厂失败");
                throw;
            }
        }
    }
}
