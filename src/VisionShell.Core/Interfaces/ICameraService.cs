// 文件职责：相机服务抽象：约束相机查询与获取方式。

namespace VisionShell.Core.Interfaces;

// 类型职责：相机服务接口。
public interface ICameraService
{
    IEnumerable<ICamera> GetAllCameras();
    ICamera? GetActiveCamera();
    void SetActiveCamera(string cameraId);
    Task StartActiveCameraAsync();
    Task StopActiveCameraAsync();
}
