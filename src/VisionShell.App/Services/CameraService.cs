// 文件职责：相机服务门面：从设备管理器获取相机集合与指定相机。

using System.Collections.Concurrent;
using VisionShell.Core.Interfaces;

namespace VisionShell.App.Services;

// 类型职责：相机查询服务实现。
public class CameraService : ICameraService
{
    private readonly IDeviceManager _deviceManager;
    private ICamera? _activeCamera;

    public CameraService(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
    }

    public IEnumerable<ICamera> GetAllCameras()
    {
        return _deviceManager.GetDevices<ICamera>();
    }

    public ICamera? GetActiveCamera()
    {
        return _activeCamera;
    }

    public void SetActiveCamera(string cameraId)
    {
        var cam = GetAllCameras().FirstOrDefault(c => c.DeviceId == cameraId);
        if (cam != null)
        {
            _activeCamera = cam;
        }
    }

    public async Task StartActiveCameraAsync()
    {
        if (_activeCamera != null)
        {
            await _activeCamera.StartCaptureAsync();
        }
    }

    public async Task StopActiveCameraAsync()
    {
        if (_activeCamera != null)
        {
            await _activeCamera.StopCaptureAsync();
        }
    }
}
