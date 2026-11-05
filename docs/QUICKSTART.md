快速上手（简明）

构建与运行

- 先确保已安装 .NET SDK（建议 8+）。
- 在仓库根目录运行：

```powershell
dotnet build
dotnet run --project src\VisionShell.App\VisionShell.App.csproj
```

首次运行

- 程序会扫描 `plugins` 目录并加载 Sample 插件（若存在）。
- App 启动时会向 DeviceManager 注册两个示例相机（AngleCamera、SpotCamera），以便于演示和 UI 调试。

常见操作

- 在主界面点击 “一键连接” 会：
  1. 读取 `DeviceMappingStore` 中的映射（位于用户 AppData 的 device-mappings.json）。
  2. 若无映射则尝试自动生成默认映射并保存。
  3. 根据映射创建并 Connect 设备。

- 若需要添加真实设备插件：将插件 DLL 放入运行时的 `plugins/` 子目录，然后重启程序或调用重新扫描。

调试建议

- 使用 Sample 插件先验证 UI 与控制流程：SampleCamera、SampleStage。
- 若出现 `device is null` 之类日志，先点击 “一键连接” 并检查日志窗口是否有连接成功信息。
- 如果需要手动映射，请编辑 `%APPDATA%\VisionShell\device-mappings.json` 或在未来的映射编辑界面中配置。

联系方式

- 在 docs/ 里查看 ARCHITECTURE.md 与 PLUGINS.md 获取更详细的实现与扩展说明。