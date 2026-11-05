// 文件职责：提供 OptiTrack 模块的一站式依赖注入扩展。

using Microsoft.Extensions.DependencyInjection;

namespace VisionShell.Features.OptiTrack;

// 类型职责：扩展 IServiceCollection 以注册 OptiTrack 模块。
public static class OptiTrackExtensions
{
    public static IServiceCollection AddOptiTrack(this IServiceCollection services)
    {
        services.AddTransient<Views.OptiTrackMainView>();
        services.AddTransient<ViewModels.OptiTrackMainViewModel>();
        // Register region factory provider for App/PluginLoader to pick up and register into RegionManager
        services.AddSingleton<VisionShell.Core.Interfaces.IRegionFactoryRegistration, RegionFactories.OptiTrackRegionFactoryRegistration>();
        services.AddSingleton<VisionShell.Core.Interfaces.IFeatureDeviceRequirements, OptiTrackDeviceRequirements>();
        return services;
    }
}
