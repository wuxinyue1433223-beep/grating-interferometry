// 鏂囦欢鑱岃矗锛歁VSDK 鐩告満椹卞姩瀹炵幇锛氳繛鎺ャ€侀噰闆嗗惊鐜€佸弬鏁颁笅鍙戜笌甯ц緭鍑恒€?

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.MVSDKCamera
{
    // 绫诲瀷鑱岃矗锛歁VSDK 鐩告満瀹炰綋锛屽疄鐜扮湡瀹炵浉鏈鸿繛鎺ャ€侀噰闆嗕笌甯ц浆鎹€?
    public class MVSDKCamera : ICamera
    {
        private readonly ILogger _logger;
        private MVSDK.tSdkCameraDevInfo? _cameraInfo;
        private readonly MVSDK.tSdkCameraDevInfo[]? _cameraList;
        private string? _requestedDeviceId;

        // MVSDK鐩稿叧
        private Int32 _cameraHandle = -1;
        private bool _isConnected = false;
        private bool _isCapturing = false;
        private CancellationTokenSource? _captureCts;
        private Task? _captureLoopTask;
        private readonly object _captureSync = new();
        private long _lastFrameEmitTickMs;
        private const int MinFrameEmitIntervalMs = 33;
        private Encoding _gbkEncoding;
        private byte[]? _mono8Buffer;
        private byte[]? _mono16Buffer;
        private byte[]? _rgbManagedBuffer;
        private IntPtr _rgbNativeBuffer = IntPtr.Zero;
        private int _rgbNativeBufferSize;

        // 甯х巼璁＄畻
        private Stopwatch _frameStopwatch = new Stopwatch();
        private int _frameCount = 0;
        private double _currentFps = 0;
        private object _fpsLock = new object();
        private readonly object _cameraParamSync = new();

        private double _exposureTimeUs = 10000.0;
        private bool _hasUserExposurePreference;

        public MVSDKCamera(ILogger logger, MVSDK.tSdkCameraDevInfo? cameraInfo = null, string? name = null, MVSDK.tSdkCameraDevInfo[]? cameraList = null)
        {
            _logger = logger;
            _cameraInfo = cameraInfo;
            _cameraList = cameraList;

            // 鍒濆鍖栫紪鐮佸櫒锛岄伩鍏嶅湪 BytesToString 涓┖寮曠敤
            try
            {
                _gbkEncoding = Encoding.GetEncoding("GBK");
            }
            catch
            {
                _gbkEncoding = Encoding.Default;
            }

            DeviceId = _cameraInfo?.acSn != null ?
                BytesToString(_cameraInfo.Value.acSn, false) :
                Guid.NewGuid().ToString();

            Name = name ?? (_cameraInfo != null ?
                GetCameraDisplayName(_cameraInfo.Value) :
                "MVSDK Camera");

            State = DeviceState.Disconnected;
        }

        public string DeviceId { get; private set; }
        public string Name { get; private set; }
        public bool IsCloneable => false;
        public DeviceState State { get; private set; }

        public event EventHandler<DeviceState>? StateChanged;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<ImageFrame>? FrameReceived;

        public void SetOpenDeviceId(string deviceId)
        {
            if (State != DeviceState.Disconnected)
                throw new InvalidOperationException("相机已连接时不能更改设备编号。");

            _requestedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        }

        // ICamera鐗瑰畾灞炴€?
        public double ExposureTimeUs
        {
            get => _exposureTimeUs;
            set
            {
                if (value <= 0)
                {
                    _logger.LogWarning("忽略无效的曝光时间：{Val} 微秒", value);
                    return;
                }

                _exposureTimeUs = value;
                _hasUserExposurePreference = true;
                if (_cameraHandle != -1 && _isConnected)
                {
                    try
                    {
                        lock (_cameraParamSync)
                        {
                            EnsureManualExposureMode();
                            _ = TryApplyExposureTimeCore(value, persistToCamera: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "设置曝光时间时发生异常");
                    }
                }
            }
        }

        public async Task ConnectAsync(CancellationToken token = default)
        {
            try
            {
                _logger.LogInformation("正在连接 MVSDK 相机：{Name}", Name);

                // 鍒濆鍖朣DK锛堝鏋滆繕鏈垵濮嬪寲锛?
                MVSDK.CameraSdkStatus status = MVSDK.MvApi.CameraSdkInit(1);
                if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    throw new InvalidOperationException($"MVSDK 初始化失败：{status}");
                }

                // 鍐冲畾瑕佽繛鎺ョ殑鐩告満淇℃伅锛堝厛鐢ㄤ紶鍏ョ殑锛屽啀鐢ㄥ伐鍘傜紦瀛橈紝鏈€鍚庢灇涓撅級
                MVSDK.tSdkCameraDevInfo? cameraToConnect = null;
                if (!string.IsNullOrWhiteSpace(_requestedDeviceId) && _cameraList != null)
                {
                    foreach (var candidate in _cameraList)
                    {
                        if (string.Equals(BytesToString(candidate.acSn, false), _requestedDeviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            cameraToConnect = candidate;
                            break;
                        }
                    }

                    if (cameraToConnect == null || IsEmptySerial(cameraToConnect.Value))
                        throw new InvalidOperationException($"未找到映射的相机“{_requestedDeviceId}”。");
                }

                cameraToConnect ??= _cameraInfo;
                if ((cameraToConnect == null || IsEmptySerial(cameraToConnect.Value)) && _cameraList != null && _cameraList.Length > 0)
                {
                    cameraToConnect = _cameraList[0];
                }

                if (cameraToConnect == null)
                {
                    status = MVSDK.MvApi.CameraEnumerateDevice(out var cameras);
                    if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS || cameras == null || cameras.Length == 0)
                    {
                        throw new InvalidOperationException("未找到可用的相机设备。");
                    }
                    cameraToConnect = cameras[0];
                }

                if (cameraToConnect == null)
                {
                    throw new InvalidOperationException("相机信息为空，无法初始化相机。");
                }

                // 鍒濆鍖栫浉鏈?
                var cameraInfo = cameraToConnect.Value;
                _cameraInfo = cameraInfo;
                DeviceId = BytesToString(cameraInfo.acSn, false);
                Name = GetCameraDisplayName(cameraInfo);
                status = MVSDK.MvApi.CameraInit(ref cameraInfo, -1, -1, ref _cameraHandle);
                if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    throw new InvalidOperationException($"相机初始化失败：{status}");
                }

                // Try restoring persisted camera parameters first.
                try
                {
                    _ = MVSDK.MvApi.CameraLoadParameter(_cameraHandle, (int)MVSDK.emSdkParameterTeam.PARAMETER_TEAM_A);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "加载相机参数组 A 失败");
                }

                // Prefer the official MONO8 output path to reduce memory bandwidth and conversion cost.
                try
                {
                    var enumType = typeof(MVSDK.emImageFormat);
                    var mono8Field = enumType.GetField("CAMERA_MEDIA_TYPE_MONO8");
                    if (mono8Field?.GetValue(null) is MVSDK.emImageFormat mono8)
                    {
                        MVSDK.MvApi.CameraSetIspOutFormat(_cameraHandle, (uint)mono8);
                        _logger.LogInformation("已通过官方接口将相机输出格式设置为 MONO8");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "设置 MONO8 输出格式失败，将使用相机默认输出格式");
                }

                // 绂佺敤鑷姩鏇濆厜锛岄伩鍏嶆墜鍔ㄨ缃洕鍏夊悗浠嶈 AE 鎸佺画鎷夊洖銆?
                try
                {
                    lock (_cameraParamSync)
                    {
                        EnsureManualExposureMode();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "连接后切换到手动曝光模式失败");
                }

                // 寮€濮嬮噰闆?
                status = MVSDK.MvApi.CameraPlay(_cameraHandle);
                if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    MVSDK.MvApi.CameraUnInit(_cameraHandle);
                    _cameraHandle = -1;
                    throw new InvalidOperationException($"启动相机采集失败：{status}");
                }

                _isConnected = true;
                State = DeviceState.Connected;
                StateChanged?.Invoke(this, State);

                lock (_cameraParamSync)
                {
                    EnsureManualExposureMode();

                    if (_hasUserExposurePreference)
                    {
                        _ = TryApplyExposureTimeCore(_exposureTimeUs, persistToCamera: false);
                    }
                    else
                    {
                        // Read current device value (could come from restored parameter group).
                        double currentExposure = 0;
                        status = MVSDK.MvApi.CameraGetExposureTime(_cameraHandle, ref currentExposure);
                        if (status == MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS && currentExposure > 0)
                        {
                            _exposureTimeUs = currentExposure;
                        }
                    }
                }

                // 鍒濆鍖栧抚鐜囩粺璁?
                _frameStopwatch.Restart();
                _frameCount = 0;

                _logger.LogInformation("MVSDK 相机连接成功：{Name}", Name);
            }
            catch (Exception ex)
            {
                try
                {
                    if (_cameraHandle != -1)
                    {
                        try { MVSDK.MvApi.CameraStop(_cameraHandle); } catch { }
                        try { MVSDK.MvApi.CameraUnInit(_cameraHandle); } catch { }
                        _cameraHandle = -1;
                    }
                }
                catch
                {
                    // ignore cleanup exceptions in failure path
                }

                _isConnected = false;
                _logger.LogError(ex, "连接 MVSDK 相机失败");
                State = DeviceState.Error;
                StateChanged?.Invoke(this, State);
                ErrorOccurred?.Invoke(this, ex.Message);
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                await StopCaptureAsync();

                if (_cameraHandle != -1)
                {
                    MVSDK.MvApi.CameraStop(_cameraHandle);
                    MVSDK.MvApi.CameraUnInit(_cameraHandle);
                    _cameraHandle = -1;
                }

                _isConnected = false;
                State = DeviceState.Disconnected;
                StateChanged?.Invoke(this, State);

                _frameStopwatch.Stop();

                _logger.LogInformation("MVSDK 相机已断开：{Name}", Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开 MVSDK 相机时发生异常");
                throw;
            }
        }

        public Task StartCaptureAsync()
        {
            if (State != DeviceState.Connected)
            {
                _logger.LogWarning("无法开始采集：相机尚未连接");
                return Task.CompletedTask;
            }

            lock (_captureSync)
            {
                if (_isCapturing)
                {
                    return Task.CompletedTask;
                }

                _isCapturing = true;
                _captureCts = new CancellationTokenSource();
                _captureLoopTask = Task.Run(() => CaptureLoop(_captureCts.Token), _captureCts.Token);
            }

            _logger.LogInformation("MVSDK 相机开始连续采集：{Name}", Name);
            return Task.CompletedTask;
        }

        public async Task StopCaptureAsync()
        {
            CancellationTokenSource? cts;
            Task? loopTask;

            lock (_captureSync)
            {
                cts = _captureCts;
                loopTask = _captureLoopTask;
                _captureCts = null;
                _captureLoopTask = null;
                _isCapturing = false;
            }

            cts?.Cancel();

            if (loopTask != null)
            {
                try
                {
                    await loopTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("等待相机采集任务退出超时：{Name}", Name);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "相机采集任务退出时发生异常：{Name}", Name);
                }
            }

            cts?.Dispose();

            _logger.LogInformation("MVSDK 相机停止采集：{Name}", Name);
        }

        public async Task SnapAsync()
        {
            if (!_isConnected || _cameraHandle == -1)
                throw new InvalidOperationException("相机尚未连接。");

            var triggerModeChanged = false;
            try
            {
                var status = MVSDK.MvApi.CameraSetTriggerMode(_cameraHandle, 1);
                if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    throw new InvalidOperationException($"进入软触发模式失败：{status}");

                triggerModeChanged = true;
                status = MVSDK.MvApi.CameraSoftTrigger(_cameraHandle);
                if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    throw new InvalidOperationException($"执行软触发失败：{status}");

                await GrabSingleFrameAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "相机软触发抓拍失败");
                throw;
            }
            finally
            {
                if (triggerModeChanged && _cameraHandle != -1)
                {
                    try
                    {
                        var restoreStatus = MVSDK.MvApi.CameraSetTriggerMode(_cameraHandle, 0);
                        if (restoreStatus != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                            _logger.LogWarning("抓拍后恢复连续采集模式失败：{Status}", restoreStatus);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "抓拍后恢复连续采集模式时发生异常");
                    }
                }
            }
        }

        public void Dispose()
        {
            try
            {
                StopCaptureAsync().Wait(1000);
                DisconnectAsync().Wait(1000);

                if (_cameraHandle != -1)
                {
                    MVSDK.MvApi.CameraUnInit(_cameraHandle);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放 MVSDK 相机资源时发生异常");
            }
            finally
            {
                if (_rgbNativeBuffer != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(_rgbNativeBuffer); } catch { }
                    _rgbNativeBuffer = IntPtr.Zero;
                    _rgbNativeBufferSize = 0;
                }
            }
        }

        #region 绉佹湁鏂规硶
        private async Task CaptureLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isCapturing && _isConnected)
            {
                try
                {
                    GrabSingleFrame();

                    // 鏍规嵁甯х巼閫傚綋寤惰繜
                    await Task.Delay(1, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "连续采集循环发生异常");
                    break;
                }
            }

            _isCapturing = false;
        }

        private async Task GrabSingleFrameAsync()
        {
            await Task.Run(GrabSingleFrame);
        }

        private void GrabSingleFrame()
        {
            if (_cameraHandle == -1) return;

            try
            {
                MVSDK.tSdkFrameHead frameHead;
                IntPtr pFrameBuffer;

                var status = MVSDK.MvApi.CameraGetImageBuffer(
                    _cameraHandle,
                    out frameHead,
                    out pFrameBuffer,
                    1000);

                if (status == MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    try
                    {
                        var nowTick = Environment.TickCount64;
                        var lastTick = Interlocked.Read(ref _lastFrameEmitTickMs);
                        if (nowTick - lastTick < MinFrameEmitIntervalMs)
                        {
                            return;
                        }

                        var frameHandler = FrameReceived;
                        if (frameHandler == null)
                        {
                            return;
                        }

                        Interlocked.Exchange(ref _lastFrameEmitTickMs, nowTick);

                        // 鏇存柊甯х巼缁熻
                        UpdateFps();

                        // 澶勭悊鍥惧儚鏁版嵁
                        byte[] imageData = ProcessImageData(pFrameBuffer, frameHead);

                        // 瑙﹀彂甯ф帴鏀朵簨浠?
                        var frame = new ImageFrame
                        {
                            Width = frameHead.iWidth,
                            Height = frameHead.iHeight,
                            Timestamp = DateTime.UtcNow,
                            Data = imageData,
                        };

                        frameHandler.Invoke(this, frame);
                    }
                    finally
                    {
                        MVSDK.MvApi.CameraReleaseImageBuffer(_cameraHandle, pFrameBuffer);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "抓取单帧图像失败");
                ErrorOccurred?.Invoke(this, $"抓取图像失败：{ex.Message}");
            }
        }

        private byte[] ProcessImageData(IntPtr pFrameBuffer, MVSDK.tSdkFrameHead frameHead)
        {
            // Prefer keeping monochrome data in16-bit containers so the pipeline can treat it as Mono10/Mono16.
            // For10-bit sources we keep lower10 bits (0..1023) in UInt16 LE.
            // For16-bit sources we keep full UInt16 LE.

            int pixels = frameHead.iWidth * frameHead.iHeight;

            // Mono8: keep native 8-bit buffer for low memory footprint on preview path.
            if (frameHead.uiMediaType == (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8)
            {
                EnsureMono8Buffer(pixels);
                Marshal.Copy(pFrameBuffer, _mono8Buffer!,0, pixels);
                return _mono8Buffer!;
            }

            // Attempt to handle common mono10/12/16 formats by copying raw buffer.
            // NOTE: Different cameras/SDKs may use different enum names; we check by numeric value if available.
            // If the SDK provides mono16/mono10 packed buffers, we prefer raw output.
            try
            {
                // Many MVSDK builds expose CAMERA_MEDIA_TYPE_MONO10 / MONO12 / MONO16.
                // We do a best-effort check via Enum.GetName to avoid hard dependency.
                var enumType = typeof(MVSDK.emImageFormat);
                string? name = Enum.GetName(enumType, (MVSDK.emImageFormat)frameHead.uiMediaType);

                if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("CAMERA_MEDIA_TYPE_MONO", StringComparison.OrdinalIgnoreCase) && !name.Equals("CAMERA_MEDIA_TYPE_MONO8", StringComparison.OrdinalIgnoreCase))
                {
                    // For MONO10/MONO12/MONO16: assume2 bytes per pixel delivered.
                    int bytes = pixels *2;
                    EnsureMono16Buffer(bytes);
                    Marshal.Copy(pFrameBuffer, _mono16Buffer!,0, bytes);
                    return _mono16Buffer!;
                }
            }
            catch
            {
                // ignore and fallback below
            }

            // Other formats: convert to RGB for display
            var rgbSize = frameHead.iWidth * frameHead.iHeight *3;
            EnsureRgbBuffers(rgbSize);
            var frameHeadCopy = frameHead;
            MVSDK.MvApi.CameraImageProcess(_cameraHandle, pFrameBuffer, _rgbNativeBuffer, ref frameHeadCopy);

            rgbSize = frameHeadCopy.iWidth * frameHeadCopy.iHeight *3;
            EnsureRgbBuffers(rgbSize);
            Marshal.Copy(_rgbNativeBuffer, _rgbManagedBuffer!,0, rgbSize);

            // Return RGB data
            return _rgbManagedBuffer!;
        }

        private void EnsureMono8Buffer(int size)
        {
            if (_mono8Buffer == null || _mono8Buffer.Length < size)
            {
                _mono8Buffer = new byte[size];
            }
        }

        private void EnsureMono16Buffer(int size)
        {
            if (_mono16Buffer == null || _mono16Buffer.Length < size)
            {
                _mono16Buffer = new byte[size];
            }
        }

        private void EnsureRgbBuffers(int size)
        {
            if (_rgbManagedBuffer == null || _rgbManagedBuffer.Length < size)
            {
                _rgbManagedBuffer = new byte[size];
            }

            if (_rgbNativeBuffer == IntPtr.Zero || _rgbNativeBufferSize < size)
            {
                if (_rgbNativeBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_rgbNativeBuffer);
                }

                _rgbNativeBuffer = Marshal.AllocHGlobal(size);
                _rgbNativeBufferSize = size;
            }
        }

        private void UpdateFps()
        {
            lock (_fpsLock)
            {
                _frameCount++;

                if (_frameStopwatch.ElapsedMilliseconds >= 1000)
                {
                    _currentFps = _frameCount * 1000.0 / _frameStopwatch.ElapsedMilliseconds;
                    _frameCount = 0;
                    _frameStopwatch.Restart();
                }
            }
        }

        private string GetCameraDisplayName(MVSDK.tSdkCameraDevInfo camera)
        {
            string productName = BytesToString(camera.acProductName, false);
            string friendlyName = BytesToString(camera.acFriendlyName, true);
            string serialNumber = BytesToString(camera.acSn, false);

            if (!string.IsNullOrEmpty(friendlyName))
                return $"{friendlyName} ({serialNumber})";
            else if (!string.IsNullOrEmpty(productName))
                return $"{productName} ({serialNumber})";
            else
                return $"Camera ({serialNumber})";
        }

        private string BytesToString(byte[] bytes, bool useGbkForChinese = true)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            // 鎵惧埌绗竴涓?瀛楄妭鐨勪綅缃?
            int length = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0)
                {
                    length = i;
                    break;
                }
            }

            if (length == 0)
                length = bytes.Length;

            byte[] validBytes = new byte[length];
            Array.Copy(bytes, 0, validBytes, 0, length);

            // 灏濊瘯GBK缂栫爜
            if (useGbkForChinese && _gbkEncoding != null)
            {
                try
                {
                    return _gbkEncoding.GetString(validBytes);
                }
                catch { }
            }

            // 灏濊瘯UTF-8
            try
            {
                return Encoding.UTF8.GetString(validBytes);
            }
            catch { }

            // 浣跨敤榛樿缂栫爜
            return Encoding.Default.GetString(validBytes);
        }

        private bool IsEmptySerial(MVSDK.tSdkCameraDevInfo info)
        {
            try
            {
                return info.acSn == null || info.acSn.Length == 0 || string.IsNullOrWhiteSpace(BytesToString(info.acSn, false));
            }
            catch
            {
                return true;
            }
        }

        private bool TryApplyExposureTimeCore(double requestedExposureUs, bool persistToCamera)
        {
            var status = MVSDK.MvApi.CameraSetExposureTime(_cameraHandle, requestedExposureUs);
            if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
            {
                _logger.LogWarning("设置曝光时间失败：{Status}", status);
                return false;
            }

            // CMOS exposure is often quantized by sensor rows; readback is the actual applied value.
            double actualExposureUs = 0;
            var getStatus = MVSDK.MvApi.CameraGetExposureTime(_cameraHandle, ref actualExposureUs);
            if (getStatus == MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS && actualExposureUs > 0)
            {
                _exposureTimeUs = actualExposureUs;
                _logger.LogInformation("请求曝光时间 {Requested} 微秒，设备实际生效 {Actual} 微秒", requestedExposureUs, actualExposureUs);
            }
            else
            {
                _logger.LogInformation("曝光时间已设置为 {Val} 微秒", requestedExposureUs);
            }

            if (persistToCamera)
            {
                try
                {
                    var saveStatus = MVSDK.MvApi.CameraSaveParameter(_cameraHandle, (int)MVSDK.emSdkParameterTeam.PARAMETER_TEAM_A);
                    if (saveStatus != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        _logger.LogWarning("保存相机参数组 A 失败：{Status}", saveStatus);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "保存相机参数组 A 时发生异常");
                }
            }

            return true;
        }

        private void EnsureManualExposureMode()
        {
            if (MVSDK.MvApi.CameraGetAeState == null || MVSDK.MvApi.CameraSetAeState == null)
            {
                return;
            }

            uint aeState = 0;
            var getStatus = MVSDK.MvApi.CameraGetAeState(_cameraHandle, ref aeState);
            if (getStatus == MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS && aeState != 0)
            {
                var setStatus = MVSDK.MvApi.CameraSetAeState(_cameraHandle, 0);
                if (setStatus == MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    _logger.LogInformation("已切换到手动曝光模式（自动曝光关闭）");
                }
                else
                {
                    _logger.LogWarning("关闭自动曝光失败：{Status}", setStatus);
                }
            }
        }
        #endregion
    }
}

