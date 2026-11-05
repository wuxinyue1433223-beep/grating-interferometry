// 文件职责：主窗口 ViewModel，承载全局状态与启动初始化入口。

// 主窗口级 ViewModel：负责标题等全局状态，并作为区域导航的入口点。
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VisionShell.App.Services;

namespace VisionShell.App.ViewModels;

// 类型职责：主窗口 ViewModel，实现全局初始化状态。
public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IRegionManager _regionManager;

    [ObservableProperty]
    private string _title = "VisionShell";

    public MainViewModel(ILogger<MainViewModel> logger, IRegionManager regionManager)
    {
        _logger = logger;
        _regionManager = regionManager;
    }

    public void Initialize()
    {
        _logger.LogInformation("主窗口模型已初始化");
        // 当前版本由具体 Feature 在自身扩展点中完成区域注册与导航，
        // 这里保留初始化钩子，便于后续加入全局启动逻辑（权限、布局、工作区恢复等）。
    }
}
