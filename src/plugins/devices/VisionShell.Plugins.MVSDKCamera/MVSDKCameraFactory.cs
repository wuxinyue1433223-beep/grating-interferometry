// 文件职责：创建并发现 MVSDK 相机实例。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;
using System.Text;

namespace VisionShell.Plugins.MVSDKCamera
{
    // 类型职责：MVSDK 工厂，负责发现并创建相机实例。
    public class MVSDKCameraFactory : IDeviceFactory, IDeviceDescriptorProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly List<IDeviceDescriptor> _descriptors = new();
        private readonly object _sync = new();
        private MVSDK.tSdkCameraDevInfo[]? _cameraList;
        private bool _hasRealCamera;
        private int _nextCameraIndex;

        private record Descriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

        public MVSDKCameraFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public string DeviceType => "mvsdk-camera";
        public string DisplayName => _hasRealCamera ? "MVSDK Industrial Camera" : "MVSDK Camera (Simulated)";
        public bool IsCloneable => !_hasRealCamera;

        public IEnumerable<IDeviceDescriptor> GetDescriptors()
        {
            lock (_sync)
            {
                return _descriptors.ToList();
            }
        }

        public Task<bool> DiscoverAsync(CancellationToken token = default)
        {
            try
            {
                _hasRealCamera = EnumerateCameras();
                RefreshDescriptors();
                return Task.FromResult(_hasRealCamera);
            }
            catch
            {
                _hasRealCamera = false;
                lock (_sync)
                {
                    _cameraList = Array.Empty<MVSDK.tSdkCameraDevInfo>();
                }
                RefreshDescriptors();
                return Task.FromResult(false);
            }
        }

        public IDevice CreateDevice(ILogger logger)
        {
            // Attempt to ensure we have an enumerated list; fall back to simulated when none.
            if (_cameraList == null || _cameraList.Length == 0)
            {
                _hasRealCamera = EnumerateCameras();
                RefreshDescriptors();
            }

            MVSDK.tSdkCameraDevInfo[] cameraSnapshot;
            int selectedIndex;
            lock (_sync)
            {
                cameraSnapshot = _cameraList ?? Array.Empty<MVSDK.tSdkCameraDevInfo>();
                selectedIndex = _nextCameraIndex;
                if (cameraSnapshot.Length > 0)
                {
                    _nextCameraIndex = (_nextCameraIndex + 1) % cameraSnapshot.Length;
                }
            }

            if (_hasRealCamera && cameraSnapshot.Length > 0)
            {
                var cameraInfo = cameraSnapshot[selectedIndex % cameraSnapshot.Length];
                return new MVSDKCamera(logger, cameraInfo, GetCameraDisplayName(cameraInfo), cameraSnapshot);
            }

            #if DEBUG
            return new SimulatedMVSDKCamera(logger, "MVSDK Camera (Simulated)");
            #else
            throw new InvalidOperationException("No physical MVSDK camera is available.");
            #endif
        }

        // ????????????????????????
        public ICamera CreateCamera(ILogger logger, MVSDK.tSdkCameraDevInfo cameraInfo)
        {
            return new MVSDKCamera(logger, cameraInfo, null);
        }

        private bool EnumerateCameras()
        {
            try
            {
                var status = MVSDK.MvApi.CameraSdkInit(1);
                if (status != MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    return false;
                }

                status = MVSDK.MvApi.CameraEnumerateDevice(out _cameraList);
                lock (_sync)
                {
                    _cameraList = _cameraList ?? Array.Empty<MVSDK.tSdkCameraDevInfo>();
                    if (_cameraList.Length == 0)
                    {
                        _nextCameraIndex = 0;
                    }
                    else if (_nextCameraIndex >= _cameraList.Length)
                    {
                        _nextCameraIndex = 0;
                    }
                }

                return status == MVSDK.CameraSdkStatus.CAMERA_STATUS_SUCCESS && _cameraList != null && _cameraList.Length > 0;
            }
            catch
            {
                lock (_sync)
                {
                    _cameraList = Array.Empty<MVSDK.tSdkCameraDevInfo>();
                    _nextCameraIndex = 0;
                }
                return false;
            }
        }

        private void RefreshDescriptors()
        {
            lock (_sync)
            {
                _descriptors.Clear();

                if (_hasRealCamera && _cameraList != null && _cameraList.Length > 0)
                {
                    foreach (var camera in _cameraList)
                    {
                        var serial = GetCameraSerial(camera);
                        var descriptorId = string.IsNullOrWhiteSpace(serial) ? Guid.NewGuid().ToString("N") : serial;
                        _descriptors.Add(new Descriptor(
                            descriptorId,
                            GetCameraDisplayName(camera),
                            "ICamera",
                            DeviceType,
                            false));
                    }

                    return;
                }

                _descriptors.Add(new Descriptor(
                    "mvsdk-simulated",
                    "MVSDK Camera (Simulated)",
                    "ICamera",
                    DeviceType,
                    true));
            }
        }

        private static string GetCameraSerial(MVSDK.tSdkCameraDevInfo cameraInfo)
        {
            return BytesToString(cameraInfo.acSn);
        }

        private static string GetCameraDisplayName(MVSDK.tSdkCameraDevInfo cameraInfo)
        {
            var serial = GetCameraSerial(cameraInfo);
            if (!string.IsNullOrWhiteSpace(serial)) return $"MVSDK Camera ({serial})";

            var friendly = BytesToString(cameraInfo.acFriendlyName);
            if (!string.IsNullOrWhiteSpace(friendly)) return friendly;

            var product = BytesToString(cameraInfo.acProductName);
            if (!string.IsNullOrWhiteSpace(product)) return product;

            return "MVSDK Industrial Camera";
        }

        private static string BytesToString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            var end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = bytes.Length;

            var valid = new byte[end];
            Array.Copy(bytes, valid, end);

            var candidates = new List<string>
            {
                TryDecode(valid, Encoding.UTF8),
                TryDecode(valid, Encoding.Default)
            };

            try
            {
                candidates.Add(TryDecode(valid, Encoding.GetEncoding("GBK")));
            }
            catch
            {
                // ignore
            }

            return candidates
                .Select(s => s?.Trim() ?? string.Empty)
                .OrderByDescending(ScoreDecodedText)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string TryDecode(byte[] bytes, Encoding encoding)
        {
            try { return encoding.GetString(bytes); }
            catch { return string.Empty; }
        }

        private static int ScoreDecodedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return int.MinValue;

            var score = 0;
            foreach (var c in text)
            {
                if (c == '\uFFFD') { score -= 8; continue; }
                if (c == '?') { score -= 3; continue; }
                if (char.IsControl(c) && !char.IsWhiteSpace(c)) { score -= 10; continue; }
                score += c <= 0x7F ? 1 : 2;
            }

            return score;
        }
    }
}
