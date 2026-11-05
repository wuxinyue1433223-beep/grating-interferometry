架构与设计理念（简明）

设计原则

- 单一职责与接口优先：所有硬件能力通过接口暴露（`IDevice`、`ICamera`、`IStage`），业务逻辑只依赖接口。
- 插件式扩展：设备驱动以插件形式提供（`IDeviceFactory`），运行时扫描并注册工厂。
- 功能模块化（Feature）：每个功能（如 OptiTrack）包含自己的 View、ViewModel 与设备需求描述（`IFeatureDeviceRequirements`）。
- 映射隔离：设备角色映射由 `IDeviceMappingStore` 管理，保存到用户目录的 JSON，支持自动生成/修复。

关键组件

- VisionShell.Core
  - 接口：`IDevice`、`ICamera`、`IStage`、`IDeviceManager`、`IDeviceMappingStore`、`IFeatureDeviceRequirements`。
  - 模型：事件参数、图片帧（`ImageFrame`）等公共类型。

- VisionShell.App
  - `DeviceManager`：插件扫描、工厂管理、设备实例注册与生命周期（Connect/Stop/Shutdown）。
  - `DeviceMappingStore`：读取/写入映射配置，触发 MappingChanged 事件。
  - 启动流程：构建 DI 容器、扫描插件、注册示例设备（模拟相机/台阶）、显示 MainWindow。

- Features（示例：OptiTrack）
  - `OptiTrackMainViewModel`：处理连接、相机切换、移台控制、捕获帧并渲染到 UI。
  - 需求描述：`OptiTrackDeviceRequirements` 列出需要的角色（Actuator1、AngleCamera 等）。

运行时流程（简要）

1. 启动：App 初始化 DI，加载 plugins 目录，调用 `ScanPluginsAsync()`。
2. 注册：将可用的 `IDeviceFactory` 列入 DeviceManager 的工厂列表。
3. 映射加载：Feature 请求其角色映射（若无则可自动生成默认映射）。
4. 连接：Feature 调用 DeviceManager 创建/获取设备实例并 Connect。
5. 控制：UI 通过 ViewModel 调用设备接口（MoveAsync、SnapAsync、StartCaptureAsync 等）。

扩展建议

- 新设备：实现 `IDeviceFactory`，返回对应 `IDevice` 派生类并在插件中导出。
- 更精细映射 UI：可在 Feature 中提供“映射编辑”视图，供用户手动选择 Descriptor 与角色对应关系。