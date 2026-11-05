// 文件职责：位移台服务门面：按类型聚合并提供位移台访问。

using VisionShell.Core.Interfaces;

namespace VisionShell.App.Services;

// 类型职责：位移台查询服务实现。
public class StageService : IStageService
{
    private readonly IDeviceManager _deviceManager;

    public StageService(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
    }

    public IEnumerable<IStage> GetAllStages()
    {
        return _deviceManager.GetDevices<IStage>();
    }

    public IStage? GetStage(string stageId)
    {
        return GetAllStages().FirstOrDefault(s => s.DeviceId == stageId);
    }
}
