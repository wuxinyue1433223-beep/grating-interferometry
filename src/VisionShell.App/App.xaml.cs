// 文件职责：应用生命周期入口：构建宿主、配置日志、启动与退出收尾。

// 应用入口：负责组装宿主、启动插件扫描、初始化默认设备，并在退出时做统一收尾。
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using VisionShell.App.Services;
using VisionShell.App.ViewModels;
using VisionShell.App.Views;
using VisionShell.Core.Interfaces;
using VisionShell.Features.OptiTrack;

namespace VisionShell.App;

// 类型职责：WPF 应用入口类，管理启动与退出生命周期。
public partial class App : Application
{
    private IHost? _host;
    private int _fatalHandled;
    private int _terminateIssued;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterGlobalExceptionHandlers();
        CleanupStaleProcessesOnStartup();

        var startupSw = Stopwatch.StartNew();

        try
        {

            // Build Host
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices((context, services) =>
                {
                    // Core Services
                    services.AddSingleton<PluginLoader>();
                    services.AddSingleton<IDeviceManager, DeviceManager>();
                    services.AddSingleton<IRegionManager, RegionManager>();
                    services.AddSingleton<ISettingsService, SettingsService>();
                    services.AddSingleton<IDeviceMappingStore, DeviceMappingStore>();
                    services.AddSingleton<ICameraService, CameraService>();
                    services.AddSingleton<IStageService, StageService>();
                    services.AddSingleton<UiLogService>();
                    // Removed UiLogLoggerProvider registration as we use Serilog Sink

                    // Windows & ViewModels
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<LogView>();

                    // Feature registrations
                    services.AddOptiTrack();

                })
                .UseSerilog((context, services, configuration) =>
                {
                    var uiLog = services.GetService<UiLogService>();
                    // 日志目录固定到仓库根目录的 logs，避免日志散落到 bin 目录。
                    var logDirectory = ResolveLogDirectory();
                    var logFilePath = Path.Combine(logDirectory, "visionshell-.log");

                    configuration
                        .MinimumLevel.Information()
                        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
                        .WriteTo.Console()
                        .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day);

                    if (uiLog != null)
                    {
                        configuration.WriteTo.Sink(new UiLogSink(uiLog));
                    }
                })
                .Build();

            await _host.StartAsync();

            // Init Services
            var deviceManager = _host.Services.GetRequiredService<IDeviceManager>();

            // 启动阶段先加载插件，再由 DeviceManager 在后台刷新耗时发现，避免首屏白屏等待。
            await deviceManager.ScanPluginsAsync();

            #if DEBUG
            RegisterDefaultSimulationDevices(deviceManager);
            #endif

            // Feature 层可注册自己的区域工厂，主程序在这里统一挂载到区域管理器。
            var regionManager = _host.Services.GetRequiredService<IRegionManager>();
            var regs = _host.Services.GetServices<VisionShell.Core.Interfaces.IRegionFactoryRegistration>();
            foreach (var r in regs)
            {
                var tuple = r.CreateFactory(_host.Services);
                if (RegionManager.TryParseRegionKey(tuple.RegionKey ?? string.Empty, out var region))
                {
                    regionManager.RegisterFactory(region, tuple.Factory);
                }
                else
                {
                    // Fallback: log or ignore unknown region keys.
                }
            }

            // Show Main Window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = mainWindow;
            mainWindow.Closed += OnMainWindowClosed;
            mainWindow.Show();

