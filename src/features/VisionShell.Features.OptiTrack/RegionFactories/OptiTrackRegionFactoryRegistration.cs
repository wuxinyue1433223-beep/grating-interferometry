// 文件职责：把 OptiTrack 主视图注册到主程序区域管理器。

using System;
using Microsoft.Extensions.DependencyInjection;
using VisionShell.Core.Interfaces;

namespace VisionShell.Features.OptiTrack.RegionFactories;

// 类型职责：向主区域注册 OptiTrack 页面工厂。
public class OptiTrackRegionFactoryRegistration : VisionShell.Core.Interfaces.IRegionFactoryRegistration
{
    public (string RegionKey, Func<object> Factory) CreateFactory(IServiceProvider services)
    {
        return ("Main", () =>
        {
            var sp = services;
            var view = sp.GetRequiredService<Views.OptiTrackMainView>();
            var vm = sp.GetService<ViewModels.OptiTrackMainViewModel>();
            if (view is System.Windows.FrameworkElement fe && vm != null)
            {
                fe.DataContext = vm;
            }
            return view as object;
        });
    }
}
