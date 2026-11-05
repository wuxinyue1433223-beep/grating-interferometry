using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsSC30Shutter;

public sealed class ThorlabsSC30Shutter : IShutter
{
    private const string DescriptorPrefix = "tl-sc30:";
    private const int TargetVendorId = 0x1313;
    private static readonly int[] TargetProductIds = [0x100C, 0x100E];

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    private IntPtr _deviceHandle;
    private bool _disposed;
    private bool _isOpen;
    private bool _enableChannelInverted;
    private static readonly int[] ChannelCandidates = [ThorlabsSC30Native.Channel1, ThorlabsSC30Native.NoChannel];
    private static readonly int[] ControllerChannel = [ThorlabsSC30Native.NoChannel];

    public string DeviceId { get; private set; }
    public string Name { get; private set; }
    public DeviceState State { get; private set; }
    public bool IsOpen => _isOpen;

    public event EventHandler<DeviceState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public ThorlabsSC30Shutter(ILogger logger, string? deviceId = null)
    {
        _logger = logger;
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId;
        Name = "Thorlabs SC30 Shutter";
        State = DeviceState.Disconnected;
    }

    public void SetOpenDeviceId(string deviceId)
    {
        if (State != DeviceState.Disconnected)
        {
            throw new InvalidOperationException("Cannot change device id while connected");
        }

        DeviceId = deviceId ?? string.Empty;
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        await _ioGate.WaitAsync(token);
        try
        {
            ThorlabsSC30ShutterFactory.EnsurePluginManagerOpened();

            var wantedSerial = ParseMappedSerial(DeviceId);
            if (!TryFindDevice(wantedSerial, out var selected))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(wantedSerial)
                    ? "No SC30 shutter devices found."
                    : $"SC30 shutter '{wantedSerial}' not found.");
            }