            Log.Information("程序启动完成，耗时 {ElapsedMs} 毫秒", startupSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            HandleFatalStartupException("startup", ex);
        }
    }

    private void CleanupStaleProcessesOnStartup()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var currentPath = TryGetProcessPath(current);
            var candidates = Process.GetProcessesByName(current.ProcessName)
                .Where(p => p.Id != current.Id)
                .ToList();

            foreach (var process in candidates)
            {
                try
                {
                    var candidatePath = TryGetProcessPath(process);
                    var samePath = !string.IsNullOrWhiteSpace(currentPath)
                        && !string.IsNullOrWhiteSpace(candidatePath)
                        && string.Equals(currentPath, candidatePath, StringComparison.OrdinalIgnoreCase);

                    var sameProductName = string.Equals(process.ProcessName, "VisionShell.App", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase);

                    if (!samePath && !sameProductName)
                    {
                        continue;
                    }

                Log.Warning("正在结束残留进程：PID={Pid}，名称={Name}，路径={Path}", process.Id, process.ProcessName, candidatePath ?? "未知");
                    process.Kill(true);
                    process.WaitForExit(2000);
                }
                catch (Exception ex)
                {
                try { Log.Warning(ex, "结束残留进程失败：PID={Pid}", process.Id); } catch { }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            try { Log.Warning(ex, "启动时清理残留进程失败"); } catch { }
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    #if DEBUG
    private void RegisterDefaultSimulationDevices(IDeviceManager deviceManager)
    {
        var loggerFactory = _host!.Services.GetRequiredService<ILoggerFactory>();
        var angleCam = new VisionShell.Plugins.SampleCamera.SimulatedCamera(
            loggerFactory.CreateLogger("AngleCamera"),
            "Angle Camera",
            "AngleCamera");

        var spotCam = new VisionShell.Plugins.SampleCamera.SimulatedCamera(
            loggerFactory.CreateLogger("SpotCamera"),
            "Spot Camera",
            "SpotCamera");

        deviceManager.RegisterDevice(angleCam);
        deviceManager.RegisterDevice(spotCam);
    }

    #endif

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try { Log.Error(e.Exception, "界面线程发生未处理异常"); } catch { }
        e.Handled = true;
        HandleFatalStartupException("dispatcher", e.Exception);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
        try { Log.Fatal(ex, "应用程序域发生未处理异常（即将终止={IsTerminating}）", e.IsTerminating); } catch { }
            HandleFatalStartupException("domain", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try { Log.Error(e.Exception, "后台任务发生未观察异常"); } catch { }
        e.SetObserved();
    }

    private void HandleFatalStartupException(string source, Exception ex)
    {
        if (Interlocked.Exchange(ref _fatalHandled, 1) == 1)
        {
            return;
        }

        try { Log.Fatal(ex, "程序启动发生致命异常，来源={Source}", source); } catch { }

        try
        {
            MessageBox.Show(
                $"程序启动失败（{source}）。\n{ex.Message}\n请查看 logs 目录日志。",
                "VisionShell 启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // ignore UI notification failure
        }

        // Best-effort hardware cleanup before hard exit, to avoid leaving SDK/device handles locked.
        TryShutdownDevicesWithTimeout(TimeSpan.FromSeconds(3), "fatal");

        try { Current?.Shutdown(-1); } catch { }

        // Ensure process exits even if WPF dispatcher is in unrecoverable state (e.g. OOM).
        ForceTerminateProcess(-1);
    }

    private void TryShutdownDevicesWithTimeout(TimeSpan timeout, string source)
    {
        try
        {
            if (_host == null)
            {
                return;
            }

            var deviceManager = _host.Services.GetService<IDeviceManager>();
            if (deviceManager == null)
            {
                return;
            }

            deviceManager.ShutdownAsync().WaitAsync(timeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException tex)
        {
            try { Log.Warning(tex, "在 {Source} 阶段关闭设备超时", source); } catch { }
        }
        catch (Exception cleanupEx)
        {
            try { Log.Warning(cleanupEx, "在 {Source} 阶段关闭设备失败", source); } catch { }
        }
    }

    private void ForceTerminateProcess(int exitCode)
    {
        if (Interlocked.Exchange(ref _terminateIssued, 1) == 1)
        {
            return;
        }

        try { Environment.Exit(exitCode); } catch { }

        try { Process.GetCurrentProcess().Kill(true); } catch { }
    }

    private static string ResolveLogDirectory()
    {
        // 从运行目录向上回溯，优先找到含 src 或 .sln 的目录作为仓库根目录。
        try
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                var hasSrcFolder = Directory.Exists(Path.Combine(current.FullName, "src"));
                var hasSolutionFile = current.GetFiles("*.sln").Length > 0;
                if (hasSrcFolder || hasSolutionFile)
                {
                    var logDir = Path.Combine(current.FullName, "logs");
                    Directory.CreateDirectory(logDir);
                    return logDir;
                }

                current = current.Parent;
            }
        }
        catch
        {
            // ignore and fallback
        }

        try
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
        catch
        {
            var localFallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisionShell", "logs");
            Directory.CreateDirectory(localFallback);
            return localFallback;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow != null)
        {
            MainWindow.Closed -= OnMainWindowClosed;
        }

        if (_host != null)
        {
            // 先停设备再停宿主，避免相机/位移台线程在宿主释放后还继续回调。
            TryShutdownDevicesWithTimeout(TimeSpan.FromSeconds(3), "exit");

            try
            {
                _host.StopAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            catch (Exception stopEx)
            {
            try { Log.Warning(stopEx, "程序退出时停止主机超时或失败"); } catch { }
            }

            try { _host.Dispose(); } catch { }
        }

        base.OnExit(e);
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        // MainWindow closed means the operator expects immediate full release of all hardware handles.
        TryShutdownDevicesWithTimeout(TimeSpan.FromSeconds(2), "main-window-closed");
        try
        {
            _host?.Dispose();
            _host = null;
        }
        catch
        {
            // ignore cleanup exceptions during close
        }
    }
}
