// 文件职责：应用设置服务接口与默认实现。

namespace VisionShell.App.Services;

// 类型职责：设置读取抽象。
public interface ISettingsService
{
    T? GetSection<T>(string sectionName);
}

// 类型职责：设置服务默认实现。
public class SettingsService : ISettingsService
{
    // Minimal mock implementation
    public T? GetSection<T>(string sectionName)
    {
        return default;
    }
}
