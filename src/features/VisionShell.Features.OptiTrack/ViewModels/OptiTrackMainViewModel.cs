// 鏂囦欢鑱岃矗锛歄ptiTrack 涓绘祦绋嬬紪鎺掞細璁惧缁戝畾銆佸浘鍍忓鐞嗐€佷綅绉绘帶鍒朵笌鍙傛暟鎸佷箙鍖栥€?

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using System.Text.Json;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;
using VisionShell.Core.Models;
using VisionShell.Features.OptiTrack.Analysis;
using System.Windows.Threading;
using System.Diagnostics;

namespace VisionShell.Features.OptiTrack.ViewModels;

// OptiTrack 涓绘祦绋嬫帶鍒跺櫒锛?
// 1) 绠＄悊鐩告満/浣嶇Щ鍙拌鑹茬粦瀹氾紝2) 鎺ユ敹骞舵樉绀哄浘鍍忥紝3) 鎵ц鏉＄汗鍒嗘瀽锛?) 鎸佷箙鍖栧弬鏁板苟椹卞姩鑷姩璋冭妭銆?
// 绫诲瀷鑱岃矗锛氱紪鎺?OptiTrack 鍏ㄩ摼璺氦浜掞細璁惧杩炴帴銆佸抚澶勭悊銆佹帶鍒跺懡浠や笌鍙傛暟淇濆瓨銆?
public partial class OptiTrackMainViewModel : ObservableObject
{
    private readonly ILogger<OptiTrackMainViewModel> _logger;
    private readonly IDeviceManager _deviceManager;
    private readonly IDeviceMappingStore _mappingStore;
    private readonly IFeatureDeviceRequirements _requirements;

    // Device Instances
    private IStage? _actuator1;
    private IStage? _actuator2;
    private IStage? _verticalStage;
    private IStage? _horizontalStage;
    private IDevice? _mechanicalShutter;
    private ICamera? _angleCamera;
    private ICamera? _spotCamera;
    
    // Currently selected camera for display/control
    private ICamera? _currentCamera;
    private readonly SemaphoreSlim _cameraSwitchGate = new(1, 1);
    private int _connectToggleInFlight;
    private const int CameraSwitchCooldownMs = 3000;

    [ObservableProperty]
    private string? _activeCameraName;

    [ObservableProperty]
    private bool _isActiveCameraConnected;

    [ObservableProperty]
    private bool _isActuator1Connected;

    [ObservableProperty]
    private bool _isActuator2Connected;

    [ObservableProperty]
    private bool _isVerticalStageConnected;

    [ObservableProperty]
    private bool _isHorizontalStageConnected;

    [ObservableProperty]
    private bool _isMechanicalShutterConnected;

    [ObservableProperty]
    private bool _isAngleCameraConnected;

    [ObservableProperty]
    private bool _isSpotCameraConnected;

    [ObservableProperty]
    private bool _hasAnyDeviceConnected;

    [ObservableProperty]
    private bool _isConnectBusy;

    [ObservableProperty]
    private bool _isCameraSwitchCoolingDown;

    public string ConnectAllButtonText => HasAnyDeviceConnected ? "一键断开" : "一键连接";

    public string ActiveCameraConnectionText => IsActiveCameraConnected ? "Connected" : "Disconnected";

    public bool CanSwitchCamera => !IsCameraSwitchCoolingDown;

    public Brush ActiveCameraConnectionBrush => IsActiveCameraConnected ? Brushes.LimeGreen : Brushes.Red;

