项目概览

这是 VisionShell 仪器控制与视觉平台的代码库，采用模块化（feature + plugin）设计，便于扩展模拟与物理设备。

主要目录（简明）

- src/
  - VisionShell.Core/        核心接口与模型（IDevice、IStage、ICamera、映射等）
  - VisionShell.App/         应用启动、DI、UI shell、服务实现（DeviceManager、MappingStore）
  - features/               各功能模块（例如 VisionShell.Features.OptiTrack）
  - plugins/                可加载设备插件（SampleCamera、SampleStage 等）
  - docs/                   项目文档（本目录）

核心思路

- 明确分层：核心接口（Core）定义抽象契约；App 提供宿主与服务；Features 实现业务视图与流程；Plugins 提供设备适配器。
- 通过依赖注入与区域/工厂注册实现视图与服务解耦，支持运行时插件发现与设备实例化。
- 设备映射（DeviceMappingStore）保存“功能角色 -> 设备描述符/ID”的关系，支持自动加载与自动生成默认映射（便于首次运行）。

目标受众

- 开发者：扩展新设备 plugin、实现新 feature。
- 仿真测试：使用 sample 插件快速验证 UI 与控制流程。