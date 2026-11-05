// 文件职责：示例相机设备，生成测试画面并模拟曝光行为。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.SampleCamera;

// 类型职责：示例相机实现，输出模拟图像帧。
public class SimulatedCamera : ICamera
{
    private readonly ILogger _logger;
    private bool _capturing;
    private CancellationTokenSource? _captureCts;

    public SimulatedCamera(ILogger logger, string? name = null, string? id = null)
    {
        _logger = logger;
        DeviceId = id ?? Guid.NewGuid().ToString();
        Name = name ?? "Simulated Cam 01";
        State = DeviceState.Disconnected;
    }
    public string DeviceId { get; }
    public string Name { get; }
    public bool IsCloneable => true;
    public DeviceState State { get; private set; }

    public event EventHandler<DeviceState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<ImageFrame>? FrameReceived;

    public async Task ConnectAsync(CancellationToken token = default)
    {
        try
        {
            _logger.LogInformation("Connecting to Simulated Camera...");
            await Task.Delay(500, token); // fake delay
            State = DeviceState.Connected;
            StateChanged?.Invoke(this, State);
            _logger.LogInformation("Connected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection failed.");
            ErrorOccurred?.Invoke(this, ex.Message);
            State = DeviceState.Error;
            StateChanged?.Invoke(this, State);
        }
    }

    public async Task DisconnectAsync()
    {
        await StopCaptureAsync();
        State = DeviceState.Disconnected;
        StateChanged?.Invoke(this, State);
        _logger.LogInformation("Disconnected.");
    }

    public Task StartCaptureAsync()
    {
        if (State != DeviceState.Connected)
        {
             _logger.LogWarning("Cannot start capture: Not connected.");
             return Task.CompletedTask;
        }

        if (_capturing) return Task.CompletedTask;

        _capturing = true;
        _captureCts = new CancellationTokenSource();
        
        // Start background loop
        _ = Task.Run(async () => await CaptureLoop(_captureCts.Token));
        
        _logger.LogInformation("Capture started.");
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        _capturing = false;
        _captureCts?.Cancel();
        _logger.LogInformation("Capture stopped.");
        return Task.CompletedTask;
    }

    public Task SnapAsync()
    {
        _logger.LogInformation("Snapshot triggering (simulated).");
        // Emit one fake frame
        EmitFakeFrame();
        return Task.CompletedTask;
    }

    public double ExposureTimeUs { get; set; } = 5000.0;

    public void Dispose()
    {
        StopCaptureAsync();
    }

    private async Task CaptureLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _capturing)
        {
            EmitFakeFrame();
            try { await Task.Delay(33, token); } catch { /* ignore cancel */ }
        }
    }

    private void EmitFakeFrame()
    {
        int w = 640;
        int h = 480;
        var data = new byte[w * h];
        
        // Generate simple pattern based on time
        var offset = (byte)(DateTime.Now.Millisecond / 10);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i % 255) + offset);
        }

        var frame = new ImageFrame 
        { 
            Width = w, 
            Height = h, 
            Timestamp = DateTime.UtcNow,
            Data = data
        };
        FrameReceived?.Invoke(this, frame);
    }
}
