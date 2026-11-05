using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsLegacyKinesisStage;

public sealed class ThorlabsLegacyKinesisStage : IStage
{
    private const double DefaultMaxVelocityMmPerSec = 0.5;
    private const double DefaultMaxAccelerationMmPerSec2 = 0.5;
    private const int ConnectRetryCount = 3;
    private const int ConnectRetryDelayMs = 500;
    private static readonly object SharedSdkLock = new();
    private static KinesisLegacySdk? SharedSdk;
    private static DateTime SharedSdkLastBuildUtc = DateTime.MinValue;
    private static int SharedConnectedDeviceCount;

    private readonly ILogger _logger;
    private readonly TimeSpan _settingsInitTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private KinesisLegacySdk? _sdk;
    private KinesisLegacyConnection? _connection;
    private bool _sharedConnectionRegistered;
    private bool _disposed;

    public string DeviceId { get; private set; }
    public string Name { get; private set; }
    public DeviceState State { get; private set; }
    public bool IsEnabled { get; private set; }
    public double Position { get; private set; }

    public event EventHandler<DeviceState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public ThorlabsLegacyKinesisStage(ILogger logger, string? deviceId = null, string? name = null)
    {
        _logger = logger;
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId;
        Name = string.IsNullOrWhiteSpace(name) ? "Thorlabs Kinesis Legacy Stage" : name;
        State = DeviceState.Disconnected;
    }

    public void SetOpenDeviceId(string deviceId)
    {
        if (State != DeviceState.Disconnected)
            throw new InvalidOperationException("Cannot change device id while connected");

        DeviceId = deviceId ?? string.Empty;
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        await _ioGate.WaitAsync(token);
        try
        {
            var sdk = GetOrCreateSharedSdk();
            if (sdk == null)
            {
                throw new InvalidOperationException("Legacy Kinesis SDK not ready.");
            }

            _sdk = sdk;
            EnsureSharedDeviceListBuilt(_sdk);

            var target = ResolveTarget(_sdk, DeviceId);
            KinesisLegacyConnection? conn = null;
            for (var attempt = 1; attempt <= ConnectRetryCount; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    conn = _sdk.Connect(target, _settingsInitTimeout);
                    break;
                }
                catch (Exception ex) when (attempt < ConnectRetryCount && IsTransientConnectionFailure(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Kinesis 设备 {DeviceId} 第 {Attempt}/{MaxAttempt} 次连接未就绪，清理后等待 {DelayMs} 毫秒重试",
                        DeviceId,
                        attempt,
                        ConnectRetryCount,
                        ConnectRetryDelayMs);
                    await Task.Delay(ConnectRetryDelayMs, token);
                    if (!TryRebuildSharedDeviceList(_sdk))
                    {
                        _logger.LogDebug("已有 Kinesis 设备保持连接，跳过全局设备列表重建，仅重试目标设备");
                    }
                }
            }

            if (conn == null)
            {
                throw new InvalidOperationException($"连续 {ConnectRetryCount} 次尝试后仍无法连接 Kinesis 设备 {DeviceId}");
            }

            _connection = conn;
            RegisterSharedConnection();

            // Best-effort safety limits: do not fail connection if model/API doesn't expose these params.
            try
            {
                conn.TryApplyMotionLimits(DefaultMaxVelocityMmPerSec, DefaultMaxAccelerationMmPerSec2, _logger);
                conn.TryNormalizeActuatorNegativeRange(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "向设备 {DeviceId} 应用默认运动限制失败", DeviceId);
            }

            DeviceId = KinesisLegacyIds.Format(target);
            Name = $"{conn.DeviceDisplayName} ({DeviceId})";
            IsEnabled = true;
            Position = conn.GetPosition();
            _logger.LogInformation("Kinesis 位移设备连接时的运动参数快照：{Snapshot}", conn.BuildMotionLimitSnapshot());

            SetState(DeviceState.Connected);
            _logger.LogInformation("Kinesis 位移设备连接成功：{Name}", Name);
        }
        catch (Exception ex)
        {
            try { _connection?.Dispose(); } catch { }
            _connection = null;
            UnregisterSharedConnection();
            SetState(DeviceState.Disconnected);
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private static bool IsTransientConnectionFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException!)
        {
            if (current.GetType().Name.Contains("DeviceNotReady", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("not ready", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current.InnerException == null)
            {
                break;
            }
        }

        return false;
    }

    public async Task DisconnectAsync()
    {
        await _ioGate.WaitAsync();
        try
        {
            if (_connection != null)
            {
                try { _connection.Stop(); }
                catch (Exception ex) { _logger.LogDebug(ex, "断开连接时立即停止 Kinesis 设备失败"); }
            }
            _connection?.Dispose();
            _connection = null;
            UnregisterSharedConnection();
            IsEnabled = false;
            SetState(DeviceState.Disconnected);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_connection != null || State != DeviceState.Disconnected)
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放 Kinesis 位移设备时断开连接失败");
        }
        finally
        {
            _disposed = true;
        }
    }

