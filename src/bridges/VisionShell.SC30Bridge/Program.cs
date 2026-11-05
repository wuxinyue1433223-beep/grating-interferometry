using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;
using VisionShell.Plugins.ThorlabsSC30Shutter;
using VisionShell.SC30Bridge.Protocol;

var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Information));
var service = new Sc30BridgeService(loggerFactory);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var pipeName = ReadArgument(args, "--pipe") ?? Sc30BridgeProtocol.PipeName;
if (int.TryParse(ReadArgument(args, "--parent-pid"), out var parentProcessId))
{
    _ = ExitWithParentAsync(parentProcessId);
}

while (true)
{
    await using var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    await pipe.WaitForConnectionAsync();
    try
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync();
        var request = string.IsNullOrWhiteSpace(line)
            ? null
            : JsonSerializer.Deserialize<Sc30BridgeRequest>(line, jsonOptions);
        var response = request is null
            ? new Sc30BridgeResponse(false, "SC30 桥接请求无效")
            : await service.HandleAsync(request);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
    }
    catch (IOException)
    {
        // The main process closed the pipe. Wait for the next request or exit with the parent.
    }
    catch (Exception ex)
    {
        try
        {
            using var errorWriter = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await errorWriter.WriteLineAsync(JsonSerializer.Serialize(new Sc30BridgeResponse(false, ex.Message), jsonOptions));
        }
        catch (IOException)
        {
            // The client disconnected before the error response could be sent.
        }
    }
}

static string? ReadArgument(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }

    return null;
}

static async Task ExitWithParentAsync(int parentProcessId)
{
    try
    {
        using var parent = Process.GetProcessById(parentProcessId);
        await parent.WaitForExitAsync();
    }
    catch
    {
        // Parent already exited.
    }

    Environment.Exit(0);
}

internal sealed class Sc30BridgeService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private ThorlabsSC30Shutter? _shutter;

    public Sc30BridgeService(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Sc30BridgeService>();
    }

    public async Task<Sc30BridgeResponse> HandleAsync(Sc30BridgeRequest request)
    {
        try
        {
            return request.Operation.ToLowerInvariant() switch
            {
                "discover" => await DiscoverAsync(),
                "connect" => await ConnectAsync(request.DeviceId),
                "set-open" => await SetOpenAsync(request.IsOpen),
                "disconnect" => await DisconnectAsync(),
                "health" => new Sc30BridgeResponse(true, DeviceId: _shutter?.DeviceId, Name: _shutter?.Name, IsOpen: _shutter?.IsOpen),
                _ => new Sc30BridgeResponse(false, $"不支持的 SC30 桥接操作：{request.Operation}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SC30 桥接操作 {Operation} 失败", request.Operation);
            return new Sc30BridgeResponse(false, ex.Message);
        }
    }

    private async Task<Sc30BridgeResponse> DiscoverAsync()
    {
        var factory = new ThorlabsSC30ShutterFactory(_loggerFactory);
        await factory.DiscoverAsync();
        var devices = factory.GetDescriptors()
            .Select(descriptor => new Sc30BridgeDevice(descriptor.DescriptorId, descriptor.DisplayName))
            .ToArray();
        return new Sc30BridgeResponse(true, Devices: devices);
    }

    private async Task<Sc30BridgeResponse> ConnectAsync(string? deviceId)
    {
        if (_shutter is not null
            && _shutter.State == DeviceState.Connected
            && string.Equals(_shutter.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            return new Sc30BridgeResponse(true, DeviceId: _shutter.DeviceId, Name: _shutter.Name, IsOpen: _shutter.IsOpen);
        }

        if (_shutter is not null && !string.Equals(_shutter.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            await _shutter.DisconnectAsync();
            _shutter.Dispose();
            _shutter = null;
        }

        _shutter ??= new ThorlabsSC30Shutter(_loggerFactory.CreateLogger<ThorlabsSC30Shutter>(), deviceId);
        if (_shutter.State == DeviceState.Disconnected
            && !string.Equals(_shutter.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            _shutter.SetOpenDeviceId(deviceId ?? string.Empty);
        }
        if (_shutter.State != DeviceState.Connected)
        {
            await _shutter.ConnectAsync();
        }

        return new Sc30BridgeResponse(true, DeviceId: _shutter.DeviceId, Name: _shutter.Name, IsOpen: _shutter.IsOpen);
    }

    private async Task<Sc30BridgeResponse> SetOpenAsync(bool? isOpen)
    {
        if (_shutter is null || _shutter.State != DeviceState.Connected)
        {
            return new Sc30BridgeResponse(false, "SC30 快门尚未连接");
        }

        await _shutter.SetOpenAsync(isOpen ?? false);
        return new Sc30BridgeResponse(true, DeviceId: _shutter.DeviceId, Name: _shutter.Name, IsOpen: _shutter.IsOpen);
    }

    private async Task<Sc30BridgeResponse> DisconnectAsync()
    {
        if (_shutter is not null)
        {
            await _shutter.DisconnectAsync();
            _shutter.Dispose();
            _shutter = null;
        }

        return new Sc30BridgeResponse(true);
    }
}
