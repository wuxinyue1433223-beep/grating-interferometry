// 文件职责：主窗口行为控制：导航、日志面板与窗口命令处理。

using System.Windows;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VisionShell.App.Services;
using VisionShell.App.ViewModels;
using VisionShell.App.Views;
using VisionShell.Core.Interfaces;
using VisionShell.Features.OptiTrack.ViewModels;

namespace VisionShell.App;

// 类型职责：主窗口代码后置类。
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IRegionManager _regionManager;
    private readonly ILogger<MainWindow> _logger;
    private readonly UiLogService _uiLog;
    private readonly IDeviceMappingStore _mapping_store;
    private readonly IDeviceManager _deviceManager;
    private readonly IEnumerable<VisionShell.Core.Interfaces.IFeatureDeviceRequirements> _featureRequirements;
    private bool _closeInProgress;
    private bool _shutdownCompleted;

    public MainWindow(MainViewModel viewModel, IRegionManager regionManager, ILogger<MainWindow> logger, UiLogService uiLog, IDeviceMappingStore mappingStore, IDeviceManager deviceManager, IEnumerable<VisionShell.Core.Interfaces.IFeatureDeviceRequirements> featureRequirements)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _regionManager = regionManager;
        _logger = logger;
        _uiLog = uiLog;
        _mapping_store = mappingStore;
        _deviceManager = deviceManager;
        _featureRequirements = featureRequirements;

        DataContext = _viewModel;
        
        // Register Regions
        _regionManager.RegisterRegion(RegionName.Main, MainRegion);
        _regionManager.RegisterRegion(RegionName.Toolbar, ToolbarRegion);
        _regionManager.RegisterRegion(RegionName.Status, StatusRegion);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        try
        {
            if (MainRegion.Content is FrameworkElement mainContent
                && mainContent.DataContext is OptiTrackMainViewModel optiTrackVm)
            {
                optiTrackVm.PersistUiStateSettings();
            }

            _logger.LogInformation("主窗口正在关闭，开始停止全部设备");
            await _deviceManager.ShutdownAsync();
            _shutdownCompleted = true;
            _logger.LogInformation("全部设备已安全关闭，正在退出程序");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭窗口时停止设备失败");
            _shutdownCompleted = true;
        }
        finally
        {
            _closeInProgress = false;
            if (_shutdownCompleted)
            {
                // The shutdown task can complete synchronously when no device is active.
                // Queue the second close after the current Closing event has returned.
                _ = Dispatcher.BeginInvoke(
                    new Action(Close),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();

        // Initial navigation
        NavigateOptiTrack_Click(this, new RoutedEventArgs());

        // Log view is hidden by default; user can enable via View->Show Log.
        LogSplitter.Visibility = Visibility.Collapsed;
        StatusRegion.Content = null;
    }

    private void NavigateOptiTrack_Click(object sender, RoutedEventArgs e)
    {
        // Manual wiring for placeholder module
        try 
        {
            _logger.LogInformation("收到导航到 OptiTrack 页面的请求");
            
            // Resolve services manually from App.Host (ugly hack for now until modular DI is clean)
            // Or better: Assume this module is compiled in, so we can just use DI if we registered the VM.
            // Since we didn't register OptiTrack types in App.xaml.cs yet, let's just make it visually work.
            
            // Use registered factory to create and navigate the OptiTrack view
            _regionManager.Navigate(RegionName.Main);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "页面导航失败");
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.LogInformation("收到退出程序请求");
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "退出程序失败");
            try { Application.Current.Shutdown(); } catch { }
        }
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi)
        {
            var show = mi.IsChecked;

            LogRow.Height = show ? new GridLength(120) : new GridLength(0);
            LogSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show)
            {
                _regionManager.Navigate(RegionName.Status, new LogView { DataContext = _uiLog });
            }
            else
            {
                StatusRegion.Content = null;
            }
        }
    }

    private void OpenDeviceMappings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var w = new DeviceMappingsWindow(_mapping_store, _deviceManager, _featureRequirements);
            w.Owner = this;
            w.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开设备映射窗口失败");
        }
    }
}
