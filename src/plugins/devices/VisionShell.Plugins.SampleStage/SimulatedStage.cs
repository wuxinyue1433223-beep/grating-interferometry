// 文件职责：示例位移台实现，模拟相对/绝对移动与回零。

using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.SampleStage;

// 类型职责：示例位移台实现，模拟位置变化与运动命令。
public class SimulatedStage : IStage
{
    private readonly ILogger _logger;
    private double _position;
    private double _currentSpeedMmPerSec;
    private const double DefaultMaxSpeedMmPerSec = 0.5;
    private double _maxSpeedMmPerSec = DefaultMaxSpeedMmPerSec;

    public SimulatedStage(ILogger logger)
    {
        _logger = logger;
        DeviceId = Guid.NewGuid().ToString();
        Name = "Simulated Axis X";
        State = DeviceState.Disconnected;
    }

    public string DeviceId { get; }
    public string Name { get; }
    public bool IsCloneable => true;
    public DeviceState State { get; private set; }
    public bool IsEnabled { get; private set; }
    public double Position => _position;
    public double CurrentSpeedMmPerSec => _currentSpeedMmPerSec;

    public double MaxSpeedMmPerSec
    {
        get => _maxSpeedMmPerSec;
        set => _maxSpeedMmPerSec = value <= 0 ? DefaultMaxSpeedMmPerSec : value;
    }

    public event EventHandler<DeviceState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CancellationToken token = default)
    {
        _logger.LogInformation("Connecting Stage...");
        await Task.Delay(200, token);
        State = DeviceState.Connected;
        StateChanged?.Invoke(this, State);
        _logger.LogInformation("Stage Connected.");
    }

    public Task DisconnectAsync()
    {
        State = DeviceState.Disconnected;
        IsEnabled = false;
        StateChanged?.Invoke(this, State);
        _logger.LogInformation("Stage Disconnected.");
        return Task.CompletedTask;
    }

    public void Dispose() { }

    public Task EnableAsync(bool state)
    {
        IsEnabled = state;
        _logger.LogInformation("Stage Enabled: {State}", state);
        return Task.CompletedTask;
    }

    public async Task HomeAsync()
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Cannot Home: Stage is disabled.");
            return;
        }

        _logger.LogInformation("Homing...");
        State = DeviceState.Busy;
        StateChanged?.Invoke(this, State);

        await Task.Delay(1000); // Simulate movement
        _position = 0.0;
        
        State = DeviceState.Connected;
        StateChanged?.Invoke(this, State);
        _logger.LogInformation("Homing Complete. Pos: 0.0");
    }

    public async Task MoveAsync(double value, DefaultMoveMode mode = DefaultMoveMode.Absolute)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Cannot Move: Stage is disabled.");
            return;
        }

        var start = _position;
        var target = mode == DefaultMoveMode.Absolute ? value : _position + value;
        _logger.LogInformation("Moving to {Target}...", target);

        State = DeviceState.Busy;
        StateChanged?.Invoke(this, State);

        // Simulate travel time with max speed
        var dist = Math.Abs(target - _position);
        var seconds = dist / _maxSpeedMmPerSec;
        if (seconds < 0.01) seconds = 0.01;

        var stepIntervalMs = 50;
        var steps = Math.Max(1, (int)Math.Ceiling(seconds * 1000.0 / stepIntervalMs));
        _currentSpeedMmPerSec = steps > 0 ? Math.Abs(target - start) / (steps * stepIntervalMs / 1000.0) : 0.0;

        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(stepIntervalMs);
            _position = start + ((target - start) * i / steps);
        }

        _position = target;
        _currentSpeedMmPerSec = 0.0;

        State = DeviceState.Connected;
        StateChanged?.Invoke(this, State);
        _logger.LogInformation("Move Complete. Pos: {Pos}", _position);
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Stage Stopped.");
        State = DeviceState.Connected;
        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    public Task<double> GetPositionAsync()
    {
        return Task.FromResult(_position);
    }
}
