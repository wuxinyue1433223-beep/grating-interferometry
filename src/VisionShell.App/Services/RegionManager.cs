// 文件职责：区域管理器：维护区域工厂并执行视图导航。

using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;

namespace VisionShell.App.Services;

// 类型职责：主壳已定义的区域枚举。
public enum RegionName
{
    Main,
    Toolbar,
    Status
}

// 类型职责：区域管理抽象，负责注册和导航。
public interface IRegionManager
{
    void RegisterRegion(RegionName name, ContentControl control);
    void Navigate(RegionName region, object view);
    void RegisterFactory(RegionName region, Func<object> viewFactory);
    void Navigate(RegionName region);
}

// 类型职责：区域管理实现，维护工厂字典和当前视图。
public class RegionManager : IRegionManager
{
    private readonly Dictionary<RegionName, ContentControl> _regions = new();
    private readonly ILogger<RegionManager> _logger;
    private readonly Dictionary<RegionName, Func<object>?> _factories = new();

    public RegionManager(ILogger<RegionManager> logger)
    {
        _logger = logger;
    }

    public void RegisterRegion(RegionName name, ContentControl control)
    {
        if (_regions.ContainsKey(name))
        {
            _regions[name] = control;
        }
        else
        {
            _regions.Add(name, control);
        }
    }

    public void Navigate(RegionName region, object view)
    {
        if (_regions.TryGetValue(region, out var control))
        {
            control.Content = view;
            _logger.LogDebug("区域 {Region} 已导航到视图 {View}", region, view.GetType().Name);
        }
        else
        {
            _logger.LogWarning("区域 {Region} 尚未注册", region);
        }
    }

    // Resolve view and optional viewmodel types from the DI container and navigate.
    public void RegisterFactory(RegionName region, Func<object> viewFactory)
    {
        _factories[region] = viewFactory;
    }

    public void Navigate(RegionName region)
    {
        if (!_factories.TryGetValue(region, out var factory) || factory == null)
        {
            _logger.LogWarning("区域 {Region} 没有已注册的视图工厂", region);
            return;
        }

        try
        {
            var view = factory();
            Navigate(region, view);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "区域 {Region} 的视图工厂发生异常", region);
        }
    }

    public static bool TryParseRegionKey(string key, out RegionName region)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            region = default;
            return false;
        }

        // Allow case-insensitive match to enum names
        if (Enum.TryParse<RegionName>(key, true, out var parsed))
        {
            region = parsed;
            return true;
        }

        // Support some common aliases
        switch (key.Trim().ToLowerInvariant())
        {
            case "main":
            case "center":
            case "content":
                region = RegionName.Main;
                return true;
            case "toolbar":
            case "top":
                region = RegionName.Toolbar;
                return true;
            case "status":
            case "bottom":
                region = RegionName.Status;
                return true;
            default:
                region = default;
                return false;
        }
    }
}
