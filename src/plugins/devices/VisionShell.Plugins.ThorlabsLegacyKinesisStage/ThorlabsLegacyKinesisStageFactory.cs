using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsLegacyKinesisStage;

public sealed class ThorlabsLegacyKinesisStageFactory : IDeviceFactory, IDeviceDescriptorProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ThorlabsLegacyKinesisStageFactory> _logger;
    private readonly List<IDeviceDescriptor> _descriptors = new();

    private record Descriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

    public ThorlabsLegacyKinesisStageFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ThorlabsLegacyKinesisStageFactory>();
    }

    public string DeviceType => "thorlabs-kinesis-stage";
    public string DisplayName => "Thorlabs Kinesis Stage (x64)";
    public bool IsCloneable => false;

    public IEnumerable<IDeviceDescriptor> GetDescriptors() => _descriptors;

    public async Task<bool> DiscoverAsync(CancellationToken token = default)
    {
        var discovered = await Task.Run(() =>
        {
            var descriptors = new List<IDeviceDescriptor>();
            try
            {
                token.ThrowIfCancellationRequested();
                var sdk = KinesisLegacySdk.TryCreate(_logger, out var sdkError);
                if (sdk == null)
                {
                    _logger.LogWarning("Kinesis SDK 尚未就绪：{Error}", sdkError);
                    return descriptors;
                }

                sdk.BuildDeviceList();
                var targets = sdk.DiscoverTargets();
                if (targets.Count == 0)
                {
                    Thread.Sleep(80);
                    token.ThrowIfCancellationRequested();
                    sdk.BuildDeviceList();
                    targets = sdk.DiscoverTargets();
                }

                foreach (var target in targets)
                {
                    token.ThrowIfCancellationRequested();
                    descriptors.Add(new Descriptor(
                        KinesisLegacyIds.Format(target),
                        target.DisplayName,
                        "IStage",
                        DeviceType,
                        false));
                }

                return descriptors;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kinesis 设备发现失败");
                return descriptors;
            }
        }, token);

        _descriptors.Clear();
        _descriptors.AddRange(discovered);

        if (_descriptors.Count > 0)
        {
            _logger.LogInformation("Kinesis 设备发现共找到 {Count} 个位移通道", _descriptors.Count);
        }
        else
        {
            _logger.LogWarning("Kinesis SDK 已加载，但未找到受支持的位移目标（KDC/BDC/KVS）；请检查设备供电、USB 连接和型号兼容性");
        }

        return _descriptors.Count > 0;
    }

    public IDevice CreateDevice(ILogger logger)
    {
        return new ThorlabsLegacyKinesisStage(_loggerFactory.CreateLogger<ThorlabsLegacyKinesisStage>());
    }
}
