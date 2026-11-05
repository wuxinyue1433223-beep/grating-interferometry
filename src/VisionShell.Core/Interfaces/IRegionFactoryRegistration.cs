// 文件职责：区域工厂注册抽象：让功能模块向主壳注册视图工厂。

using System;

namespace VisionShell.Core.Interfaces;

/// <summary>
/// Feature or plugin provided registration that supplies a region key and a factory delegate to create the view.
/// Host will discover implementations and register their factories into the RegionManager.
/// </summary>
// 类型职责：区域工厂注册接口。
public interface IRegionFactoryRegistration
{
    (string RegionKey, Func<object> Factory) CreateFactory(IServiceProvider services);
}
