// 文件职责：UI 日志缓冲与转发服务，把后台日志推送到界面。

using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;
using Serilog.Core;
using Serilog.Events;

namespace VisionShell.App.Services;

// 类型职责：UI 日志容器，维护可绑定的日志集合。
public class UiLogService
{
    public ObservableCollection<string> Entries { get; } = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    // prevent unbounded growth
    public int MaxEntries { get; set; } = 500;

    public void Add(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => SafeAdd(message), DispatcherPriority.Background);
        }
        else
        {
            SafeAdd(message);
        }
    }

    private void SafeAdd(string message)
    {
        Entries.Add(message);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}

// 类型职责：Serilog sink 适配器，把日志事件投递到 UiLogService。
public class UiLogSink : ILogEventSink
{
    private readonly UiLogService _uiLog;
    private readonly IFormatProvider? _formatProvider;

    public UiLogSink(UiLogService uiLog, IFormatProvider? formatProvider = null)
    {
        _uiLog = uiLog;
        _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
        var level = MapLevel(logEvent.Level);
        if (level < _uiLog.MinimumLevel) return;

        var message = logEvent.RenderMessage(_formatProvider);
        var time = logEvent.Timestamp.ToString("HH:mm:ss");
        var prefix = $"[{time} {level.ToString()[..3].ToUpper()}]";
        
        if (logEvent.Exception != null)
        {
            _uiLog.Add($"{prefix} {message} | {logEvent.Exception.Message}");
        }
        else
        {
            _uiLog.Add($"{prefix} {message}");
        }
    }

    private LogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.Information
    };
}