    public Task EnableAsync(bool state)
    {
        ThrowIfDisposed();
        EnsureConnected();

        if (state)
        {
            _connection!.Enable();
        }

        IsEnabled = state;
        return Task.CompletedTask;
    }

    public async Task HomeAsync()
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _ioGate.WaitAsync();
        try
        {
            SetState(DeviceState.Busy);
            await Task.Run(() => _connection!.Home());
            Position = await Task.Run(() => _connection!.GetPosition());
            SetState(DeviceState.Connected);
        }
        catch (Exception ex)
        {
            SetState(DeviceState.Connected);
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task MoveAsync(double value, DefaultMoveMode mode = DefaultMoveMode.Absolute)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _ioGate.WaitAsync();
        try
        {
            SetState(DeviceState.Busy);

            var beforePos = _connection!.GetPosition();
            var hasBounds = _connection.TryGetTravelBounds(out var minPos, out var maxPos);

            if (mode == DefaultMoveMode.Absolute)
            {
                var requested = (decimal)value;

                if (hasBounds && ((double)requested < minPos || (double)requested > maxPos))
                {
                    _logger.LogWarning(
                        "拒绝超出范围的绝对移动：请求={Request:F6} mm，范围=[{Min:F6}, {Max:F6}] mm，参数快照={Snapshot}",
                        (double)requested,
                        minPos,
                        maxPos,
                        _connection.BuildMotionLimitSnapshot());
                    throw new InvalidOperationException($"绝对目标 {(double)requested:F6} mm 超出范围 [{minPos:F6}, {maxPos:F6}] mm。");
                }

                await Task.Run(() => _connection!.MoveTo((decimal)value));

                var afterAbs = await Task.Run(() => _connection.GetPosition());
                Position = afterAbs;
                var absErr = Math.Abs(afterAbs - (double)requested);

                _logger.LogInformation(
                    "Kinesis 绝对移动：请求={Request:F6} mm，移动前={Before:F6} mm，移动后={After:F6} mm，绝对误差={Err:F6} mm",
                    (double)requested,
                    beforePos,
                    afterAbs,
                    absErr);

                if ((double)requested < 0 && absErr > 0.05)
                {
                    _logger.LogWarning(
                        "负向绝对移动可能受到设备限制：请求={Request:F6} mm，移动后={After:F6} mm，参数快照={Snapshot}",
                        (double)requested,
                        afterAbs,
                        _connection.BuildMotionLimitSnapshot());
                }
            }
            else
            {
                var delta = (decimal)value;
                var target = beforePos + (double)delta;

                if (hasBounds && (target < minPos || target > maxPos))
                {
                    _logger.LogWarning(
                        "拒绝超出范围的相对移动：增量={Delta:F6} mm，目标={Target:F6} mm，范围=[{Min:F6}, {Max:F6}] mm，参数快照={Snapshot}",
                        (double)delta,
                        target,
                        minPos,
                        maxPos,
                        _connection.BuildMotionLimitSnapshot());
                    throw new InvalidOperationException($"相对移动目标 {target:F6} mm 超出范围 [{minPos:F6}, {maxPos:F6}] mm。");
                }

                var moved = await Task.Run(() => _connection!.TryMoveRelative(delta, _logger));
                if (!moved)
                {
                    var current = (decimal)await Task.Run(() => _connection!.GetPosition());
                    await Task.Run(() => _connection!.MoveTo(current + delta));
                }

                var afterRel = await Task.Run(() => _connection!.GetPosition());
                Position = afterRel;
                _logger.LogInformation(
                    "Kinesis 相对移动：增量={Delta:F6} mm，移动前={Before:F6} mm，移动后={After:F6} mm",
                    (double)delta,
                    beforePos,
                    afterRel);
            }

            SetState(DeviceState.Connected);
        }
        catch (Exception ex)
        {
            SetState(DeviceState.Connected);
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task StopAsync()
    {
        ThrowIfDisposed();
        EnsureConnected();

        // Kinesis StopImmediate is designed to interrupt an in-flight motion command.
        // Do not queue it behind the per-axis motion gate.
        var connection = _connection!;
        try
        {
            await Task.Run(connection.Stop);
            SetState(DeviceState.Connected);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public Task<double> GetPositionAsync()
    {
        ThrowIfDisposed();
        EnsureConnected();

        // Kinesis Position is backed by StartPolling's SDK cache. Reading it must not wait
        // behind a synchronous Home/MoveTo operation, otherwise live position freezes in motion.
        Position = _connection!.GetPosition();
        return Task.FromResult(Position);
    }

    private static KinesisLegacyTarget ResolveTarget(KinesisLegacySdk sdk, string descriptorId)
    {
        if (KinesisLegacyIds.TryParse(descriptorId, out var parsed))
        {
            return parsed;
        }

        var targets = sdk.DiscoverTargets();
        if (targets.Count == 0)
        {
            _ = TryRebuildSharedDeviceList(sdk);
            targets = sdk.DiscoverTargets();
        }
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("未检测到受支持的 Kinesis 控制器。");
        }

        return targets[0];
    }

    private KinesisLegacySdk? GetOrCreateSharedSdk()
    {
        lock (SharedSdkLock)
        {
            if (SharedSdk != null)
            {
                return SharedSdk;
            }

            var sdk = KinesisLegacySdk.TryCreate(_logger, out var sdkError);
            if (sdk == null)
            {
                _logger.LogWarning("Kinesis SDK 尚未就绪：{Error}", sdkError);
                return null;
            }

            SharedSdk = sdk;
            return SharedSdk;
        }
    }

    private static void EnsureSharedDeviceListBuilt(KinesisLegacySdk sdk)
    {
        lock (SharedSdkLock)
        {
            if (SharedSdkLastBuildUtc != DateTime.MinValue || SharedConnectedDeviceCount > 0)
            {
                return;
            }

            sdk.BuildDeviceList();
            SharedSdkLastBuildUtc = DateTime.UtcNow;
        }
    }

    private static bool TryRebuildSharedDeviceList(KinesisLegacySdk sdk)
    {
        lock (SharedSdkLock)
        {
            if (SharedConnectedDeviceCount > 0)
            {
                return false;
            }

            sdk.BuildDeviceList();
            SharedSdkLastBuildUtc = DateTime.UtcNow;
            return true;
        }
    }

    private void RegisterSharedConnection()
    {
        lock (SharedSdkLock)
        {
            if (_sharedConnectionRegistered)
            {
                return;
            }

            SharedConnectedDeviceCount++;
            _sharedConnectionRegistered = true;
        }
    }

    private void UnregisterSharedConnection()
    {
        lock (SharedSdkLock)
        {
            if (!_sharedConnectionRegistered)
            {
                return;
            }

            SharedConnectedDeviceCount = Math.Max(0, SharedConnectedDeviceCount - 1);
            _sharedConnectionRegistered = false;
        }
    }

    private void EnsureConnected()
    {
        if (_connection == null)
            throw new InvalidOperationException("Legacy stage is not connected");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ThorlabsLegacyKinesisStage));
    }

    private void SetState(DeviceState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
