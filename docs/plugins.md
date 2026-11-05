插件与设备集成说明（中文）

概述

VisionShell 的设备驱动以插件形式提供，运行时从 `plugins/` 目录加载。每个插件负责发现并创建具体 `IDevice`（例如 `ICamera`、`IStage`）实例，主程序通过 `IDeviceFactory` 与通用接口交互。

目录结构（运行时）

插件输出应放在运行目录的 `plugins/` 下，例如：

```
bin/Debug/app/plugins/
  └── my-camera-plugin/
      ├── plugin.json
      ├── VisionShell.Plugins.MyCamera.dll
      └── (相关依赖)
```

插件清单（plugin.json）示例

```json
{
  "id": "sample-camera",
  "name": "Sample Camera Plugin",
  "version": "1.0.0",
  "type": "camera",
  "entryType": "VisionShell.Plugins.SampleCamera.SampleCameraPlugin",
  "dependencies": []
}
```

实现契约（要点）

- 入口：实现 `IDevicePlugin`，用于插件注册与初始化（可选）。
- 工厂：实现 `IDeviceFactory`，负责发现（`DiscoverAsync`）与创建设备（`CreateDevice`）。
- 设备：实现对应的设备接口，例如 `ICamera`、`IStage`，并遵循 `IDevice` 的生命周期（Connect/Disconnect/Dispose）。
- 日志：通过 `ILogger` 打点，插件内部捕获异常并记录，避免未处理异常终止主进程。

简短示例（插件工厂与设备骨架）

```csharp
// SampleCameraFactory.cs
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

public class SampleCameraFactory : IDeviceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    public SampleCameraFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DeviceType => "simulated-camera";
    public string DisplayName => "Simulated Camera";
    public bool IsCloneable => true;

    public Task<bool> DiscoverAsync(CancellationToken token = default)
        => Task.FromResult(true);

    public IDevice CreateDevice(ILogger logger)
    {
        // 返回一个实现了 ICamera 的模拟设备
        return new SimulatedCamera(_loggerFactory.CreateLogger("SimCam"), "Simulated Camera", "SimCam-001");
    }
}

// SimulatedCamera.cs（简化骨架）
public class SimulatedCamera : ICamera
{
    private readonly ILogger _logger;
    public string DeviceId { get; }
    public string Name { get; }
    public DeviceState State { get; private set; }

    public SimulatedCamera(ILogger logger, string name, string id)
    {
        _logger = logger; Name = name; DeviceId = id; State = DeviceState.Disconnected;
    }

    public Task ConnectAsync() { State = DeviceState.Connected; return Task.CompletedTask; }
    public Task DisconnectAsync() { State = DeviceState.Disconnected; return Task.CompletedTask; }
    public Task StartCaptureAsync() { /* 触发 FrameReceived 事件 */ return Task.CompletedTask; }
    public Task StopCaptureAsync() { return Task.CompletedTask; }
    public Task SnapAsync() { return Task.CompletedTask; }
    public void Dispose() { }

    // 注意：按项目接口补充事件与帧处理逻辑（示例省略）
}
```

部署与调试说明

- 将编译出的插件 DLL 与 `plugin.json` 放入 `plugins/<your-plugin>/` 下。
- 启动主程序，程序会执行 `DeviceManager.ScanPluginsAsync()` 并加载工厂。
- 使用 Sample 插件可以快速验证 UI 与控制流程。

日志与异常策略

- 插件内部应捕获异常并通过 `ILogger` 记录，避免向上抛出不可控异常。
- 使用 `BeginScope` 为设备操作加上下文（例如 `DeviceId`），便于定位问题。

调试提示

- 开发时将插件项目加入解决方案并将构建输出复制到主程序的 `plugins/` 文件夹，或使用构建后脚本自动复制。
- 若设备未被映射或出现 `device is null`，先检查 `DeviceMappingStore`（位于 `%APPDATA%/VisionShell/device-mappings.json`），并使用“一键连接”测试自动映射行为。

