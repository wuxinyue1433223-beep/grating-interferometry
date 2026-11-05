// 文件职责：位移台服务抽象：统一获取位移台设备集合。

namespace VisionShell.Core.Interfaces;

// 类型职责：位移台服务接口。
public interface IStageService
{
    IEnumerable<IStage> GetAllStages();
    IStage? GetStage(string stageId);
}
