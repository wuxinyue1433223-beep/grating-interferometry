using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;
using VisionShell.SC30Bridge.Protocol;

namespace VisionShell.Plugins.ThorlabsSC30Bridge;

public sealed class ThorlabsSC30BridgeFactory : IDeviceFactory, IDeviceDescriptorProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ThorlabsSC30BridgeFactory> _logger;
    private readonly Sc30BridgeClient _client = new();
    private readonly List<IDeviceDescriptor> _descriptors = new();

    private sealed record Descriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

    public ThorlabsSC30BridgeFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ThorlabsSC30BridgeFactory>();
    }

    public string DeviceType => "thorlabs-sc30-shutter";
    public string DisplayName => "Thorlabs SC30 Shutter (x86 bridge)";
    public bool IsCloneable => false;

    public IEnumerable<IDeviceDescriptor> GetDescriptors() => _descriptors;

    public async Task<bool> DiscoverAsync(CancellationToken token = default)
    {
        _descriptors.Clear();
        try
        {
            var response = await _client.SendAsync(new Sc30BridgeRequest("discover"), token);
            if (!response.Success)
            {
                _logger.LogWarning("SC30 桥接设备发现失败：{Error}", response.Error);
                return false;
            }

            foreach (var device in response.Devices ?? [])
            {
                _descriptors.Add(new Descriptor(device.DeviceId, device.DisplayName, "IShutter", DeviceType, false));
            }

            _logger.LogInformation("SC30 桥接设备发现共找到 {Count} 个快门设备", _descriptors.Count);
            return _descriptors.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SC30 桥接设备发现失败");
            return false;
        }
    }

    public IDevice CreateDevice(ILogger logger) => new ThorlabsSC30BridgeShutter(_loggerFactory.CreateLogger<ThorlabsSC30BridgeShutter>());
}
