// 文件职责：插件加载器：读取清单、反射入口并回传设备工厂。

// 插件加载器：从 plugins 目录读取 plugin.json，加载插件入口，并返回设备工厂。
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using VisionShell.Core.Interfaces;
using VisionShell.Core.Models;

namespace VisionShell.App.Services;

// 类型职责：插件加载实现，负责清单解析与入口反射实例化。
public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly IRegionManager _regionManager;

    public PluginLoader(ILogger<PluginLoader> logger, ILoggerFactory loggerFactory, IServiceProvider services, IRegionManager regionManager)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _services = services;
        _regionManager = regionManager;
    }

    public IEnumerable<IDeviceFactory> LoadPlugins(string pluginsRootPath)
    {
        var deviceFactories = new List<IDeviceFactory>();

        if (!Directory.Exists(pluginsRootPath))
        {
            _logger.LogWarning("未找到插件目录：{Path}", pluginsRootPath);
            return deviceFactories;
        }

        // 约定 plugins 根目录下每个一级子目录是一个插件目录，且目录根包含 plugin.json。
        // 避免递归扫描把 win-x86/x64 等构建子目录中的重复清单也算进来。
        var pluginManifests = Directory
            .GetDirectories(pluginsRootPath)
            .Select(static d => Path.Combine(d, "plugin.json"))
            .Where(File.Exists)
            .ToList();

        foreach (var manifestPath in pluginManifests)
        {
            try
            {
                var factory = LoadPlugin(manifestPath);
                if (factory != null)
                {
                    deviceFactories.AddRange(factory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 {Path} 加载插件失败", manifestPath);
            }
        }

        return deviceFactories;
    }

    private IEnumerable<IDeviceFactory> LoadPlugin(string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);

        if (manifest == null)
        {
            _logger.LogError("插件清单格式无效：{Path}", manifestPath);
            return Enumerable.Empty<IDeviceFactory>();
        }
var pluginDir = Path.GetDirectoryName(manifestPath)!;
        manifest.PluginDirectory = pluginDir;

        // 当前策略是“遍历目录内所有 dll，找到真正包含 EntryType 的那个程序集”。
        // 这样对插件命名规则的依赖更弱。
        
        var dlls = Directory.GetFiles(pluginDir, "*.dll");
        if (!dlls.Any())
        {
            _logger.LogError("插件目录中没有 DLL：{Dir}", pluginDir);
             return Enumerable.Empty<IDeviceFactory>();
        }

        var loadContext = new PluginLoadContext(pluginDir);
        Assembly? pluginAssembly = null;
        Type? entryType = null;

        foreach (var dll in dlls)
        {
            try
            {
                // 在插件专用的 AssemblyLoadContext 中加载，避免依赖污染主程序上下文。
                var asm = loadContext.LoadFromAssemblyPath(dll);
                _logger.LogDebug("已加载候选插件程序集 {Dll}", dll);
                var t = asm.GetType(manifest.EntryType);
                if (t != null)
                {
                    entryType = t;
                    pluginAssembly = asm;
                    break;
                }
            }
            catch (BadImageFormatException)
            {
                // Native vendor DLLs can exist in plugin folders and are not managed assemblies.
                _logger.LogDebug("托管插件探测时跳过原生 DLL {Dll}", dll);
            }
            catch (Exception ex)
            { 
                _logger.LogWarning(ex, "DLL {Dll} 不包含插件入口或加载失败，已跳过", dll);
            }
        }

        if (pluginAssembly == null || entryType == null)
        {
            _logger.LogError("在目录 {Dir} 的 DLL 中找不到入口类型 {Type}", pluginDir, manifest.EntryType);
            return Enumerable.Empty<IDeviceFactory>();
        }

        if (!typeof(IDevicePlugin).IsAssignableFrom(entryType))
        {
            _logger.LogError("入口类型 {Type} 未实现 IDevicePlugin", manifest.EntryType);
            return Enumerable.Empty<IDeviceFactory>();
        }

        var plugin = (IDevicePlugin)Activator.CreateInstance(entryType)!;

        // 插件只拿到 logger factory，不直接依赖主程序内部服务。
        plugin.Initialize(_loggerFactory);

        _logger.LogInformation("已加载插件：{Name}（{Version}）", manifest.Name, manifest.Version);

        // 插件可以附带区域工厂注册（用于把自己的 View 挂到主窗口区域）。
        try
        {
            var regInterface = typeof(VisionShell.Core.Interfaces.IRegionFactoryRegistration);
            var types = pluginAssembly.GetTypes().Where(t => regInterface.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract).ToList();
            foreach (var t in types)
            {
                try
                {
                    // Create instance via ActivatorUtilities so DI can inject if needed
                    var instance = ActivatorUtilities.CreateInstance(_services, t) as VisionShell.Core.Interfaces.IRegionFactoryRegistration;
                    if (instance != null)
                    {
                        var tuple = instance.CreateFactory(_services);
                        if (RegionManager.TryParseRegionKey(tuple.RegionKey ?? string.Empty, out var region))
                        {
                            _regionManager.RegisterFactory(region, tuple.Factory);
                        }
                        else
                        {
                    _logger.LogWarning("插件 {Type} 提供了未知区域键“{Key}”，已跳过注册", t.FullName, tuple.RegionKey);
                        }
                    }
                }
                catch (Exception ex)
                {
                _logger.LogError(ex, "实例化区域工厂注册 {Type} 失败", t.FullName);
                }
            }
        }
        catch (ReflectionTypeLoadException rex)
        {
            _logger.LogWarning(rex, "扫描插件程序集中的区域注册失败：{Path}", pluginDir);
        }

        return plugin.GetDeviceFactories();
    }

    private PluginManifest? ReadManifest(string manifestPath)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            var jsonBytes = File.ReadAllBytes(manifestPath);
            if (jsonBytes.Length == 0)
            {
            _logger.LogWarning("插件清单为空：{Path}", manifestPath);
                return null;
            }

            // Fast path: UTF-8 JSON.
            try
            {
                return JsonSerializer.Deserialize<PluginManifest>(jsonBytes, options);
            }
            catch (JsonException)
            {
                // Some environments may leave NUL bytes in manifest files temporarily.
                // Sanitize and retry once to avoid failing all plugins in a scan.
                var compact = jsonBytes.Where(static b => b != 0x00).ToArray();
                if (compact.Length == 0)
                {
            _logger.LogWarning("插件清单只包含空字节：{Path}", manifestPath);
                    return null;
                }

                var json = Encoding.UTF8.GetString(compact);
                return JsonSerializer.Deserialize<PluginManifest>(json, options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取插件清单失败：{Path}", manifestPath);
            return null;
        }
    }
}
