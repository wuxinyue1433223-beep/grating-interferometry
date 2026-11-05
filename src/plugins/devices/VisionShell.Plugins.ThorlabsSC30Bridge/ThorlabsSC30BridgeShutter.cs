using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;
using VisionShell.SC30Bridge.Protocol;

namespace VisionShell.Plugins.ThorlabsSC30Bridge;

public sealed class ThorlabsSC30BridgeShutter : IShutter
{
    private readonly ILogger _logger;
    private readonly Sc30BridgeClient _client = new();
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private bool _disposed;

    public ThorlabsSC30BridgeShutter(ILogger logger, string? deviceId = null)
    {
        _logger = logger;
        DeviceId = deviceId ?? string.Empty;
        Name = "Thorlabs SC30 Shutter";
        State = DeviceState.Disconnected;
    }

    public string DeviceId { get; private set; }
    public string Name { get; private set; }
    public DeviceState State { get; private set; }
    public bool IsOpen { get; private set; }

    public event EventHandler<DeviceState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public void SetOpenDeviceId(string deviceId)
    {
        if (State != DeviceState.Disconnected)
        {
            throw new InvalidOperationException("Cannot change device id while connected.");
        }

        DeviceId = deviceId ?? string.Empty;
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        await _ioGate.WaitAsync(token);
        try
        {
            var response = await _client.SendAsync(new Sc30BridgeRequest("connect", DeviceId), token);
            Apply(response, "connect");
            SetState(DeviceState.Connected);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            SetState(DeviceState.Error);
            throw;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_disposed || State == DeviceState.Disconnected)
        {
            return;
        }

        await _ioGate.WaitAsync();
        try
        {
            var response = await _client.SendAsync(new Sc30BridgeRequest("disconnect"));
            if (!response.Success)
            {
                _logger.LogWarning("SC30 桥接断开失败：{Error}", response.Error);
            }
        }
        finally
        {
            IsOpen = false;
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
            var response = await _client.SendAsync(new Sc30BridgeRequest("set-open", IsOpen: isOpen), token);
            Apply(response, "set-open");
            SetState(DeviceState.Connected);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            SetState(DeviceState.Connected);
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

        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放 SC30 桥接快门资源失败");
        }
        finally
        {
            _disposed = true;
        }
    }

    private void Apply(Sc30BridgeResponse response, string operation)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? $"SC30 bridge {operation} failed.");
        }

        DeviceId = response.DeviceId ?? DeviceId;
        Name = response.Name ?? Name;
        IsOpen = response.IsOpen ?? IsOpen;
    }

    private void EnsureConnected()
    {
        if (State != DeviceState.Connected)
        {
            throw new InvalidOperationException("SC30 shutter is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ThorlabsSC30BridgeShutter));
        }
    }

    private void SetState(DeviceState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