    partial void OnIsActiveCameraConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveCameraConnectionText));
        OnPropertyChanged(nameof(ActiveCameraConnectionBrush));
    }

    partial void OnHasAnyDeviceConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectAllButtonText));
    }

    partial void OnIsCameraSwitchCoolingDownChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSwitchCamera));
    }
    
    [ObservableProperty]
    private BitmapSource? _activeFrame;
    
    // Backing writeable bitmap and buffer to reuse across frames
    private System.Windows.Media.Imaging.WriteableBitmap? _writeableBitmap;
    private byte[]? _backBuffer;
    private long _lastUiFrameTickMs;
    private int _uiRenderQueued;
    private const int UiFrameIntervalMs = 33;
    private ImageFrame? _latestPendingFrame;
    private int _frameProcessorRunning;
    private byte[]? _analysisBuffer;
    private int _analysisBufferWidth;
    private int _analysisBufferHeight;
    private int _analysisWorkerRunning;
    private long _analysisWorkerStartTickMs;
    private long _lastAnalysisTickMs;
    private int _analysisIntervalMs = DefaultAnalysisIntervalMs;
    private bool _enableProcessingDiagnostics;
    private const int AnalysisTimeoutMs = 2000;
    private const int DefaultAnalysisIntervalMs = 1000;
    private const int MinAnalysisIntervalMs = 50;
    private const int MaxAnalysisIntervalMs = 60000;
    private const string ProcessingSettingsFileName = "optitrack-processing-settings.json";
    private const string UiStateSettingsFileName = "optitrack-ui-state-settings.json";
    private long _frameEventSeq;
    private long _analysisSeq;
    private string? _uiStateSettingsPath;
    private double _angleCameraExposureMs = 10.0;
    private double _spotCameraExposureMs = 10.0;

    private bool _isFrameFrozen;
    private bool _isSnapPending;

    // Use string for input to allow decimal points during typing withut auto-formatting interference
    private string _exposureTimeInput = "10";
    public string ExposureTimeInput
    {
        get => _exposureTimeInput;
        set => SetProperty(ref _exposureTimeInput, value);
    }

    [ObservableProperty]
    private double _actuator1StepSize;
    [ObservableProperty]
    private double _actuator1Position;

    [ObservableProperty]
    private double _actuator2StepSize;
    [ObservableProperty]
    private double _actuator2Position;

    [ObservableProperty]
    private double _verticalStageStepSize;
    [ObservableProperty]
    private double _verticalStagePosition;

    [ObservableProperty]
    private double _horizontalStageStepSize;
    [ObservableProperty]
    private double _horizontalStagePosition;
    
    [ObservableProperty]
    private bool _isMasterSwitchOn;

    // 蹇棬瀹氭椂寮€鍚椂闀匡紙绉掞級锛屾敮鎸佺敤鎴疯緭鍏ュ皬鏁般€?
    private string _timedShutterOpenSecondsInput = "1.0";
    public string TimedShutterOpenSecondsInput
    {
        get => _timedShutterOpenSecondsInput;
        set => SetProperty(ref _timedShutterOpenSecondsInput, value);
    }

    private CancellationTokenSource? _timedShutterCts;

    [ObservableProperty]
    private string _timedShutterRemainingSecondsText = "-";

    [ObservableProperty]
    private bool _isTimedShutterCountingDown;

    // Controls which device control panels are visible in the UI
    [ObservableProperty]
    private bool _showActuator1 = true;

    [ObservableProperty]
    private bool _showActuator2 = true;

    [ObservableProperty]
    private bool _showVerticalStage = true;

    [ObservableProperty]
    private bool _showHorizontalStage = true;

    [ObservableProperty]
    private bool _showMechanicalShutter = true;

    // Cameras: allow enabling connection for both
    [ObservableProperty]
    private bool _showAngleCamera = true;

    [ObservableProperty]
    private bool _showSpotCamera = true;

    // Which camera's image is currently displayed
    [ObservableProperty]
    private bool _isAngleCameraSelected = true;

    [ObservableProperty]
    private bool _isSpotCameraSelected;

    [ObservableProperty]
    private bool _isImageProcessingEnabled = true;

    [ObservableProperty]
    private string _captureModeText = "连续采集";

    [ObservableProperty]
    private double _stripeAngleDeg;

    [ObservableProperty]
    private double _stripePeriodPixels;

    public double StripePeriodUm => StripePeriodPixels * 3.7;

    public string StripePeriodUmText => FormatWithFixed2Decimals(StripePeriodUm);

    public string StripeAngleDegSig2Text => FormatWithFixed2Decimals(StripeAngleDeg);

    partial void OnStripePeriodPixelsChanged(double value)
    {
     OnPropertyChanged(nameof(StripePeriodUm));
     OnPropertyChanged(nameof(StripePeriodUmText));
    }

    partial void OnStripeAngleDegChanged(double value)
    {
        OnPropertyChanged(nameof(StripeAngleDegSig2Text));
    }

    [ObservableProperty]
    private int _maxValue95;

    [ObservableProperty]
    private double _currentAngle;

    // 闇€瑕佽嚜瀹氫箟鐨勬爣棰樺睘鎬э紙涓嶇敤 ObservableProperty锛屼究浜庤嚜瀹氫箟 set 閫昏緫锛?
    private string _actuator1Header = "促动器1控制";
    public string Actuator1HeaderText
    {
        get => _actuator1Header;
        set => SetProperty(ref _actuator1Header, value);
    }

    private string _actuator2Header = "促动器2控制";
    public string Actuator2HeaderText
    {
        get => _actuator2Header;
        set => SetProperty(ref _actuator2Header, value);
    }

    private string _verticalHeader = "垂直位移台控制";
    public string VerticalHeaderText
    {
        get => _verticalHeader;
        set => SetProperty(ref _verticalHeader, value);
    }

    private string _horizontalHeader = "水平位移台控制";
    public string HorizontalHeaderText
    {
        get => _horizontalHeader;
        set => SetProperty(ref _horizontalHeader, value);
    }

    // 鍚勮酱缁濆浣嶇Щ鐩爣锛埼糾锛?
    private double _actuator1TargetMm;
    public double Actuator1TargetMm
    {
        get => _actuator1TargetMm;
        set => SetProperty(ref _actuator1TargetMm, value);
    }

    private double _actuator2TargetMm;
    public double Actuator2TargetMm
    {
        get => _actuator2TargetMm;
        set => SetProperty(ref _actuator2TargetMm, value);
    }

    private double _verticalTargetMm;
    public double VerticalTargetMm
    {
        get => _verticalTargetMm;
        set => SetProperty(ref _verticalTargetMm, value);
    }

    private double _horizontalTargetMm;
    public double HorizontalTargetMm
    {
        get => _horizontalTargetMm;
        set => SetProperty(ref _horizontalTargetMm, value);
    }

    // 鐢ㄦ埛杈撳叆鐨勭洰鏍囧懆鏈燂紙瀛楃涓诧紝渚夸簬杈撳叆涓棿鎬侊級
    private string _targetPeriodInput = "0.0";
    public string TargetPeriodInput
    {
        get => _targetPeriodInput;
        set => SetProperty(ref _targetPeriodInput, value);
    }

    private readonly DispatcherTimer _positionPollTimer = new();
    private readonly SemaphoreSlim _actuator1PollGate = new(1, 1);
    private readonly SemaphoreSlim _actuator2PollGate = new(1, 1);
    private readonly SemaphoreSlim _verticalPollGate = new(1, 1);
    private readonly SemaphoreSlim _horizontalPollGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _activeHomeOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _autoHomeAfterConnect;

    private const double ActuatorAbsoluteMinMm = 0.0;
    private const double ActuatorAbsoluteMaxMm = 6.0;
    private const double ActuatorRelativeMinReachMm = -0.02;

    public OptiTrackMainViewModel(
        ILogger<OptiTrackMainViewModel> logger,
        IDeviceManager deviceManager,
        IDeviceMappingStore mappingStore,
        IFeatureDeviceRequirements requirements)
    {
        _logger = logger;
        _deviceManager = deviceManager;
        _mappingStore = mappingStore;
        _requirements = requirements;

        _positionPollTimer.Interval = TimeSpan.FromMilliseconds(100);
        _positionPollTimer.Tick += async (_, _) => await PollStagePositionsAsync();

        _autoHomeAfterConnect = string.Equals(
            Environment.GetEnvironmentVariable("VISION_AUTO_HOME_ON_CONNECT"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        LoadUiStateSettings();
        LoadProcessingSettings();

        _logger.LogInformation("OptiTrack 主界面模型已创建");
    }

    private void LoadUiStateSettings()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, UiStateSettingsFileName),
                Path.Combine(FindRepoRootContainingSrc(AppContext.BaseDirectory), UiStateSettingsFileName)
            };

            string? settingsPath = null;
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    settingsPath = candidate;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                _uiStateSettingsPath = candidates[0];
                return;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<UiStateSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UiStateSettings();

            _uiStateSettingsPath = settingsPath;

            _angleCameraExposureMs = SanitizePositive(settings.AngleCameraExposureMs, 10.0);
            _spotCameraExposureMs = SanitizePositive(settings.SpotCameraExposureMs, 10.0);

            Actuator1StepSize = SanitizeFinite(settings.Actuator1StepSizeMm ?? ToMm(settings.Actuator1StepSizeUm));
            Actuator2StepSize = SanitizeFinite(settings.Actuator2StepSizeMm ?? ToMm(settings.Actuator2StepSizeUm));
            VerticalStageStepSize = SanitizeFinite(settings.VerticalStageStepSizeMm ?? ToMm(settings.VerticalStageStepSizeUm));
            HorizontalStageStepSize = SanitizeFinite(settings.HorizontalStageStepSizeMm ?? ToMm(settings.HorizontalStageStepSizeUm));

            var shutterSeconds = SanitizePositive(settings.TimedShutterOpenSeconds, 1.0);
            TimedShutterOpenSecondsInput = shutterSeconds.ToString(CultureInfo.CurrentCulture);

            // Default selection is AngleCamera, so initialize input from angle camera exposure.
            ExposureTimeInput = _angleCameraExposureMs.ToString(CultureInfo.CurrentCulture);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 OptiTrack 界面状态设置失败，将使用默认值");
        }
    }

    public void PersistUiStateSettings()
    {
        try
        {
            // Save the current exposure input back to the active camera slot before persisting.
            CaptureCurrentCameraExposureFromInput();

            var settings = new UiStateSettings
            {
                AngleCameraExposureMs = SanitizePositive(_angleCameraExposureMs, 10.0),
                SpotCameraExposureMs = SanitizePositive(_spotCameraExposureMs, 10.0),
                Actuator1StepSizeMm = SanitizeFinite(Actuator1StepSize),
                Actuator2StepSizeMm = SanitizeFinite(Actuator2StepSize),
                VerticalStageStepSizeMm = SanitizeFinite(VerticalStageStepSize),
                HorizontalStageStepSizeMm = SanitizeFinite(HorizontalStageStepSize),
                TimedShutterOpenSeconds = ParsePositiveOrDefault(TimedShutterOpenSecondsInput, 1.0)
            };

            var path = _uiStateSettingsPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, UiStateSettingsFileName);
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path!, json);
            _uiStateSettingsPath = path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存 OptiTrack 界面状态设置失败");
        }
    }

    private void CaptureCurrentCameraExposureFromInput()
    {
        if (!double.TryParse(ExposureTimeInput, NumberStyles.Float, CultureInfo.CurrentCulture, out var ms)
            && !double.TryParse(ExposureTimeInput, NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
        {
            return;
        }

        ms = SanitizePositive(ms, 10.0);
        if (ReferenceEquals(_currentCamera, _angleCamera) || IsAngleCameraSelected)
        {
            _angleCameraExposureMs = ms;
        }
        else if (ReferenceEquals(_currentCamera, _spotCamera) || IsSpotCameraSelected)
        {
            _spotCameraExposureMs = ms;
        }
    }

    private void ApplySelectedCameraExposure(string cameraType)
    {
        if (_currentCamera == null)
        {
            return;
        }

        var fallbackMs = SanitizePositive(_currentCamera.ExposureTimeUs / 1000.0, 10.0);
        var selectedMs = string.Equals(cameraType, "SpotCamera", StringComparison.OrdinalIgnoreCase)
            ? SanitizePositive(_spotCameraExposureMs, fallbackMs)
            : SanitizePositive(_angleCameraExposureMs, fallbackMs);

        ExposureTimeInput = selectedMs.ToString(CultureInfo.CurrentCulture);
        try
        {
            _currentCamera.ExposureTimeUs = selectedMs * 1000.0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "向相机 {Type} 应用已保存的曝光参数失败", cameraType);
        }
    }

    private static double SanitizeFinite(double value)
    {
        return double.IsFinite(value) ? value : 0.0;
    }

    private static double SanitizePositive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private static double ParsePositiveOrDefault(string value, double fallback)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return SanitizePositive(parsed, fallback);
        }

        return fallback;
    }

    private void LoadProcessingSettings()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, ProcessingSettingsFileName),
                Path.Combine(FindRepoRootContainingSrc(AppContext.BaseDirectory), ProcessingSettingsFileName)
            };

            string? settingsPath = null;
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    settingsPath = candidate;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                _analysisIntervalMs = DefaultAnalysisIntervalMs;
                _enableProcessingDiagnostics = false;
                _logger.LogInformation("未找到 OptiTrack 图像处理设置文件，使用默认值：诊断={Diagnostics}，分析间隔={Interval} 毫秒", 0, _analysisIntervalMs);
                return;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<ProcessingSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ProcessingSettings();

            var interval = settings.AnalysisIntervalMs <= 0 ? DefaultAnalysisIntervalMs : settings.AnalysisIntervalMs;
            _analysisIntervalMs = Math.Clamp(interval, MinAnalysisIntervalMs, MaxAnalysisIntervalMs);
            _enableProcessingDiagnostics = settings.EnableProcessingDiagnostics != 0;

            _logger.LogInformation(
                "已从 {Path} 加载 OptiTrack 图像处理设置：诊断={Diagnostics}，分析间隔={Interval} 毫秒",
                settingsPath,
                _enableProcessingDiagnostics ? 1 : 0,
                _analysisIntervalMs);
        }
        catch (Exception ex)
        {
            _analysisIntervalMs = DefaultAnalysisIntervalMs;
            _enableProcessingDiagnostics = false;
            _logger.LogWarning(ex, "加载 OptiTrack 图像处理设置失败，回退到默认值：诊断={Diagnostics}，分析间隔={Interval} 毫秒", 0, _analysisIntervalMs);
        }
    }

    private void LogProcessingDiagnostics(string messageTemplate, params object[] args)
    {
        if (!_enableProcessingDiagnostics)
        {
            return;
        }

        _logger.LogInformation(messageTemplate, args);
    }

    private static double ToMm(double microns) => microns / 1000.0;

    // 每 100 ms 轮询一次已连接位移设备；单轴轮询不重叠。
    private Task PollStagePositionsAsync()
    {
        return Task.WhenAll(
            PollStagePositionAsync(_actuator1, _actuator1PollGate, valueMm => Actuator1Position = valueMm),
            PollStagePositionAsync(_actuator2, _actuator2PollGate, valueMm => Actuator2Position = valueMm),
            PollStagePositionAsync(_verticalStage, _verticalPollGate, valueMm => VerticalStagePosition = valueMm),
            PollStagePositionAsync(_horizontalStage, _horizontalPollGate, valueMm => HorizontalStagePosition = valueMm));
    }

    private async Task PollStagePositionAsync(IStage? stage, SemaphoreSlim pollGate, Action<double> applyPositionMm)
    {
        if (stage == null || (stage.State != DeviceState.Connected && stage.State != DeviceState.Busy)) return;
        if (!await pollGate.WaitAsync(0)) return;

        try
        {
            var positionMm = await stage.GetPositionAsync();
            applyPositionMm(positionMm);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "轮询位移设备 {DeviceId} 的位置失败", stage.DeviceId);
        }
        finally
        {
            pollGate.Release();
        }
    }

    [RelayCommand]
    // 鍒囨崲褰撳墠鏄剧ず/鎺у埗鐩告満锛氬厛瀹夊叏鍋滄鏃х浉鏈洪噰闆嗭紝鍐嶈闃呮柊鐩告満甯у洖璋冨苟鑷姩寮€濮嬮噰闆嗐€?
    private async Task SelectCameraAsync(string cameraType)
    {
        if (IsCameraSwitchCoolingDown)
        {
            _logger.LogInformation("相机切换处于冷却时间，本次请求已忽略");
            return;
        }

        if (!await _cameraSwitchGate.WaitAsync(0))
        {
            _logger.LogInformation("已有相机切换正在进行，本次请求已忽略");
            return;
        }

        var switched = false;
        try
        {
        _logger.LogInformation("收到相机选择请求：{Type}", cameraType);
            CaptureCurrentCameraExposureFromInput();

            ICamera? newCam = cameraType switch
            {
                "AngleCamera" => _angleCamera,
                "SpotCamera" => _spotCamera,
                _ => null
            };

            if (newCam == null)
            {
            _logger.LogWarning("相机 {Type} 实例为空，请先连接设备", cameraType);
                 return;
            }

            if (_currentCamera == newCam)
            {
                return;
            }

            if (_currentCamera != null)
            {
                try { await _currentCamera.StopCaptureAsync(); } catch { }
                _currentCamera.FrameReceived -= OnFrameReceived;
                _currentCamera.StateChanged -= OnCurrentCameraStateChanged;
                // Give camera SDK a brief settle window to avoid rapid stop/start thrash.
                await Task.Delay(120);
            }

            // Drop old frame resources aggressively before switching camera source.
            ActiveFrame = null;
            _writeableBitmap = null;
            _backBuffer = null;
            Interlocked.Exchange(ref _lastUiFrameTickMs, 0);

            _currentCamera = newCam;
            _isFrameFrozen = false;
            _isSnapPending = false;
            _logger.LogInformation("已切换到相机：{Name}", _currentCamera.Name);

            // keep selection state in sync for the radio buttons
            if (cameraType == "AngleCamera")
            {
                IsAngleCameraSelected = true;
                IsSpotCameraSelected = false;
            }
            else if (cameraType == "SpotCamera")
            {
                IsSpotCameraSelected = true;
                IsAngleCameraSelected = false;
            }

            _currentCamera.FrameReceived += OnFrameReceived;
            _currentCamera.StateChanged += OnCurrentCameraStateChanged;

            // Ensure capture starts immediately after switching so frames arrive
            try { await _currentCamera.StartCaptureAsync(); } catch { }

            // UI 渚ц緭鍏?鏄剧ず鍗曚綅鏄?ms锛岃澶囧弬鏁板崟浣嶆槸 us锛岃繖閲屽仛涓€娆℃崲绠楀苟鎭㈠璇ョ浉鏈轰笂娆¤缃€笺€?
            ApplySelectedCameraExposure(cameraType);
            UpdateActiveCameraState();
            switched = true;
        }
        finally
        {
            _cameraSwitchGate.Release();
        }

        if (switched)
        {
            _ = StartCameraSwitchCooldownAsync();
        }
    }

    private async Task StartCameraSwitchCooldownAsync()
    {
        IsCameraSwitchCoolingDown = true;
        try
        {
            await Task.Delay(CameraSwitchCooldownMs);
        }
        finally
        {
            IsCameraSwitchCoolingDown = false;
        }
    }

    private void OnCurrentCameraStateChanged(object? sender, DeviceState state)
    {
        UpdateActiveCameraState();
    }

    [RelayCommand]
    // 鍗曟鎶撴媿娴佺▼锛氬喕缁撶敾闈?-> 鍋滄杩炵画閲囬泦 -> 瑙﹀彂 Snap锛岄殢鍚庡湪甯у洖璋冮噷寮傛钀界洏銆?
    private async Task SnapAsync()
    {
        if (_currentCamera == null) return;
        _logger.LogInformation("收到软触发抓拍请求");

        CaptureModeText = "软触发";

        _isSnapPending = true;
        _isFrameFrozen = true;

        try { await _currentCamera.StopCaptureAsync(); } catch (Exception ex) { _logger.LogDebug(ex, "抓拍前停止连续采集失败"); }
        await _currentCamera.SnapAsync();
    }

    [RelayCommand]
    // 杩涘叆瀹炴椂妯″紡锛屾竻闄ゆ姄鎷嶅喕缁撴爣璁般€?
    private async Task StartCaptureAsync()
    {
        if (_currentCamera == null) return;
        _logger.LogInformation("收到开始连续采集请求");
        CaptureModeText = "连续采集";
        _isFrameFrozen = false;
        _isSnapPending = false;
        await _currentCamera.StartCaptureAsync();
    }

    [RelayCommand]
    // 鎵嬪姩鍋滄褰撳墠鐩告満杩炵画閲囬泦銆?
    private async Task StopCaptureAsync()
    {
        if (_currentCamera == null) return;
        _logger.LogInformation("收到停止连续采集请求");
        CaptureModeText = "已停止";
        await _currentCamera.StopCaptureAsync();
    }
    
    [RelayCommand]
    private async Task ToggleConnectAllAsync()
    {
        if (Interlocked.Exchange(ref _connectToggleInFlight, 1) == 1)
        {
            return;
        }

        IsConnectBusy = true;
        try
        {
            if (HasAnyDeviceConnected)
            {
                await DisconnectAllAsync();
            }
            else
            {
                await ConnectAllAsync();
            }
        }
        finally
        {
            IsConnectBusy = false;
            Interlocked.Exchange(ref _connectToggleInFlight, 0);
            RefreshConnectAllState();
        }
    }

    // 杩炴帴骞剁粦瀹?OptiTrack 鎵€闇€鍏ㄩ儴瑙掕壊璁惧銆?
    // 鑻ユ湭閰嶇疆鏄犲皠锛屼細鍏堝皾璇曡嚜鍔ㄦ槧灏勶紝灏介噺璁╅娆″惎鍔ㄥ嵆鍙伐浣溿€?
    private async Task ConnectAllAsync()
    {
        _logger.LogInformation("正在连接 OptiTrack 所需的全部设备");
        var hasConnectedDevice = _deviceManager.GetDevices<IDevice>()
            .Any(static device => device.State is DeviceState.Connected or DeviceState.Busy);
        if (hasConnectedDevice)
        {
            _logger.LogInformation("已有设备保持连接，本次复用现有发现结果，避免重新枚举打断 USB 连接");
        }
        else
        {
            await _deviceManager.RefreshDevicesAsync();
        }

        // 姣忔杩炴帴鍓嶅厛娓呯┖鏃у紩鐢紝閬垮厤淇濈暀涓婃浼氳瘽閲岀殑澶辨晥璁惧瀹炰緥銆?
        _actuator1 = _actuator2 = _verticalStage = _horizontalStage = null;
        _mechanicalShutter = null;
        _angleCamera = _spotCamera = null;
        _currentCamera = null;
        ResetStageHeaders();
        ResetDeviceConnectionStatus();
        UpdateActiveCameraState();

        var mapping = _mappingStore.GetMappingForFeature(_requirements.FeatureId);
        
        // 鑷剤绛栫暐锛氭病鏈夋槧灏勬椂鑷姩鐢熸垚涓€浠介粯璁ゆ槧灏勶紝鍑忓皯鎵嬪伐閰嶇疆闂ㄦ銆?
        if (mapping == null || mapping.Count == 0)
        {
            _logger.LogWarning("未找到 OptiTrack 设备映射，正在尝试自动配置");
            await AutoConfigureDefaultMappingsAsync();
            
            // Reload mapping
            mapping = _mappingStore.GetMappingForFeature(_requirements.FeatureId);
            
            if (mapping == null || mapping.Count == 0)
            {
                 _logger.LogError("自动配置失败，请手动配置设备映射");
                 return;
            }
        }

        var discoveredDescriptors = _deviceManager.GetDiscoveredDescriptors().ToList();
        var descriptorsById = discoveredDescriptors.ToDictionary(d => d.DescriptorId, StringComparer.OrdinalIgnoreCase);
        var plans = new List<RoleConnectPlan>();
        var resolvedByDescriptor = new Dictionary<string, IDevice>(StringComparer.OrdinalIgnoreCase);
        var reservedStageDescriptorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool IsCompatibleStageDescriptor(string roleName, IDeviceDescriptor descriptor)
        {
            return roleName switch
            {
                "Actuator1" or "Actuator2" => IsXaStageDescriptor(descriptor),
                "VerticalStage" => IsKinesisVerticalDescriptor(descriptor),
                "HorizontalStage" => IsKinesisHorizontalDescriptor(descriptor),
                _ => false
            };
        }

        string ResolveStageDescriptorId(string roleName, string mappedDescriptorId)
        {
            var isStageRole = roleName is "Actuator1" or "Actuator2" or "VerticalStage" or "HorizontalStage";
            if (!isStageRole)
            {
                return mappedDescriptorId;
            }

            if (descriptorsById.TryGetValue(mappedDescriptorId, out var mappedDescriptor)
                && IsCompatibleStageDescriptor(roleName, mappedDescriptor)
                && reservedStageDescriptorIds.Add(mappedDescriptorId))
            {
                return mappedDescriptorId;
            }

            var selected = discoveredDescriptors.FirstOrDefault(d =>
                IsCompatibleStageDescriptor(roleName, d) && reservedStageDescriptorIds.Add(d.DescriptorId));
            if (selected == null)
            {
                _logger.LogError("角色 {Role} 没有可用且兼容的位移设备描述符", roleName);
                return mappedDescriptorId;
            }

            _logger.LogWarning(
                "角色 {Role} 映射到了不兼容或重复的描述符 {OldId}，改用 {Factory} 描述符 {NewId}",
                roleName,
                mappedDescriptorId,
                selected.FactoryType,
                selected.DescriptorId);
            return selected.DescriptorId;
        }

        var correctedMapping = new Dictionary<string, string>(mapping, StringComparer.OrdinalIgnoreCase);
        var mappingChanged = false;

        foreach (var role in _requirements.Roles)
        {
            if (mapping.TryGetValue(role.Name, out var descriptorId) && !string.IsNullOrEmpty(descriptorId))
            {
                try
                {
                    var shouldConnect = ShouldConnectRole(role.Name);
                    if (!shouldConnect)
                    {
                        UpdateRoleConnectionState(role.Name, null);
                        continue;
                    }

                    var originalDescriptorId = descriptorId;
                    descriptorId = ResolveStageDescriptorId(role.Name, descriptorId);
                    if (!string.Equals(originalDescriptorId, descriptorId, StringComparison.OrdinalIgnoreCase))
                    {
                        correctedMapping[role.Name] = descriptorId;
                        mappingChanged = true;
                    }

                    // 鏄犲皠鍊煎彲鑳芥槸鈥滃凡婵€娲昏澶嘔D鈥濇垨鈥滃緟鍒涘缓鎻忚堪ID鈥濓紝涓嬮潰閫昏緫浼氬吋瀹逛袱绉嶆儏鍐点€?
                    // Since DeviceManager doesn't expose "FindActiveByDescriptor", and our Descriptor for simulator is ambiguous (Factory Type), 
                    // we always create new for Cloneables.
                    
                    // Retrieve Descriptor to check metadata
                    if (!descriptorsById.TryGetValue(descriptorId, out var descriptor))
                    {
                        descriptor = discoveredDescriptors.FirstOrDefault(d => d.DescriptorId == descriptorId);
                    }

                    if (descriptor == null)
                    {
                        if (role.Name is "Actuator1" or "Actuator2"
                            && !string.IsNullOrWhiteSpace(descriptorId)
                            && _deviceManager.GetAvailableFactories().Any(static factory =>
                                string.Equals(factory.DeviceType, "thorlabs-xa-stage", StringComparison.OrdinalIgnoreCase)))
                        {
                            descriptor = new RuntimeDeviceDescriptor(
                                descriptorId,
                                $"Thorlabs XA Stage ({descriptorId})",
                                "IStage",
                                "thorlabs-xa-stage",
                                false);
                            _logger.LogWarning(
                                "XA 发现列表未包含角色 {Role} 的已保存设备编号 {Id}，仍将按该编号调用官方 TryOpenDevice",
                                role.Name,
                                descriptorId);
                        }
                        else if (role.Name is "HorizontalStage" or "VerticalStage"
                            && !string.IsNullOrWhiteSpace(descriptorId)
                            && ((role.Name == "HorizontalStage"
                                    && descriptorId.StartsWith("kinesis-legacy:bdc:", StringComparison.OrdinalIgnoreCase))
                                || (role.Name == "VerticalStage"
                                    && descriptorId.StartsWith("kinesis-legacy:kvs:", StringComparison.OrdinalIgnoreCase)))
                            && _deviceManager.GetAvailableFactories().Any(static factory =>
                                string.Equals(factory.DeviceType, "thorlabs-kinesis-stage", StringComparison.OrdinalIgnoreCase)))
                        {
                            descriptor = new RuntimeDeviceDescriptor(
                                descriptorId,
                                $"Thorlabs Kinesis Stage ({descriptorId})",
                                "IStage",
                                "thorlabs-kinesis-stage",
                                false);
                            _logger.LogWarning(
                                "Kinesis 发现列表未包含角色 {Role} 的已保存设备 {Id}，仍将按该编号调用官方连接接口",
                                role.Name,
                                descriptorId);
                        }
                        else
                        {
                            _logger.LogWarning("未找到角色 {Role} 对应的描述符 {Id}", role.Name, descriptorId);
                            continue;
                        }
                    }

                    var isStageRole = role.Name is "Actuator1" or "Actuator2" or "HorizontalStage" or "VerticalStage";
                    if (isStageRole && !IsCompatibleStageDescriptor(role.Name, descriptor))
                    {
                        _logger.LogError("拒绝角色 {Role} 使用不兼容的描述符 {DescriptorId}（{FactoryType}）", role.Name, descriptorId, descriptor.FactoryType);
                        continue;
                    }
                    IDevice? device;
                    if (resolvedByDescriptor.TryGetValue(descriptorId, out var existingDevice))
                    {
                        device = existingDevice;
                    }
                    else if ((device = _deviceManager.GetDevices<IDevice>().FirstOrDefault(d =>
                                  string.Equals(d.DeviceId, descriptorId, StringComparison.OrdinalIgnoreCase))) != null)
                    {
                        resolvedByDescriptor[descriptorId] = device;
                        _logger.LogInformation("角色 {Role} 复用已断开的设备实例 {DescriptorId}", role.Name, descriptorId);
                    }
                    else if (descriptor.IsCloneable)
                    {
                        // Always create new for cloneable (e.g. Simulator)
                        device = _deviceManager.CreateDevice<IDevice>(descriptor.FactoryType);
                        _deviceManager.RegisterDevice(device);
                        resolvedByDescriptor[descriptorId] = device;
                    }
                    else
                    {
                        // Unique device. Check if active.
                        // Ideally checking by DeviceId if the device instance has same ID as descriptor?
                        // For physical, DeviceId usually equals Serial. 
                        // Let's assume device.DeviceId == descriptorId.
                        device = _deviceManager.GetDevices<IDevice>().FirstOrDefault(d => d.DeviceId == descriptorId);
                        if (device != null && device.State == DeviceState.Error)
                        {
                            _logger.LogWarning("描述符 {DescriptorId} 的活动设备处于错误状态，将重新创建设备实例", descriptorId);
                            try { await device.DisconnectAsync(); } catch { }
                            _deviceManager.UnregisterDevice(device);
                            device = null;
                        }

                        if (device == null)
                        {
                            device = _deviceManager.CreateDevice<IDevice>(descriptor.FactoryType);
                            // Theoretically CreateDevice should initialize with the ID? 
                            // Factory.CreateDevice usually doesn't take ID. It discovers.
                            // This part is tricky for real hardware without Open(id).
                            // Assuming for now CreateDevice returns a device, and if it's the right one...
                            // For this prototype, we focus on Simulated.
                            _deviceManager.RegisterDevice(device);
                        }

                        resolvedByDescriptor[descriptorId] = device;
                    }

                    if (device == null)
                    {
                        _logger.LogWarning("无法为角色 {Role} 解析描述符 {DescriptorId} 对应的设备", role.Name, descriptorId);
                        continue;
                    }

                    // Push descriptor/open-id into devices that support explicit target selection.
                    if (device.State == DeviceState.Disconnected)
                    {
                        TrySetOpenDeviceId(device, descriptorId, role.Name);
                    }

                    plans.Add(new RoleConnectPlan(role, descriptorId, descriptor.FactoryType, device, shouldConnect));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "为角色 {Role} 准备连接时失败", role.Name);
                }
            }
        }

        if (mappingChanged)
        {
            await _mappingStore.SetMappingForFeatureAsync(_requirements.FeatureId, correctedMapping);
            _logger.LogInformation("描述符兼容性校验完成，已保存修正后的角色映射");
        }

        var connectErrors = new ConcurrentBag<(string RoleName, string DescriptorId, Exception Error)>();

        async Task ConnectPlanAsync(RoleConnectPlan plan)
        {
            if (!plan.ShouldConnect)
            {
                return;
            }

            if (plan.Device.State == DeviceState.Connected)
            {
                return;
            }

            if (plan.Device.State == DeviceState.Error)
            {
                try { await Task.Run(async () => await plan.Device.DisconnectAsync()); } catch { }
            }

            try
            {
                var startedAt = Stopwatch.GetTimestamp();
                _logger.LogInformation(
                    "开始连接角色 {Role}：描述符={DescriptorId}，驱动={FactoryType}",
                    plan.Role.Name,
                    plan.DescriptorId,
                    plan.FactoryType);

                await plan.Device.ConnectAsync();
                UpdateRoleConnectionStateSafe(plan.Role.Name, plan.Device);
                _logger.LogInformation(
                    "角色 {Role} 连接成功：描述符={DescriptorId}，耗时={ElapsedMs} 毫秒",
                    plan.Role.Name,
                    plan.DescriptorId,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                connectErrors.Add((plan.Role.Name, plan.DescriptorId, ex));
                _logger.LogError(ex, "角色 {Role} 连接描述符 {DescriptorId} 失败", plan.Role.Name, plan.DescriptorId);
                try { await plan.Device.DisconnectAsync(); } catch { }
                UpdateRoleConnectionStateSafe(plan.Role.Name, plan.Device);
            }
        }

        // XA 与 Kinesis 都会操作 Thorlabs 的进程级设备管理器和 USB 句柄。
        // 四个运动轴必须串行完成连接，避免两个 XA SystemManager.Open 或 Kinesis 连接互相抢占。
        var stagePlans = plans
            .Where(static p => p.Role.Name is "Actuator1" or "Actuator2" or "HorizontalStage" or "VerticalStage")
            .ToList();

        async Task ConnectStagesAsync()
        {
            foreach (var plan in stagePlans)
            {
                await ConnectPlanAsync(plan);
            }
        }

        var otherPlans = plans.Where(p => !stagePlans.Contains(p)).ToList();
        var stagesTask = ConnectStagesAsync();

        using (var connectLimiter = new SemaphoreSlim(3, 3))
        {
            var connectTasks = otherPlans.Select(async plan =>
            {
                await connectLimiter.WaitAsync();
                try
                {
                    await ConnectPlanAsync(plan);
                }
                finally
                {
                    connectLimiter.Release();
                }
            }).ToList();

            await Task.WhenAll(stagesTask, Task.WhenAll(connectTasks));
        }

        if (!connectErrors.IsEmpty)
        {
            _logger.LogWarning("全部设备连接完成，其中 {Count} 个角色连接失败", connectErrors.Count);
        }

        foreach (var plan in plans)
        {
            if (plan.Device.State != DeviceState.Connected)
            {
                _logger.LogWarning("角色 {Role} 的设备状态为 {State}，跳过绑定", plan.Role.Name, plan.Device.State);
                UpdateRoleConnectionState(plan.Role.Name, plan.Device);
                continue;
            }

            await AssignDeviceToRoleAsync(plan.Role.Name, plan.Device);

            if (plan.Device is IStage stage)
            {
                try
                {
                    var posMm = await stage.GetPositionAsync();
                    ApplyStagePositionFromMm(plan.Role.Name, posMm);
                }
                catch (Exception ex)
                {
                _logger.LogWarning(ex, "读取角色 {Role} 的初始位置失败", plan.Role.Name);
                }
            }
        }

        // Auto-select display camera (prefer Angle if enabled, else Spot)
        if (_currentCamera == null)
        {
            if (ShowAngleCamera && _angleCamera != null)
            {
                IsAngleCameraSelected = true;
                IsSpotCameraSelected = false;
                await SelectCameraAsync("AngleCamera");
            }
            else if (ShowSpotCamera && _spotCamera != null)
            {
                IsSpotCameraSelected = true;
                IsAngleCameraSelected = false;
                await SelectCameraAsync("SpotCamera");
            }
        }

        // 璁惧缁戝畾瀹屾垚鍚庯紝鎶婅澶囧悕鍥炲啓鍒伴潰鏉挎爣棰橈紝鏂逛究鎿嶄綔鑰呮牳瀵瑰綋鍓嶆帶鍒跺璞°€?
        try { UpdateStageHeaders(); } catch { }

        // On slower target PCs this can cause long blocking operations at startup.
        // Enable only when explicitly requested via environment variable.
        if (_autoHomeAfterConnect)
        {
            await HomeAllStagesAfterConnectAsync();
        }
        else
        {
            _logger.LogInformation("连接后自动 Home 已禁用；如需启用，请设置 VISION_AUTO_HOME_ON_CONNECT=1");
        }

        if (!_positionPollTimer.IsEnabled)
            _positionPollTimer.Start();

        RefreshConnectAllState();
    }

    private async Task DisconnectAllAsync()
    {
        _logger.LogInformation("正在断开 OptiTrack 的全部设备");

        CancelTimedShutterOperation();

        if (_positionPollTimer.IsEnabled)
        {
            _positionPollTimer.Stop();
        }

        if (_currentCamera != null)
        {
            _currentCamera.FrameReceived -= OnFrameReceived;
            _currentCamera.StateChanged -= OnCurrentCameraStateChanged;
        }
        Interlocked.Exchange(ref _latestPendingFrame, null);

        // Manual disconnect keeps SDK/device instances alive for safe reconnect.
        // Full Dispose is reserved for the actual application shutdown path.
        await _deviceManager.DisconnectAllAsync();

        _actuator1 = _actuator2 = _verticalStage = _horizontalStage = null;
        _mechanicalShutter = null;
        _angleCamera = _spotCamera = null;
        _currentCamera = null;
        ActiveFrame = null;
        _writeableBitmap = null;
        _backBuffer = null;
        IsMasterSwitchOn = false;
        ResetStageHeaders();
        ResetDeviceConnectionStatus();
        UpdateActiveCameraState();
        _logger.LogInformation("OptiTrack 的全部设备已断开");
    }

    private void CancelTimedShutterOperation()
    {
        var cts = Interlocked.Exchange(ref _timedShutterCts, null);
        if (cts == null)
        {
            IsTimedShutterCountingDown = false;
            TimedShutterRemainingSecondsText = "-";
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignore
        }
        finally
        {
            cts.Dispose();
            IsTimedShutterCountingDown = false;
            TimedShutterRemainingSecondsText = "-";
        }
    }

    private sealed record RoleConnectPlan(FeatureRole Role, string DescriptorId, string FactoryType, IDevice Device, bool ShouldConnect);
    private sealed record RuntimeDeviceDescriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

    private bool ShouldConnectRole(string roleName)
    {
        return roleName switch
        {
            "Actuator1" => ShowActuator1,
            "Actuator2" => ShowActuator2,
            "VerticalStage" => ShowVerticalStage,
            "HorizontalStage" => ShowHorizontalStage,
            "MechanicalShutter" => ShowMechanicalShutter,
            "AngleCamera" => ShowAngleCamera,
            "SpotCamera" => ShowSpotCamera,
            _ => true
        };
    }

    // 鑷姩鍥為浂鐨勫け璐ヤ笉闃绘柇鏁翠綋杩炴帴娴佺▼锛氬崟杞村け璐ヤ細璁板綍鍛婅锛屼絾鍏朵綑杞寸户缁墽琛屻€?
    private async Task HomeAllStagesAfterConnectAsync()
    {
        try
        {
            var stages = new (string Name, IStage? Stage)[]
            {
                ("Actuator1", _actuator1),
                ("Actuator2", _actuator2),
                ("VerticalStage", _verticalStage),
                ("HorizontalStage", _horizontalStage)
            };

            foreach (var (name, stage) in stages)
            {
                if (stage == null) continue;
                try
                {
            _logger.LogInformation("连接后正在自动 Home：{Stage}", name);
                    await stage.HomeAsync();
                }
                catch (Exception ex)
                {
            _logger.LogWarning(ex, "设备 {Stage} 自动 Home 失败", name);
                }
            }

            // Refresh positions after homing
            try { await PollStagePositionsAsync(); } catch { }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "连接后的自动 Home 流程失败");
        }
    }

    // 鑷姩鏄犲皠绛栫暐锛?
    // 1) 鍏堝皾璇曟妸瑙掕壊鍚嶅尮閰嶅埌宸叉縺娲昏澶囷紱
    // 2) 鎵句笉鍒板啀鍥為€€鍒板悓绫诲瀷宸ュ巶鎻忚堪锛堜紭鍏?simulated锛夛紝骞跺啓鍥炴槧灏勫瓨鍌ㄣ€?
    private async Task AutoConfigureDefaultMappingsAsync()
    {
        var newMapping = new Dictionary<string, string>();
        var descriptors = _deviceManager.GetDiscoveredDescriptors().ToList();
        var activeDevices = _deviceManager.GetDevices<IDevice>().ToList();
        var usedStageDescriptorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedCameraDescriptorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in _requirements.Roles)
        {
            // Strategy 1: Look for an Active Device with Name similar to Role Name (Case Insensitive)
            // e.g. Role "AngleCamera" matches Device "Angle Camera" or ID "AngleCamera"
            var matchingDevice = activeDevices.FirstOrDefault(d => 
                d.Name.Replace(" ", "").Equals(role.Name, StringComparison.OrdinalIgnoreCase) || 
                d.DeviceId.Equals(role.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingDevice != null)
            {
                newMapping[role.Name] = matchingDevice.DeviceId;
                    _logger.LogInformation("已将角色 {Role} 自动映射到活动设备 {Id}", role.Name, matchingDevice.DeviceId);
                continue;
            }

            // Strategy 2: role-specific descriptor preference.
            IDeviceDescriptor? selectedDescriptor = null;

            if (role.DeviceType == "IStage")
            {
                var stageCandidates = descriptors
                    .Where(d => d.DeviceTypeIdentifier == "IStage")
                    .Where(d => !usedStageDescriptorIds.Contains(d.DescriptorId))
                    .ToList();

                selectedDescriptor = role.Name switch
                {
                    "Actuator1" or "Actuator2" => stageCandidates.FirstOrDefault(IsXaStageDescriptor),
                    "VerticalStage" => stageCandidates.FirstOrDefault(IsKinesisVerticalDescriptor),
                    "HorizontalStage" => stageCandidates.FirstOrDefault(IsKinesisHorizontalDescriptor),
                    _ => stageCandidates.FirstOrDefault(IsXaStageDescriptor)
                };
            }
            else if (role.DeviceType == "ICamera")
            {
                selectedDescriptor = descriptors.FirstOrDefault(d =>
                        d.DeviceTypeIdentifier == "ICamera"
                        && !usedCameraDescriptorIds.Contains(d.DescriptorId)
                        && string.Equals(d.FactoryType, "mvsdk-camera", StringComparison.OrdinalIgnoreCase)
                        && !d.DescriptorId.Contains("simulated", StringComparison.OrdinalIgnoreCase))
                    ?? descriptors.FirstOrDefault(d =>
                        d.DeviceTypeIdentifier == "ICamera"
                        && !usedCameraDescriptorIds.Contains(d.DescriptorId)
                        && !d.FactoryType.Contains("simul", StringComparison.OrdinalIgnoreCase)
                        && !d.DescriptorId.Contains("simulated", StringComparison.OrdinalIgnoreCase))
                    ?? descriptors.FirstOrDefault(d =>
                        d.DeviceTypeIdentifier == "ICamera"
                        && !usedCameraDescriptorIds.Contains(d.DescriptorId));
            }

            if (selectedDescriptor != null)
            {
                newMapping[role.Name] = selectedDescriptor.DescriptorId;
                _logger.LogInformation("已将角色 {Role} 自动映射到描述符 {DescriptorId}（{Factory}）", role.Name, selectedDescriptor.DescriptorId, selectedDescriptor.FactoryType);

                if (selectedDescriptor.DeviceTypeIdentifier == "IStage")
                {
                    usedStageDescriptorIds.Add(selectedDescriptor.DescriptorId);
                }
                else if (selectedDescriptor.DeviceTypeIdentifier == "ICamera" && !selectedDescriptor.IsCloneable)
                {
                    usedCameraDescriptorIds.Add(selectedDescriptor.DescriptorId);
                }
            }
        }

        if (newMapping.Count > 0)
        {
            await _mappingStore.SetMappingForFeatureAsync(_requirements.FeatureId, newMapping);
            _logger.LogInformation("已保存自动配置，共 {Count} 条映射", newMapping.Count);
        }
    }

    private static bool IsXaStageDescriptor(IDeviceDescriptor d)
    {
        return d.DeviceTypeIdentifier == "IStage"
            && string.Equals(d.FactoryType, "thorlabs-xa-stage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKinesisStageDescriptor(IDeviceDescriptor d)
    {
        return d.DeviceTypeIdentifier == "IStage"
            && string.Equals(d.FactoryType, "thorlabs-kinesis-stage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKinesisVerticalDescriptor(IDeviceDescriptor d)
    {
        return IsKinesisStageDescriptor(d)
            && d.DescriptorId.StartsWith("kinesis-legacy:kvs:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKinesisHorizontalDescriptor(IDeviceDescriptor d)
    {
        return IsKinesisStageDescriptor(d)
            && d.DescriptorId.StartsWith("kinesis-legacy:bdc:", StringComparison.OrdinalIgnoreCase);
    }

    // 鎶娾€滆鑹插悕鈥濆啓鍏ュ搴旇澶囧瓧娈碉紝鍚庣画鎵€鏈夊懡浠ら兘閫氳繃杩欎簺瀛楁鎵ц銆?
    private async Task AssignDeviceToRoleAsync(string roleName, IDevice device)
    {
        switch (roleName)
        {
            case "Actuator1": _actuator1 = device as IStage; break;
            case "Actuator2": _actuator2 = device as IStage; break;
            case "VerticalStage": _verticalStage = device as IStage; break;
            case "HorizontalStage": _horizontalStage = device as IStage; break;
            case "MechanicalShutter":
                _mechanicalShutter = device;
                if (device is IShutter shutter)
                {
                    IsMasterSwitchOn = shutter.IsOpen;
                }
                break;
            case "AngleCamera": 
                _angleCamera = device as ICamera; 
                // Auto Select if needed?
                break;
            case "SpotCamera": 
                _spotCamera = device as ICamera; 
                break;
        }

        UpdateRoleConnectionState(roleName, device);
        await Task.CompletedTask;
    }

    private void ResetDeviceConnectionStatus()
    {
        IsActuator1Connected = false;
        IsActuator2Connected = false;
        IsVerticalStageConnected = false;
        IsHorizontalStageConnected = false;
        IsMechanicalShutterConnected = false;
        IsAngleCameraConnected = false;
        IsSpotCameraConnected = false;
        RefreshConnectAllState();
    }

    private void UpdateRoleConnectionState(string roleName, IDevice? device)
    {
        var connected = device?.State == DeviceState.Connected;
        switch (roleName)
        {
            case "Actuator1": IsActuator1Connected = connected; break;
            case "Actuator2": IsActuator2Connected = connected; break;
            case "VerticalStage": IsVerticalStageConnected = connected; break;
            case "HorizontalStage": IsHorizontalStageConnected = connected; break;
            case "MechanicalShutter": IsMechanicalShutterConnected = connected; break;
            case "AngleCamera": IsAngleCameraConnected = connected; break;
            case "SpotCamera": IsSpotCameraConnected = connected; break;
        }

        RefreshConnectAllState();
    }

    private void UpdateRoleConnectionStateSafe(string roleName, IDevice? device)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            UpdateRoleConnectionState(roleName, device);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => UpdateRoleConnectionState(roleName, device)), DispatcherPriority.Background);
    }

    private void RefreshConnectAllState()
    {
        HasAnyDeviceConnected = IsActuator1Connected
            || IsActuator2Connected
            || IsVerticalStageConnected
            || IsHorizontalStageConnected
            || IsMechanicalShutterConnected
            || IsAngleCameraConnected
            || IsSpotCameraConnected;
    }

    private void TrySetOpenDeviceId(IDevice device, string descriptorId, string roleName)
    {
        if (string.IsNullOrWhiteSpace(descriptorId))
        {
            return;
        }

        try
        {
            var method = device.GetType().GetMethod("SetOpenDeviceId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (method == null)
            {
                return;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            {
                return;
            }

            method.Invoke(device, new object[] { descriptorId });
            _logger.LogDebug("已将映射描述符 {DescriptorId} 应用于角色 {Role}", descriptorId, roleName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "向角色 {Role} 应用映射描述符失败", roleName);
        }
    }


    [RelayCommand]
    private async Task AutoStartAngleAdjust()
    {
        _logger.LogInformation("收到周期调整启动请求");

        if (!double.TryParse(TargetPeriodInput, NumberStyles.Float, CultureInfo.CurrentCulture, out var targetPeriodUm)
            && !double.TryParse(TargetPeriodInput, NumberStyles.Float, CultureInfo.InvariantCulture, out targetPeriodUm))
        {
            _logger.LogWarning("目标周期输入无效：{Input}", TargetPeriodInput);
            MessageBox.Show("请输入有效的目标周期。", "周期调整", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!double.IsFinite(targetPeriodUm) || targetPeriodUm < 3 || targetPeriodUm > 500)
        {
            MessageBox.Show("目标周期必须在 3.0 到 500.0 μm 之间。", "周期调整", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureAngleAdjustDevicesReady("周期调整"))
        {
            return;
        }

        var root = FindRepoRootContainingSrc(AppContext.BaseDirectory);
        var filePath = Path.Combine(root, "angle-adjust-params.json");
        var store = await LoadParamsStoreAsync(filePath);
        if (store.Entries.Count == 0)
        {
            _logger.LogWarning("未找到可用于周期调整的已保存参数");
            MessageBox.Show("没有可用的周期校准参数。", "周期调整", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AngleAdjustParamsEntry? selected = null;

        // 浼樺厛绮剧‘鍖归厤锛岄伩鍏嶆彃鍊艰宸€?
        var exact = store.Entries.FirstOrDefault(e => Math.Abs(e.StripePeriodUm - targetPeriodUm) < 0.05);
        if (exact != null)
        {
            selected = exact;
            _logger.LogInformation("找到周期 {Period:F1} 微米的精确匹配，正在应用已保存位置", targetPeriodUm);
        }
        else
        {
            var lower = store.Entries.Where(e => e.StripePeriodUm < targetPeriodUm).OrderByDescending(e => e.StripePeriodUm).FirstOrDefault();
            var upper = store.Entries.Where(e => e.StripePeriodUm > targetPeriodUm).OrderBy(e => e.StripePeriodUm).FirstOrDefault();

            if (lower == null && upper == null)
            {
                _logger.LogWarning("没有可用于插值的校准条目");
                return;
            }

            if (lower == null || upper == null)
            {
                selected = lower ?? upper;
                _logger.LogInformation("只找到一个相邻校准点，使用最近周期 {Period:F1} 微米", selected!.StripePeriodUm);
            }
            else
            {
                var lowerPeriod = lower.StripePeriodUm;
                var upperPeriod = upper.StripePeriodUm;
                var span = upperPeriod - lowerPeriod;
                if (Math.Abs(span) < 1e-9)
                {
                    selected = lower;
                    _logger.LogInformation("相邻校准点周期相同，使用周期 {Period:F1} 微米", selected.StripePeriodUm);
                }
                else
                {
                    var ratio = (targetPeriodUm - lower.StripePeriodUm) / span;
                    selected = new AngleAdjustParamsEntry
                    {
                        StripePeriodUm = targetPeriodUm,
                        StagePositionsMm = new StagePositionsMm
                        {
                            Actuator1 = Lerp(lower.StagePositionsMm.Actuator1, upper.StagePositionsMm.Actuator1, ratio),
                            Actuator2 = Lerp(lower.StagePositionsMm.Actuator2, upper.StagePositionsMm.Actuator2, ratio),
                            Vertical = Lerp(lower.StagePositionsMm.Vertical, upper.StagePositionsMm.Vertical, ratio),
                            Horizontal = Lerp(lower.StagePositionsMm.Horizontal, upper.StagePositionsMm.Horizontal, ratio)
                        }
                    };
                    _logger.LogInformation("已在 {Low:F1} 与 {High:F1} 微米之间插值得到目标周期 {Period:F1} 微米的位置", lower.StripePeriodUm, upper.StripePeriodUm, targetPeriodUm);
                }
            }
        }

        if (selected == null)
        {
            _logger.LogWarning("无法解析目标周期 {Period:F1} 微米对应的参数", targetPeriodUm);
            return;
        }

        if (!selected.StagePositionsMm.AreFinite())
        {
            MessageBox.Show("校准文件包含无效的轴位置，已阻止启动。", "周期调整", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            await ApplyStageTargetsAsync(selected.StagePositionsMm);
            MessageBox.Show($"已应用 {targetPeriodUm:F1} μm 的周期参数。", "周期调整", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用周期调整位置失败");
            await StopAllStagesBestEffortAsync();
            MessageBox.Show($"周期调整执行失败，已向所有轴发送停止命令：{ex.Message}", "周期调整", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool EnsureAngleAdjustDevicesReady(string operationTitle)
    {
        var stages = new[] { _actuator1, _actuator2, _verticalStage, _horizontalStage };
        if (stages.Any(stage => stage == null || stage.State != DeviceState.Connected))
        {
            MessageBox.Show("两个促动器和两个位移台必须全部连接且处于空闲状态。", operationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return EnsureActuatorHomedForMotion("Actuator1")
            && EnsureActuatorHomedForMotion("Actuator2");
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static bool IsActuatorRole(string deviceName)
    {
        return string.Equals(deviceName, "Actuator1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(deviceName, "Actuator2", StringComparison.OrdinalIgnoreCase);
    }

    private bool EnsureActuatorHomedForMotion(string deviceName)
    {
        if (!IsActuatorRole(deviceName)) return true;

        IStage? stage = deviceName switch
        {
            "Actuator1" => _actuator1,
            "Actuator2" => _actuator2,
            _ => null
        };

        try
        {
            if (stage is IHomingStatus { IsHomed: true }) return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取设备 {Device} 的 XA 官方 Homed 状态失败", deviceName);
            MessageBox.Show(
                $"无法读取 {deviceName} 的 XA 官方 Homed 状态，已阻止运动。请检查设备连接。",
                "Home 状态不可用", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        MessageBox.Show(
            $"{deviceName} 控制器报告尚未 Home。断电重启后请先执行 Home；仅重连软件不会清除该状态。",
            "需要 Home", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool ValidateActuatorAbsoluteRange(string deviceName, double targetMm)
    {
        if (!IsActuatorRole(deviceName)) return true;
        if (targetMm < ActuatorAbsoluteMinMm || targetMm > ActuatorAbsoluteMaxMm)
        {
            _logger.LogWarning("拒绝促动器绝对移动：设备={Device}，目标={TargetMm:F5} mm，允许范围=[{MinMm:F5},{MaxMm:F5}] mm", deviceName, targetMm, ActuatorAbsoluteMinMm, ActuatorAbsoluteMaxMm);
            MessageBox.Show($"{deviceName} 的绝对位置必须在 {ActuatorAbsoluteMinMm:F5} 到 {ActuatorAbsoluteMaxMm:F5} mm 之间。", "位置超出范围", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private bool ValidateActuatorRelativeReach(string deviceName, double targetMm)
    {
        if (!IsActuatorRole(deviceName)) return true;
        if (targetMm < ActuatorRelativeMinReachMm || targetMm > ActuatorAbsoluteMaxMm)
        {
            _logger.LogWarning("拒绝促动器相对移动：设备={Device}，移动后目标={TargetMm:F5} mm，允许范围=[{MinMm:F5},{MaxMm:F5}] mm", deviceName, targetMm, ActuatorRelativeMinReachMm, ActuatorAbsoluteMaxMm);
            MessageBox.Show($"{deviceName} 本次移动后的目标位置必须在 {ActuatorRelativeMinReachMm:F5} 到 {ActuatorAbsoluteMaxMm:F5} mm 之间。", "位置超出范围", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private static double StageUiValueToMm(string deviceName, double value) => value;
    private static double StageMmToUiValue(string deviceName, double valueMm) => valueMm;

    private void ApplyStagePositionFromMm(string deviceName, double positionMm)
    {
        var uiValue = StageMmToUiValue(deviceName, positionMm);
        switch (deviceName)
        {
            case "Actuator1": Actuator1Position = uiValue; break;
            case "Actuator2": Actuator2Position = uiValue; break;
            case "Vertical":
            case "VerticalStage": VerticalStagePosition = uiValue; break;
            case "Horizontal":
            case "HorizontalStage": HorizontalStagePosition = uiValue; break;
        }
    }

    private async Task ApplyStageTargetsAsync(StagePositionsMm positions)
    {
        await MoveAngleAdjustAxisAsync("Actuator1", _actuator1!, positions.Actuator1);
        await MoveAngleAdjustAxisAsync("Actuator2", _actuator2!, positions.Actuator2);
        await MoveAngleAdjustAxisAsync("Vertical", _verticalStage!, positions.Vertical);
        await MoveAngleAdjustAxisAsync("Horizontal", _horizontalStage!, positions.Horizontal);
    }

    private async Task MoveAngleAdjustAxisAsync(string deviceName, IStage stage, double targetMm)
    {
        if (!double.IsFinite(targetMm))
        {
            throw new InvalidDataException($"{deviceName} target is not finite.");
        }

        if (IsActuatorRole(deviceName) && !EnsureActuatorHomedForMotion(deviceName))
        {
            throw new InvalidOperationException($"{deviceName} is not homed.");
        }

        try
        {
            await stage.MoveAsync(targetMm, DefaultMoveMode.Absolute);
        }
        catch (Exception ex) when (targetMm < 0 && !IsActuatorRole(deviceName))
        {
            var currentMm = await stage.GetPositionAsync();
            _logger.LogWarning(ex, "设备 {Device} 的负向绝对移动失败，改用相对位移 {DeltaMm:F6} mm", deviceName, targetMm - currentMm);
            await stage.MoveAsync(targetMm - currentMm, DefaultMoveMode.Relative);
        }

        ApplyStagePositionFromMm(deviceName, await stage.GetPositionAsync());
    }

    private async Task StopAllStagesBestEffortAsync()
    {
        var stages = new[] { _actuator1, _actuator2, _verticalStage, _horizontalStage }
            .Where(stage => stage != null)
            .Cast<IStage>();

        await Task.WhenAll(stages.Select(async stage =>
        {
            try { await stage.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "周期调整出错后停止位移设备 {DeviceId} 失败", stage.DeviceId); }
        }));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task MoveStageAsync(string commandId)
    {
        var parts = commandId.Split('_');
        if (parts.Length != 2) return;
        var deviceName = parts[0];
        var direction = parts[1] switch { "Left" or "Down" => -1.0, "Right" or "Up" => 1.0, _ => 0.0 };
        IStage? targetStage = deviceName switch { "Actuator1" => _actuator1, "Actuator2" => _actuator2, "Vertical" => _verticalStage, "Horizontal" => _horizontalStage, _ => null };
        var stepUi = deviceName switch { "Actuator1" => Actuator1StepSize, "Actuator2" => Actuator2StepSize, "Vertical" => VerticalStageStepSize, "Horizontal" => HorizontalStageStepSize, _ => 0.0 };
        if (targetStage == null || direction == 0 || !double.IsFinite(stepUi) || Math.Abs(stepUi) <= 0) return;
        if (!EnsureActuatorHomedForMotion(deviceName)) return;
        try
        {
            var currentUi = StageMmToUiValue(deviceName, await targetStage.GetPositionAsync());
            var targetUi = currentUi + Math.Abs(stepUi) * direction;
            if (!ValidateActuatorRelativeReach(deviceName, targetUi)) return;
            await MoveStageAbsoluteStrictAsync(deviceName, targetUi);
        }
        catch (Exception ex) { _logger.LogError(ex, "移动设备 {Device} 时发生异常", deviceName); }
    }

    private async Task MoveStageAbsoluteStrictAsync(string deviceName, double targetUi)
    {
        IStage? targetStage = deviceName switch { "Actuator1" => _actuator1, "Actuator2" => _actuator2, "Vertical" => _verticalStage, "Horizontal" => _horizontalStage, _ => null };
        if (targetStage == null || !EnsureActuatorHomedForMotion(deviceName) || !ValidateActuatorAbsoluteRange(deviceName, targetUi)) return;
        await targetStage.MoveAsync(StageUiValueToMm(deviceName, targetUi), DefaultMoveMode.Absolute);
        ApplyStagePositionFromMm(deviceName, await targetStage.GetPositionAsync());
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task MoveStageAbsoluteAsync(string deviceName)
    {
        var targetUi = deviceName switch { "Actuator1" => Actuator1TargetMm, "Actuator2" => Actuator2TargetMm, "Vertical" => VerticalTargetMm, "Horizontal" => HorizontalTargetMm, _ => 0.0 };
        await MoveStageAbsoluteCoreAsync(deviceName, targetUi);
    }

    private Task MoveStageAbsoluteCoreAsync(string deviceName, double targetUi)
    {
        if (!ValidateActuatorAbsoluteRange(deviceName, targetUi)) return Task.CompletedTask;
        return MoveStageAbsoluteDeviceMmAsync(deviceName, StageUiValueToMm(deviceName, targetUi));
    }

    private async Task MoveStageAbsoluteDeviceMmAsync(string deviceName, double targetMm)
    {
        IStage? targetStage = deviceName switch { "Actuator1" => _actuator1, "Actuator2" => _actuator2, "Vertical" => _verticalStage, "Horizontal" => _horizontalStage, _ => null };
        if (targetStage == null || !double.IsFinite(targetMm) || !EnsureActuatorHomedForMotion(deviceName)) return;

        try { await targetStage.MoveAsync(targetMm, DefaultMoveMode.Absolute); }
        catch (Exception ex) when (targetMm < 0 && !IsActuatorRole(deviceName))
        {
            var currentMm = await targetStage.GetPositionAsync();
            _logger.LogWarning(ex, "设备 {Device} 的负向绝对移动失败，改用相对位移 {DeltaMm:F6} mm", deviceName, targetMm - currentMm);
            await targetStage.MoveAsync(targetMm - currentMm, DefaultMoveMode.Relative);
        }
        try { ApplyStagePositionFromMm(deviceName, await targetStage.GetPositionAsync()); }
        catch (Exception ex) { _logger.LogWarning(ex, "设备 {Device} 移动完成，但位置读回失败", deviceName); }
    }

    [RelayCommand]
    // 瀵规寚瀹氳酱鍙戦€佹€ュ仠銆?
    private async Task StopStageAsync(string deviceId)
    {
        IStage? targetStage = null;
        switch (deviceId)
        {
            case "Actuator1": targetStage = _actuator1; break;
            case "Actuator2": targetStage = _actuator2; break;
            case "Vertical": targetStage = _verticalStage; break;
            case "Horizontal": targetStage = _horizontalStage; break;
        }

        if (targetStage != null)
        {
            try
            {
                await targetStage.StopAsync();
            _logger.LogInformation("已停止位移设备 {Device}", deviceId);
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "停止位移设备 {Device} 时发生异常", deviceId);
            }
        }
    }

    [RelayCommand]
    // 瀵规寚瀹氳酱鎵ц鍥為浂锛屽苟鎶婂洖闆跺悗浣嶇疆鍚屾鍥?UI銆?
    private void HomeStage(string deviceId)
    {
        if (!_activeHomeOperations.TryAdd(deviceId, 0))
        {
            _logger.LogInformation("位移设备 {Device} 已在执行 Home，忽略重复请求", deviceId);
            return;
        }

        _ = RunHomeStageAsync(deviceId);
    }

    private async Task RunHomeStageAsync(string deviceId)
    {
        IStage? targetStage = null;
        switch (deviceId)
        {
            case "Actuator1": targetStage = _actuator1; break;
            case "Actuator2": targetStage = _actuator2; break;
            case "Vertical": targetStage = _verticalStage; break;
            case "Horizontal": targetStage = _horizontalStage; break;
        }

        try
        {
            if (targetStage != null)
            {
                try
                {
                    _logger.LogInformation("位移设备 {Device} 正在执行 Home", deviceId);
                    await targetStage.HomeAsync();
                    _logger.LogInformation("位移设备 {Device} 已完成 Home", deviceId);

                    var posMm = await targetStage.GetPositionAsync();
                    ApplyStagePositionFromMm(deviceId, posMm);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "位移设备 {Device} 执行 Home 时发生异常", deviceId);
                }
            }
        }
        finally
        {
            _activeHomeOperations.TryRemove(deviceId, out _);
        }
    }

    [RelayCommand]
    private Task ZeroStageAsync(string deviceId)
    {
        // Home establishes the reference; zero is an ordinary absolute move to coordinate 0.
        return MoveStageAbsoluteStrictAsync(deviceId, 0.0);
    }

    [RelayCommand]
    // 涓诲紑鍏宠仈鍔ㄦ満姊板揩闂細娌℃湁椹卞姩鏃舵嫆缁濆垏鎹紝閬垮厤 UI 鍋囩姸鎬佽瀵笺€?
    private async Task ToggleMasterSwitch()
    {
        CancelTimedShutterOperation();

        var targetState = !IsMasterSwitchOn;

        if (_mechanicalShutter is IShutter shutter)
        {
            try
            {
                await shutter.SetOpenAsync(targetState);
                IsMasterSwitchOn = shutter.IsOpen;
            _logger.LogInformation("机械快门已切换为{State}", IsMasterSwitchOn ? "打开" : "关闭");
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "切换机械快门失败");
            }

            return;
        }

            _logger.LogWarning("机械快门尚未连接，已忽略切换请求");
    }

    [RelayCommand]
    private async Task TimedOpenShutterAsync()
    {
        if (_mechanicalShutter is not IShutter shutter)
        {
            _logger.LogWarning("机械快门尚未连接，已忽略定时开启请求");
            return;
        }

        if (!double.TryParse(TimedShutterOpenSecondsInput, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds)
            && !double.TryParse(TimedShutterOpenSecondsInput, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            _logger.LogWarning("快门定时开启时长输入无效：{Input}", TimedShutterOpenSecondsInput);
            return;
        }

        if (seconds <= 0)
        {
            _logger.LogWarning("快门定时开启时长必须大于 0 秒，当前输入：{Seconds}", seconds);
            return;
        }

        var delay = TimeSpan.FromSeconds(seconds);

        CancelTimedShutterOperation();
        var cts = new CancellationTokenSource();
        _timedShutterCts = cts;
        var token = cts.Token;
        IsTimedShutterCountingDown = true;
        TimedShutterRemainingSecondsText = seconds.ToString("F1", CultureInfo.CurrentCulture);

        try
        {
            if (!shutter.IsOpen)
            {
                await shutter.SetOpenAsync(true);
            _logger.LogInformation("机械快门定时开启已开始，持续 {Seconds:F3} 秒", seconds);
            }
            else
            {
            _logger.LogInformation("机械快门已处于打开状态，将在 {Seconds:F3} 秒后关闭", seconds);
            }

            IsMasterSwitchOn = shutter.IsOpen;

            var countdownStartUtc = DateTime.UtcNow;
            while (true)
            {
                var elapsed = DateTime.UtcNow - countdownStartUtc;
                var remaining = delay - elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                TimedShutterRemainingSecondsText = Math.Max(0.0, remaining.TotalSeconds).ToString("F1", CultureInfo.CurrentCulture);
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            await shutter.SetOpenAsync(false);
            IsMasterSwitchOn = shutter.IsOpen;
            _logger.LogInformation("机械快门定时开启已完成，快门已关闭");
            TimedShutterRemainingSecondsText = "0.0";
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("机械快门定时操作已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "机械快门定时操作失败");
        }
        finally
        {
            if (ReferenceEquals(_timedShutterCts, cts))
            {
                _timedShutterCts = null;
            }

            IsTimedShutterCountingDown = false;

            cts.Dispose();
        }
    }

    [RelayCommand]
    // 鏆撮湶鏃堕棿璁剧疆锛歎I 杈撳叆涓?ms锛岃澶囧睘鎬т负 us銆?
    private void SetExposure()
    {
        if (_currentCamera != null)
        {
            if (double.TryParse(ExposureTimeInput, NumberStyles.Float, CultureInfo.CurrentCulture, out double ms)
                || double.TryParse(ExposureTimeInput, NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
            {
                // UI 鍗曚綅涓?ms锛岃繖閲岃浆鎹负 us 鍚庡啓鍥炶澶囥€?
                ms = SanitizePositive(ms, 10.0);
                var us = ms * 1000.0;
                _currentCamera.ExposureTimeUs = us;
                if (ReferenceEquals(_currentCamera, _angleCamera))
                {
                    _angleCameraExposureMs = ms;
                }
                else if (ReferenceEquals(_currentCamera, _spotCamera))
                {
                    _spotCameraExposureMs = ms;
                }
                _logger.LogInformation("曝光时间已设置为 {Ms} 毫秒（{Us} 微秒）", ms, us);
                
                // Update input to formatted string to confirm set
                // ExposureTimeInput = ms.ToString(); 
            }
            else
            {
                _logger.LogWarning("曝光时间格式无效：{Input}", ExposureTimeInput);
            }
        }
        else
        {
            _logger.LogWarning("无法设置曝光时间：当前未选择相机");
        }
    }

    // 鍏煎鐜版湁 XAML 鍛戒护鍚?SaveParameters銆?
    [RelayCommand]
    private async Task SaveParametersAsync()
    {
        await SaveAngleAdjustParamsAsync();
    }

    [RelayCommand]
    // 淇濆瓨鈥滄潯绾瑰懆鏈?-> 鍥涜酱浣嶅Э鈥濇爣瀹氱偣锛涘悓鍛ㄦ湡浜屾淇濆瓨鏃朵細鍏堣闂槸鍚﹁鐩栥€?
    private async Task SaveAngleAdjustParamsAsync()
    {
            // 灏嗗綋鍓嶅懆鏈熶笌鍥涜酱浣嶇疆鍐欏叆 angle-adjust-params.json銆?
        try
        {
            if (!double.IsFinite(StripePeriodUm) || StripePeriodUm < 3 || StripePeriodUm > 500)
            {
                MessageBox.Show("当前条纹周期无效，必须在 3.0 到 500.0 μm 之间。", "保存参数", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!EnsureAngleAdjustDevicesReady("保存参数"))
            {
                return;
            }

            var pos1 = await _actuator1!.GetPositionAsync();
            var pos2 = await _actuator2!.GetPositionAsync();
            var posV = await _verticalStage!.GetPositionAsync();
            var posH = await _horizontalStage!.GetPositionAsync();

            var root = FindRepoRootContainingSrc(AppContext.BaseDirectory);
            var filePath = Path.Combine(root, "angle-adjust-params.json");
            var store = await LoadParamsStoreAsync(filePath);

            // 浠ヤ竴浣嶅皬鏁板懆鏈熶綔涓轰富閿仛 upsert锛屽拰 UI 灞曠ず绮惧害淇濇寔涓€鑷淬€?
            var periodUm = Math.Round(StripePeriodUm, 1);
            var existing = store.Entries.FirstOrDefault(e => Math.Round(e.StripePeriodUm, 1) == periodUm);
            
            var newEntry = new AngleAdjustParamsEntry
            {
                StripePeriodUm = periodUm,
                StagePositionsMm = new StagePositionsMm
                {
                    Actuator1 = pos1,
                    Actuator2 = pos2,
                    Vertical = posV,
                    Horizontal = posH
                }
            };

            if (existing != null)
            {
                var result = MessageBox.Show(
                    $"周期 {periodUm:F1} μm 已存在，是否覆盖原有参数？",
                    "覆盖确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    existing.StagePositionsMm = newEntry.StagePositionsMm;
                }
                else
                {
                    // 鐢ㄦ埛鍙栨秷瑕嗙洊锛岀洿鎺ヨ繑鍥炪€?
                    return;
                }
            }
            else
            {
                store.Entries.Add(newEntry);
            }

            // 鎸夊懆鏈熷崌搴忎繚瀛橈紝渚夸簬鍚庣画鎻掑€兼煡鎵鹃偦鐐广€?
            store.Entries = store.Entries.OrderBy(e => e.StripePeriodUm).ToList();

            store.Version = 4;
            await SaveParamsStoreAsync(filePath, store);

            _logger.LogInformation("已将周期 {PeriodUm:F1} 微米的调整参数保存到 {FilePath}", periodUm, filePath);
            MessageBox.Show($"已保存 {periodUm:F1} μm 的四轴参数。", "保存参数", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存周期调整参数失败");
            MessageBox.Show($"保存参数失败：{ex.Message}", "保存参数", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateStageHeaders()
    {
        if (_actuator1 != null) Actuator1HeaderText = $"促动器1控制 ({_actuator1.Name})";
        if (_actuator2 != null) Actuator2HeaderText = $"促动器2控制 ({_actuator2.Name})";
        if (_verticalStage != null) VerticalHeaderText = $"垂直位移台控制 ({_verticalStage.Name})";
        if (_horizontalStage != null) HorizontalHeaderText = $"水平位移台控制 ({_horizontalStage.Name})";
    }

    private void ResetStageHeaders()
    {
        Actuator1HeaderText = "促动器1控制";
        Actuator2HeaderText = "促动器2控制";
        VerticalHeaderText = "垂直位移台控制";
        HorizontalHeaderText = "水平位移台控制";
    }

    private void UpdateActiveCameraState()
    {
        ActiveCameraName = _currentCamera?.Name;
        IsActiveCameraConnected = _currentCamera?.State == DeviceState.Connected;
    }

    // 甯у洖璋冨悓鏃舵壙鎷呬笁浠朵簨锛?
    // 1) 鍙€夋潯绾瑰垎鏋愶紱2) 鏇存柊 UI 鍥惧儚涓庣粺璁￠噺锛?) 鍦ㄦ姄鎷嶅満鏅紓姝ヨ惤鐩樺師濮嬪抚銆?
    private void OnFrameReceived(object? sender, ImageFrame e)
    {
        var sourceData = e.Data;
        if (sourceData == null || sourceData.Length == 0)
        {
            return;
        }

        // Keep at most one waiting frame. The camera thread must never wait for UI rendering or analysis.
        if (Volatile.Read(ref _frameProcessorRunning) != 0 && Volatile.Read(ref _latestPendingFrame) != null)
        {
            return;
        }

        byte[] copiedData;
        try
        {
            copiedData = (byte[])sourceData.Clone();
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "暂存相机帧时内存不足，已丢弃该帧");
            return;
        }

        Interlocked.Exchange(ref _latestPendingFrame, new ImageFrame
        {
            Width = e.Width,
            Height = e.Height,
            Timestamp = e.Timestamp,
            Data = copiedData
        });

        if (Interlocked.CompareExchange(ref _frameProcessorRunning, 1, 0) == 0)
        {
            _ = Task.Run(ProcessLatestFrames);
        }
    }

    private void ProcessLatestFrames()
    {
        try
        {
            while (Interlocked.Exchange(ref _latestPendingFrame, null) is { } frame)
            {
                ProcessFrame(frame);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _frameProcessorRunning, 0);
            if (Volatile.Read(ref _latestPendingFrame) != null
                && Interlocked.CompareExchange(ref _frameProcessorRunning, 1, 0) == 0)
            {
                _ = Task.Run(ProcessLatestFrames);
            }
        }
    }

    private void ProcessFrame(ImageFrame e)
    {
        var frameSeq = Interlocked.Increment(ref _frameEventSeq);
        var frameSw = Stopwatch.StartNew();

        LogProcessingDiagnostics(
            "[处理诊断] 收到帧：序号={Seq}，相机={Camera}，尺寸={Width}x{Height}，字节数={Bytes}，冻结={Frozen}，待抓拍={SnapPending}，图像处理={ImageProcessing}",
            frameSeq,
            _currentCamera?.Name ?? "<null>",
            e.Width,
            e.Height,
            e.Data?.Length ?? 0,
            _isFrameFrozen,
            _isSnapPending,
            IsImageProcessingEnabled);

        try
        {
            var acceptFrame = !_isFrameFrozen || _isSnapPending;
            if (!acceptFrame)
            {
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 已丢弃：画面已冻结且没有待处理抓拍", frameSeq);
                return;
            }

            var isSnapFrame = _isSnapPending;
            if (isSnapFrame) _isSnapPending = false;

            LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 已接收，是否为抓拍帧={Snap}", frameSeq, isSnapFrame);

            // Snap frame should be analyzed at full resolution; periodic frames use center crop.
            TryScheduleStripeAnalysis(e, frameSeq, forceFullResolution: isSnapFrame);

            bool shouldRenderUi = true;
            if (!isSnapFrame)
            {
                var nowTick = Environment.TickCount64;
                var lastTick = Interlocked.Read(ref _lastUiFrameTickMs);
                if (nowTick - lastTick < UiFrameIntervalMs)
                {
                    LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 被界面节流丢弃：间隔={Delta} 毫秒，阈值={Threshold} 毫秒", frameSeq, nowTick - lastTick, UiFrameIntervalMs);
                    return;
                }

                Interlocked.Exchange(ref _lastUiFrameTickMs, nowTick);
            }

            if (Interlocked.CompareExchange(ref _uiRenderQueued, 1, 0) != 0)
            {
                // UI is still handling the previous frame; drop this preview frame to avoid queue growth.
                shouldRenderUi = false;
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 遇到界面渲染队列繁忙，将跳过预览渲染", frameSeq);
            }

            if (!shouldRenderUi && !isSnapFrame)
            {
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 已丢弃：界面繁忙且不是抓拍帧", frameSeq);
                return;
            }

            var sourceData = e.Data;
            if (sourceData == null || sourceData.Length == 0)
            {
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 已丢弃：帧数据为空", frameSeq);
                return;
            }

            // The camera callback already copied this frame into the one-slot processing queue.
            var frameData = sourceData;

            ImageFrame? frameCopyForSave = null;
            if (isSnapFrame)
            {
                frameCopyForSave = new ImageFrame
                {
                    Width = e.Width,
                    Height = e.Height,
                    Timestamp = e.Timestamp,
                    Data = frameData
                };
            }

            int maxVal =0;
            int width = e.Width;
            int height = e.Height;

            try
            {
                var statsSw = Stopwatch.StartNew();
                // Calculate95% threshold of grayscale values
                // Support:8-bit gray (1Bpp) or16-bit little-endian where lower10 bits are valid (2Bpp)
                int pixels = width * height;
                int bytesPerPixel = Math.Max(1, frameData.Length / Math.Max(1, pixels));

                if (bytesPerPixel >=2 && frameData.Length >= pixels *2)
                {
                    //10-bit histogram (0..1023)
                    int[] histogram10 = new int[1024];
                    for (int i =0; i < pixels; i++)
                    {
                        int o = i *2;
                        int v = (frameData[o] | (frameData[o +1] <<8)) &0x03FF;
                        histogram10[v]++;
                    }

                    int total = pixels;
                    int target = (int)(total *0.95);
                    int cumulative =0;
                    int p95 =1023;
                    for (int i =0; i <1024; i++)
                    {
                        cumulative += histogram10[i];
                        if (cumulative >= target)
                        {
                            p95 = i;
                            break;
                        }
                    }

                    // Keep existing UI MaxValue95 int but scale to0..255 for backwards display
                    maxVal = p95;
                }
                else
                {
                    //8-bit path
                    int[] histogram = new int[256];
                         for (int i =0; i < frameData.Length; i++)
                    {
                             histogram[frameData[i]]++;
                    }

                         int total = frameData.Length;
                    int target = (int)(total *0.95);
                    int cumulative =0;
                    maxVal =255;
                    for (int i =0; i <256; i++)
                    {
                        cumulative += histogram[i];
                        if (cumulative >= target)
                        {
                            maxVal = i;
                            break;
                        }
                    }

                    // 缁熶竴鎸?10-bit 鏄剧ず锛?..1023锛?
                    maxVal = (int)Math.Round(maxVal * 1023.0 / 255.0);
                }

                statsSw.Stop();
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 的直方图和 95% 最大值计算耗时 {ElapsedMs} 毫秒，最大值={Max95}", frameSeq, statsSw.ElapsedMilliseconds, maxVal);
            }
            catch { }

            // WPF 浣嶅浘鏇存柊蹇呴』鍦?UI 绾跨▼鎵ц銆備娇鐢?BeginInvoke 閬垮厤闃诲閲囬泦绾跨▼銆?
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (shouldRenderUi && dispatcher != null)
            {
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 正在安排界面渲染", frameSeq);
                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    var uiSw = Stopwatch.StartNew();
                    try
                    {
                       int w = width;
                       int h = height;
                       int pixels = w * h;
                       int bytesPerPixel = Math.Max(1, frameData.Length / Math.Max(1, pixels));

                       PixelFormat pixelFormat;
                       int stride;

                       // Display: if data looks like10-bit packed in16-bit, show as Gray16 (no gamma)
                       if (bytesPerPixel >=2 && frameData.Length >= pixels *2)
                       {
                           pixelFormat = PixelFormats.Gray16;
                           stride = w *2;

                           if (_backBuffer == null || _backBuffer.Length < stride * h)
                               _backBuffer = new byte[stride * h];

                           // For display, copy exactly w*h*2 bytes
                           Buffer.BlockCopy(frameData,0, _backBuffer,0, stride * h);
                       }
                       else
                       {
                           stride = bytesPerPixel * w;
                           pixelFormat = bytesPerPixel >=3 ? PixelFormats.Bgr24 : PixelFormats.Gray8;

                           if (_backBuffer == null || _backBuffer.Length < stride * h)
                               _backBuffer = new byte[stride * h];

                           Buffer.BlockCopy(frameData,0, _backBuffer,0, Math.Min(frameData.Length, _backBuffer.Length));
                       }

                       if (_writeableBitmap == null || _writeableBitmap.PixelWidth != w || _writeableBitmap.PixelHeight != h || _writeableBitmap.Format != pixelFormat)
                       {
                           _writeableBitmap = new WriteableBitmap(w, h,96,96, pixelFormat, null);
                           ActiveFrame = _writeableBitmap;
                       }

                       var rect = new System.Windows.Int32Rect(0,0, w, h);
                       _writeableBitmap.WritePixels(rect, _backBuffer, stride,0);

                       MaxValue95 = maxVal;

                       uiSw.Stop();
                       LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 的界面渲染完成，耗时 {ElapsedMs} 毫秒", frameSeq, uiSw.ElapsedMilliseconds);
                    }
                    catch (OutOfMemoryException ex)
                    {
                        _logger.LogError(ex, "更新相机画面时内存不足，正在释放帧资源");
                        ActiveFrame = null;
                        _writeableBitmap = null;
                        _backBuffer = null;
                        _isFrameFrozen = true;
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                    catch
                    {
                        // ignore frame errors for now
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _uiRenderQueued, 0);
                    }
                }), DispatcherPriority.Render);
            }
            else
            {
                Interlocked.Exchange(ref _uiRenderQueued, 0);
            }

            if (frameCopyForSave != null)
            {
                _ = SaveFrameToDiskAsync(frameCopyForSave);
                LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 已安排抓拍保存", frameSeq);
            }
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "处理相机帧时内存不足，已冻结画面更新");
            _isFrameFrozen = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "帧回调发生未处理异常，已丢弃该帧");
        }
        finally
        {
            frameSw.Stop();
            LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 回调总耗时 {ElapsedMs} 毫秒", frameSeq, frameSw.ElapsedMilliseconds);
        }
    }

    private void TryScheduleStripeAnalysis(ImageFrame frame, long frameSeq, bool forceFullResolution = false)
    {
        if (!IsImageProcessingEnabled)
        {
            LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 跳过分析：图像处理已关闭", frameSeq);
            return;
        }

        var nowTick = Environment.TickCount64;

        var lastTick = Interlocked.Read(ref _lastAnalysisTickMs);
        if (!forceFullResolution && nowTick - lastTick < _analysisIntervalMs)
        {
            LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 因分析间隔而跳过：间隔={Delta} 毫秒，设定={Interval} 毫秒", frameSeq, nowTick - lastTick, _analysisIntervalMs);
            return;
        }

        if (Interlocked.CompareExchange(ref _analysisWorkerRunning, 1, 0) != 0)
        {
            // The single analysis worker prevents CPU work and frame buffers from accumulating.
            LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 跳过分析：分析任务正忙", frameSeq);
            return;
        }

        Interlocked.Exchange(ref _lastAnalysisTickMs, nowTick);
        Interlocked.Exchange(ref _analysisWorkerStartTickMs, nowTick);

        try
        {
            int len = frame.Data.Length;
            if (_analysisBuffer == null || _analysisBuffer.Length != len)
            {
                _analysisBuffer = new byte[len];
            }

            Buffer.BlockCopy(frame.Data, 0, _analysisBuffer, 0, len);
            _analysisBufferWidth = frame.Width;
            _analysisBufferHeight = frame.Height;

            var analysisSeq = Interlocked.Increment(ref _analysisSeq);
            LogProcessingDiagnostics(
                "[处理诊断] 已安排分析 {AnalysisSeq}：来源帧={FrameSeq}，尺寸={Width}x{Height}，字节数={Bytes}，模式={Mode}",
                analysisSeq,
                frameSeq,
                _analysisBufferWidth,
                _analysisBufferHeight,
                len,
                forceFullResolution ? "全分辨率" : "中心裁剪");

            _ = RunStripeAnalysisOnceAsync(_analysisBuffer, _analysisBufferWidth, _analysisBufferHeight, analysisSeq, frameSeq, useCenterCrop: !forceFullResolution);
        }
        catch (Exception ex)
        {
            LogProcessingDiagnostics("[处理诊断] 帧 {Seq} 安排分析失败：{Error}", frameSeq, ex.Message);
            Interlocked.Exchange(ref _analysisWorkerRunning, 0);
            return;
        }
    }

    private async Task RunStripeAnalysisOnceAsync(byte[] data, int width, int height, long analysisSeq, long sourceFrameSeq, bool useCenterCrop)
    {
        var analysisSw = Stopwatch.StartNew();
        try
        {
            LogProcessingDiagnostics(
                "[处理诊断] 分析 {AnalysisSeq} 已开始：来源帧={FrameSeq}，尺寸={Width}x{Height}，字节数={Bytes}，模式={Mode}",
                analysisSeq,
                sourceFrameSeq,
                width,
                height,
                data.Length,
                useCenterCrop ? "中心裁剪" : "全分辨率");

            var analyzeTask = Task.Run(() => StripeAnalysis.Analyze(
                data,
                width,
                height,
                useCenterCrop));
            var completed = await Task.WhenAny(analyzeTask, Task.Delay(AnalysisTimeoutMs));
            if (completed != analyzeTask)
            {
                _logger.LogWarning("条纹分析超过 {TimeoutMs} 毫秒，本周期结果已跳过", AnalysisTimeoutMs);
                LogProcessingDiagnostics("[处理诊断] 分析 {AnalysisSeq} 在 {TimeoutMs} 毫秒后超时", analysisSeq, AnalysisTimeoutMs);
                // Keep the single-worker gate until the timed-out calculation actually exits.
                // This prevents repeated timeouts from accumulating CPU-bound tasks.
                try { await analyzeTask; } catch { }
                return;
            }

            var result = await analyzeTask;
            analysisSw.Stop();
            LogProcessingDiagnostics(
                "[处理诊断] 分析 {AnalysisSeq} 完成，耗时={ElapsedMs} 毫秒，成功={Success}，角度={Angle}，像素周期={Period}",
                analysisSeq,
                analysisSw.ElapsedMilliseconds,
                result.IsSuccess,
                result.StripeAngle,
                result.PeriodPixels);

            if (result.IsSuccess)
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = dispatcher.BeginInvoke(new Action(() =>
                    {
                        StripeAngleDeg = result.StripeAngle;
                        // Laboratory calibration: the image repetition period is twice the required physical grating period.
                        StripePeriodPixels = result.PeriodPixels * 0.5;
                        LogProcessingDiagnostics("[处理诊断] 分析 {AnalysisSeq} 的结果已应用到界面", analysisSeq);
                    }), DispatcherPriority.DataBind);
                }
            }
            else
            {
                LogProcessingDiagnostics("[处理诊断] 分析 {AnalysisSeq} 失败：{Error}", analysisSeq, result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "图像分析失败");
            LogProcessingDiagnostics("[处理诊断] 分析 {AnalysisSeq} 发生异常：{Error}", analysisSeq, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _analysisWorkerRunning, 0);
            Interlocked.Exchange(ref _analysisWorkerStartTickMs, 0);
            LogProcessingDiagnostics("[处理诊断] 分析 {AnalysisSeq} 已释放单任务门控", analysisSeq);
        }
    }

    private async Task SaveFrameToDiskAsync(ImageFrame frame)
    {
        try
        {
            var root = FindRepoRootContainingSrc(AppContext.BaseDirectory);
            var dir = Path.Combine(root, "photo");
            Directory.CreateDirectory(dir);

            var safeName = SanitizeFileName(_currentCamera?.Name ?? "camera");
            var filePath = Path.Combine(dir, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmssfff}.tif");

            int pixels = frame.Width * frame.Height;
            int bytesPerPixel = Math.Max(1, frame.Data.Length / Math.Max(1, pixels));

            await Task.Run(() =>
            {
                // 缁熶竴淇濆瓨涓?16-bit TIFF锛屼究浜庡悗缁绾垮垎鏋愪笌鍔ㄦ€佽寖鍥翠繚鐣欍€?
                ushort[] gray16;

                if (bytesPerPixel >=2 && frame.Data.Length >= pixels *2)
                {
                    // 16-bit 杈撳叆鎸夊皬绔鍙栵紱鑻ュ疄闄呮槸 10-bit 鏈夋晥浣嶏紝鍒欐斁澶у埌 16-bit 鍔ㄦ€佽寖鍥淬€?
                    gray16 = new ushort[pixels];
                    for (int i =0; i < pixels; i++)
                    {
                        int o = i *2;
                        int v16 = (frame.Data[o] | (frame.Data[o +1] <<8));
                        int v10 = v16 &0x03FF;
                        gray16[i] = (ushort)(v10 *65535 /1023);
                    }
                }
                else if (bytesPerPixel >=3 && frame.Data.Length >= pixels *3)
                {
                    // 褰╄壊杈撳叆鍏堣浆浜害鐏板害锛屽啀鎵╁睍鍒?16-bit銆?
                    gray16 = new ushort[pixels];
                    for (int i =0; i < pixels; i++)
                    {
                        int o = i *3;
                        // 涓庢樉绀鸿矾寰勪繚鎸佷竴鑷达紝鎸?BGR 椤哄簭瑙ｉ噴銆?
                        int b = frame.Data[o +0];
                        int g = frame.Data[o +1];
                        int r = frame.Data[o +2];
                        int y8 = (int)Math.Round(0.114 * b +0.587 * g +0.299 * r);
                        gray16[i] = (ushort)(y8 *257);
                    }
                }
                else
                {
                    // 8-bit 鐏板害鎸夌嚎鎬ф瘮渚嬫槧灏勫埌 16-bit銆?
                    gray16 = new ushort[pixels];
                    for (int i =0; i < pixels && i < frame.Data.Length; i++)
                    {
                        gray16[i] = (ushort)(frame.Data[i] *257);
                    }
                }

                int strideBytes = frame.Width *2;
                var bitmap = BitmapSource.Create(frame.Width, frame.Height,96,96, PixelFormats.Gray16, null, gray16, strideBytes);

                using var fs = File.Create(filePath);
                var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fs);
            });

            _logger.LogInformation("原始帧已保存 (16-bit TIFF): {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存原始帧失败");
        }
    }

    // 浠庡綋鍓嶈繍琛岀洰褰曞悜涓婃煡鎵句粨搴撴牴鐩綍锛堝寘鍚?src 鐨勭洰褰曪級銆?
    private static string FindRepoRootContainingSrc(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                    return dir.FullName;

                dir = dir.Parent;
            }
        }
        catch
        {
            // ignore
        }

        return startDir;
    }

    // 璇诲彇鍙傛暟鏂囦欢澶辫触鏃惰繑鍥炵┖瀵硅薄锛岄伩鍏嶅奖鍝嶄富娴佺▼銆?
    private static async Task<AngleAdjustParamsStore> LoadParamsStoreAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new AngleAdjustParamsStore();

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var document = JsonDocument.Parse(json);
            var version = document.RootElement.TryGetProperty("Version", out var versionElement)
                ? versionElement.GetInt32()
                : 1;

            if (version >= 4)
            {
                return JsonSerializer.Deserialize<AngleAdjustParamsStore>(json, options) ?? new AngleAdjustParamsStore();
            }

            if (version == 3)
            {
                var version3 = JsonSerializer.Deserialize<Version3AngleAdjustParamsStore>(json, options);
                return new AngleAdjustParamsStore
                {
                    Version = 4,
                    Entries = version3?.Entries.Select(entry => new AngleAdjustParamsEntry
                    {
                        StripePeriodUm = entry.StripePeriodUm,
                        StagePositionsMm = new StagePositionsMm
                        {
                            Actuator1 = entry.StagePositions.Actuator1Mm,
                            Actuator2 = entry.StagePositions.Actuator2Mm,
                            Vertical = ToMm(entry.StagePositions.VerticalUm),
                            Horizontal = ToMm(entry.StagePositions.HorizontalUm)
                        }
                    }).ToList() ?? new List<AngleAdjustParamsEntry>()
                };
            }

            if (version == 2)
            {
                var version2 = JsonSerializer.Deserialize<Version2AngleAdjustParamsStore>(json, options);
                return new AngleAdjustParamsStore
                {
                    Version = 4,
                    Entries = version2?.Entries.Select(entry => new AngleAdjustParamsEntry
                    {
                        StripePeriodUm = entry.StripePeriodUm,
                        StagePositionsMm = new StagePositionsMm
                        {
                            Actuator1 = entry.StagePositionsMm.Actuator1,
                            Actuator2 = entry.StagePositionsMm.Actuator2,
                            Vertical = entry.StagePositionsMm.Vertical,
                            Horizontal = entry.StagePositionsMm.Horizontal
                        }
                    }).ToList() ?? new List<AngleAdjustParamsEntry>()
                };
            }

            var legacy = JsonSerializer.Deserialize<LegacyAngleAdjustParamsStore>(json, options);
            return new AngleAdjustParamsStore
            {
                Version = 4,
                Entries = legacy?.Entries.Select(entry => new AngleAdjustParamsEntry
                {
                    StripePeriodUm = entry.StripePeriodUm,
                    StagePositionsMm = new StagePositionsMm
                    {
                        Actuator1 = ToMm(entry.StagePositionsUm.Actuator1),
                        Actuator2 = ToMm(entry.StagePositionsUm.Actuator2),
                        Vertical = ToMm(entry.StagePositionsUm.Vertical),
                        Horizontal = ToMm(entry.StagePositionsUm.Horizontal)
                    }
                }).ToList() ?? new List<AngleAdjustParamsEntry>()
            };
        }
        catch
        {
            return new AngleAdjustParamsStore();
        }
    }

    // 涓存椂鏂囦欢 + 鍘熷瓙鏇挎崲锛岄檷浣庨厤缃啓鍏ヨ繃绋嬩腑鍑虹幇鍗婂啓鏂囦欢鐨勬鐜囥€?
    private static async Task SaveParamsStoreAsync(string filePath, AngleAdjustParamsStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var tmp = filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json);

        // Remove existing file if present, then move tmp into its place.
        // No backup (.bak) is kept.
        if (File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }
        
        try
        {
            File.Move(tmp, filePath);
        }
        catch
        {
            // If move fails, try copy/delete fallback
            File.Copy(tmp, filePath, true);
            File.Delete(tmp);
        }
    }

    // 鎶婄浉鏈哄悕涓殑闈炴硶鏂囦欢鍚嶅瓧绗︽浛鎹负涓嬪垝绾匡紝闃叉淇濆瓨鎴浘澶辫触銆?
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >=0 ? '_' : c);
        }
        return sb.ToString();
    }

    private static string FormatWithFixed2Decimals(double value)
    {
        if (!double.IsFinite(value)) return "-";
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded.ToString("F4", CultureInfo.CurrentCulture);
    }

    // 绫诲瀷鑱岃矗锛氳搴﹁皟鑺傚弬鏁版枃浠剁殑鏍瑰璞★紝鎸佹湁鐗堟湰鍙峰拰鏉＄洰闆嗗悎銆?
    private sealed class AngleAdjustParamsStore
    {
        public int Version { get; set; } = 4;
        public List<AngleAdjustParamsEntry> Entries { get; set; } = new();
    }

    // 绫诲瀷鑱岃矗锛氬崟涓潯绾瑰懆鏈熷搴旂殑鍥涜酱鐩爣浣嶅Э璁板綍銆?
    private sealed class AngleAdjustParamsEntry
    {
        public double StripePeriodUm { get; set; }
        public StagePositionsMm StagePositionsMm { get; set; } = new();
    }

    private sealed class StagePositionsMm
    {
        public double Actuator1 { get; set; }
        public double Actuator2 { get; set; }
        public double Vertical { get; set; }
        public double Horizontal { get; set; }

        public bool AreFinite() =>
            double.IsFinite(Actuator1)
            && double.IsFinite(Actuator2)
            && double.IsFinite(Vertical)
            && double.IsFinite(Horizontal);
    }

    private sealed class Version3AngleAdjustParamsStore
    {
        public List<Version3AngleAdjustParamsEntry> Entries { get; set; } = new();
    }

    private sealed class Version3AngleAdjustParamsEntry
    {
        public double StripePeriodUm { get; set; }
        public Version3StagePositions StagePositions { get; set; } = new();
    }

    private sealed class Version3StagePositions
    {
        public double Actuator1Mm { get; set; }
        public double Actuator2Mm { get; set; }
        public double VerticalUm { get; set; }
        public double HorizontalUm { get; set; }
    }

    private sealed class Version2AngleAdjustParamsStore
    {
        public List<Version2AngleAdjustParamsEntry> Entries { get; set; } = new();
    }

    private sealed class Version2AngleAdjustParamsEntry
    {
        public double StripePeriodUm { get; set; }
        public Version2StagePositionsMm StagePositionsMm { get; set; } = new();
    }

    private sealed class Version2StagePositionsMm
    {
        public double Actuator1 { get; set; }
        public double Actuator2 { get; set; }
        public double Vertical { get; set; }
        public double Horizontal { get; set; }
    }

    private sealed class LegacyAngleAdjustParamsStore
    {
        public List<LegacyAngleAdjustParamsEntry> Entries { get; set; } = new();
    }

    private sealed class LegacyAngleAdjustParamsEntry
    {
        public double StripePeriodUm { get; set; }
        public LegacyStagePositionsUm StagePositionsUm { get; set; } = new();
    }

    private sealed class LegacyStagePositionsUm
    {
        public double Actuator1 { get; set; }
        public double Actuator2 { get; set; }
        public double Vertical { get; set; }
        public double Horizontal { get; set; }
    }

    private sealed class ProcessingSettings
    {
        // 1=enable detailed processing diagnostics logs, 0=disable.
        public int EnableProcessingDiagnostics { get; set; } = 0;

        // Image-processing scheduling interval in milliseconds.
        public int AnalysisIntervalMs { get; set; } = DefaultAnalysisIntervalMs;
    }

    private sealed class UiStateSettings
    {
        public double AngleCameraExposureMs { get; set; } = 10.0;
        public double SpotCameraExposureMs { get; set; } = 10.0;
        public double? Actuator1StepSizeMm { get; set; }
        public double? Actuator2StepSizeMm { get; set; }
        public double Actuator1StepSizeUm { get; set; }
        public double Actuator2StepSizeUm { get; set; }
        public double? VerticalStageStepSizeMm { get; set; }
        public double? HorizontalStageStepSizeMm { get; set; }
        public double VerticalStageStepSizeUm { get; set; }
        public double HorizontalStageStepSizeUm { get; set; }
        public double TimedShutterOpenSeconds { get; set; } = 1.0;
    }
}



