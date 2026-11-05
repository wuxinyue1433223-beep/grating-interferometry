// 文件职责：日志视图行为：新日志到达时自动滚动。

using System.Collections.Specialized;
using System.Windows.Controls;
using VisionShell.App.Services;

namespace VisionShell.App.Views;

// 类型职责：日志控件代码后置类。
public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is UiLogService log)
        {
            log.Entries.CollectionChanged += OnLogCollectionChanged;
            ScrollToEnd();
        }
    }

    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (LogScroll != null)
        {
            LogScroll.ScrollToEnd();
        }
    }
}