            if (!ThorlabsSC30Native.OpenDevice(selected.DeviceHandle))
            {
                var msg = ThorlabsSC30Native.GetLastErrorMessage(selected.DeviceHandle);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
                    ? $"Failed to open SC30 shutter '{selected.Serial}'."
                    : msg);
            }

            _deviceHandle = selected.DeviceHandle;
            DeviceId = $"{DescriptorPrefix}{selected.Serial}";
            Name = string.IsNullOrWhiteSpace(selected.Name)
                ? $"Thorlabs SC30 Shutter ({selected.Serial})"
                : $"{selected.Name} ({selected.Serial})";

            UnlockControllerAfterConnect();
            _isOpen = ReadOpenStateSafe();
            SetState(DeviceState.Connected);
            _logger.LogInformation("SC30 快门连接成功：{Name}", Name);
        }
        catch (Exception ex)
        {
            if (_deviceHandle != IntPtr.Zero)
            {
                try { ThorlabsSC30Native.CloseDevice(_deviceHandle); } catch { }
            }
            _deviceHandle = IntPtr.Zero;
            SetState(DeviceState.Disconnected);
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _ioGate.WaitAsync();
        try
        {
            if (_deviceHandle != IntPtr.Zero)
            {
                ThorlabsSC30Native.CloseDevice(_deviceHandle);
            }
        }
        finally
        {
            _deviceHandle = IntPtr.Zero;
            _isOpen = false;
            SetState(DeviceState.Disconnected);
            _ioGate.Release();
        }
    }

    public Task OpenAsync(CancellationToken token = default) => SetOpenAsync(true, token);

    public Task CloseAsync(CancellationToken token = default) => SetOpenAsync(false, token);

    public async Task SetOpenAsync(bool isOpen, CancellationToken token = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _ioGate.WaitAsync(token);
        try
        {
            SetState(DeviceState.Busy);
            var writePath = WriteShutterState(isOpen);
            _isOpen = string.Equals(writePath, "enable_channel", StringComparison.OrdinalIgnoreCase)
                ? ReadEnableChannelStateSafe(isOpen)
                : ReadOpenStateSafe(defaultValue: isOpen);

            if (_isOpen != isOpen)
            {
                _logger.LogWarning("SC30 快门状态回读不一致：请求={Requested}，实际={Actual}", isOpen, _isOpen);
            }

            SetState(DeviceState.Connected);
            _logger.LogInformation("SC30 快门已切换为{State}", _isOpen ? "打开" : "关闭");
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "断开 SC30 快门时释放资源失败");
        }
    }

    private string WriteShutterState(bool isOpen)
    {
        // Prefer direct shutter-state parameter. Fallback to enable_channel for older plugin builds.
        if (TryWriteBoolParameter("channel_shutter_state", isOpen))
        {
            return "channel_shutter_state";
        }

        var enableValue = _enableChannelInverted ? !isOpen : isOpen;
        if (TryWriteBoolParameter("enable_channel", enableValue))
        {
            return "enable_channel";
        }

        throw new InvalidOperationException("SC30 可写参数不可用（channel_shutter_state / enable_channel）");
    }

    private void UnlockControllerAfterConnect()
    {
        // The official SC30 plugin declares unlocked_state as a controller-level (num_channels=0) rw uchar.
        if (!TryWriteBoolParameter("unlocked_state", true, ControllerChannel))
        {
            throw new InvalidOperationException("SC30 自动解锁失败：可写参数 unlocked_state 不可用");
        }

        if (!TryReadBoolParameter("unlocked_state", out var unlocked, ControllerChannel))
        {
            throw new InvalidOperationException("SC30 自动解锁失败：无法回读 unlocked_state");
        }

        if (!unlocked)
        {
            throw new InvalidOperationException("SC30 自动解锁失败：控制器回读仍为锁定状态");
        }

        _logger.LogInformation("SC30 控制器连接后自动解锁已通过回读确认");
    }

    private bool ReadEnableChannelStateSafe(bool defaultValue)
    {
        if (!TryReadBoolParameter("enable_channel", out var enableValue))
        {
            return defaultValue;
        }

        // If readback disagrees with the command, try inverse semantics once without rewriting device state.
        var interpreted = _enableChannelInverted ? !enableValue : enableValue;
        if (interpreted == defaultValue)
        {
            return interpreted;
        }

        var inverse = !enableValue;
        if (inverse == defaultValue)
        {
            _enableChannelInverted = !_enableChannelInverted;
            _logger.LogWarning("SC30 的 enable_channel 状态疑似反向，切换反向模式为 {Inverted}", _enableChannelInverted);
            return inverse;
        }

        return interpreted;
    }

    private bool TryWriteBoolParameter(string parameterName, bool value, IReadOnlyList<int>? channelCandidates = null)
    {
        var hParam = ThorlabsSC30Native.GetParameterHandle(_deviceHandle, parameterName);
        if (hParam == IntPtr.Zero)
        {
            return false;
        }

        foreach (var channelId in channelCandidates ?? ChannelCandidates)
        {
            // Drain previous error, so we only inspect the current operation's result.
            _ = ThorlabsSC30Native.GetLastErrorMessage(_deviceHandle);

            var payload = new byte[] { value ? (byte)1 : (byte)0 };
            ThorlabsSC30Native.SetParameterValueToDevice(_deviceHandle, hParam, channelId, payload, payload.Length);

            var err = ThorlabsSC30Native.GetLastErrorMessage(_deviceHandle);
            if (string.IsNullOrWhiteSpace(err))
            {
                _logger.LogDebug("SC30 参数写入成功：参数={Param}，通道={Channel}，值={Value}", parameterName, channelId, value);
                return true;
            }

            _logger.LogDebug("SC30 参数写入尝试失败：参数={Param}，通道={Channel}，错误={Error}", parameterName, channelId, err);
        }

        _logger.LogWarning("SC30 参数在全部候选通道上均写入失败：参数={Param}", parameterName);
        return false;
    }

    private bool ReadOpenStateSafe(bool defaultValue = false)
    {
        try
        {
            if (TryReadBoolParameter("channel_shutter_state", out var shutterState))
            {
                return shutterState;
            }

            if (TryReadBoolParameter("enable_channel", out var enableChannel))
            {
                return _enableChannelInverted ? !enableChannel : enableChannel;
            }

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private bool TryReadBoolParameter(string parameterName, out bool value, IReadOnlyList<int>? channelCandidates = null)
    {
        value = false;
        var hParam = ThorlabsSC30Native.GetParameterHandle(_deviceHandle, parameterName);
        if (hParam == IntPtr.Zero)
        {
            return false;
        }

        foreach (var channelId in channelCandidates ?? ChannelCandidates)
        {
            try
            {
                _ = ThorlabsSC30Native.GetLastErrorMessage(_deviceHandle);

                var payload = new byte[1];
                ThorlabsSC30Native.GetParameterValueFromDevice(_deviceHandle, hParam, channelId, payload, payload.Length);
                var err = ThorlabsSC30Native.GetLastErrorMessage(_deviceHandle);
                if (!string.IsNullOrWhiteSpace(err))
                {
                    _logger.LogDebug("SC30 参数读取尝试失败：参数={Param}，通道={Channel}，错误={Error}", parameterName, channelId, err);
                    continue;
                }

                value = payload[0] != 0;
                _logger.LogDebug("SC30 参数读取成功：参数={Param}，通道={Channel}，值={Value}", parameterName, channelId, value);
                return true;
            }
            catch
            {
                // try next channel
            }
        }

        return false;
    }

    private static bool TryFindDevice(string? wantedSerial, out (IntPtr DeviceHandle, string Serial, string Name) selected)
    {
        selected = default;

        var moduleCount = ThorlabsSC30Native.NumAvailablePluginModules();
        for (var moduleIndex = 1; moduleIndex <= moduleCount; moduleIndex++)
        {
            var moduleHandle = ThorlabsSC30Native.GetPluginModule(moduleIndex);
            if (moduleHandle == IntPtr.Zero)
            {
                continue;
            }

            var vid = ThorlabsSC30Native.ParseHexValue(ThorlabsSC30Native.ReadModuleProperty(moduleHandle, ThorlabsSC30Native.ModuleVendorId));
            var pid = ThorlabsSC30Native.ParseHexValue(ThorlabsSC30Native.ReadModuleProperty(moduleHandle, ThorlabsSC30Native.ModuleProductId));
            if (vid != TargetVendorId || !TargetProductIds.Contains(pid))
            {
                continue;
            }

            var deviceCount = ThorlabsSC30Native.NumDevices(moduleHandle);
            for (var deviceIndex = 1; deviceIndex <= deviceCount; deviceIndex++)
            {
                var deviceHandle = ThorlabsSC30Native.GetPluginDevice(moduleHandle, deviceIndex);
                if (deviceHandle == IntPtr.Zero)
                {
                    continue;
                }

                var serial = ThorlabsSC30Native.ReadDeviceProperty(deviceHandle, ThorlabsSC30Native.DeviceSn);
                if (string.IsNullOrWhiteSpace(serial))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(wantedSerial) && !string.Equals(serial, wantedSerial, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = ThorlabsSC30Native.ReadDeviceProperty(deviceHandle, ThorlabsSC30Native.DeviceName);
                selected = (deviceHandle, serial, name);
                return true;
            }
        }

        return false;
    }

    private static string? ParseMappedSerial(string descriptorId)
    {
        if (string.IsNullOrWhiteSpace(descriptorId))
        {
            return null;
        }

        if (!descriptorId.StartsWith(DescriptorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var serial = descriptorId.Substring(DescriptorPrefix.Length).Trim();
        return string.IsNullOrWhiteSpace(serial) ? null : serial;
    }

    private void SetState(DeviceState state)
    {
        State = state;
        StateChanged?.Invoke(this, State);
    }

    private void EnsureConnected()
    {
        if (_deviceHandle == IntPtr.Zero || State == DeviceState.Disconnected)
        {
            throw new InvalidOperationException("SC30 shutter is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ThorlabsSC30Shutter));
        }
    }
}
