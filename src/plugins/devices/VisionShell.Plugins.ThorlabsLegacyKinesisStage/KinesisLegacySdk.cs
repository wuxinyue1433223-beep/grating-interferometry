using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Thorlabs.MotionControl.Benchtop.DCServoCLI;
using Thorlabs.MotionControl.DeviceManagerCLI;
using Thorlabs.MotionControl.GenericMotorCLI;
using Thorlabs.MotionControl.VerticalStageCLI;

namespace VisionShell.Plugins.ThorlabsLegacyKinesisStage;

internal enum KinesisLegacyDeviceKind
{
    BdcChannel,
    VerticalStage,
    KdcServo
}

internal readonly record struct KinesisLegacyTarget(KinesisLegacyDeviceKind Kind, string SerialNumber, int Channel)
{
    public string DisplayName => Kind switch
    {
        KinesisLegacyDeviceKind.BdcChannel => $"BDC {SerialNumber} - CH{Channel}",
        KinesisLegacyDeviceKind.VerticalStage => $"KVS {SerialNumber}",
        KinesisLegacyDeviceKind.KdcServo => $"KDC {SerialNumber}",
        _ => SerialNumber
    };
}

internal static class KinesisLegacyIds
{
    private const string Prefix = "kinesis-legacy:";
    private const string LegacyBdcPrefix = "kinesis-bdc:";

    public static string Format(KinesisLegacyTarget target)
    {
        return target.Kind switch
        {
            KinesisLegacyDeviceKind.BdcChannel => $"{Prefix}bdc:{target.SerialNumber}-{target.Channel}",
            KinesisLegacyDeviceKind.VerticalStage => $"{Prefix}kvs:{target.SerialNumber}",
            KinesisLegacyDeviceKind.KdcServo => $"{Prefix}kdc:{target.SerialNumber}",
            _ => $"{Prefix}unknown:{target.SerialNumber}"
        };
    }

    public static bool TryParse(string? value, out KinesisLegacyTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var id = value.Trim();

        // Backward compatibility with the previous bdc-only identifier format.
        if (id.StartsWith(LegacyBdcPrefix, StringComparison.OrdinalIgnoreCase))
        {
            id = id.Substring(LegacyBdcPrefix.Length);
            var idxLegacy = id.LastIndexOf('-');
            if (idxLegacy <= 0 || idxLegacy >= id.Length - 1) return false;

            var legacySerial = id[..idxLegacy];
            var legacyChannelText = id[(idxLegacy + 1)..];
            if (!int.TryParse(legacyChannelText, out var legacyChannel) || legacyChannel <= 0) return false;

            target = new KinesisLegacyTarget(KinesisLegacyDeviceKind.BdcChannel, legacySerial, legacyChannel);
            return true;
        }

        if (!id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        id = id.Substring(Prefix.Length);

        if (id.StartsWith("bdc:", StringComparison.OrdinalIgnoreCase))
        {
            id = id.Substring("bdc:".Length);
            var idx = id.LastIndexOf('-');
            if (idx <= 0 || idx >= id.Length - 1) return false;

            var serial = id[..idx];
            var channelText = id[(idx + 1)..];
            if (!int.TryParse(channelText, out var channel) || channel <= 0) return false;

            target = new KinesisLegacyTarget(KinesisLegacyDeviceKind.BdcChannel, serial, channel);
            return true;
        }

        if (id.StartsWith("kvs:", StringComparison.OrdinalIgnoreCase))
        {
            var serial = id.Substring("kvs:".Length);
            if (string.IsNullOrWhiteSpace(serial)) return false;
            target = new KinesisLegacyTarget(KinesisLegacyDeviceKind.VerticalStage, serial, 0);
            return true;
        }

        if (id.StartsWith("kdc:", StringComparison.OrdinalIgnoreCase))
        {
            var serial = id.Substring("kdc:".Length);
            if (string.IsNullOrWhiteSpace(serial)) return false;
            target = new KinesisLegacyTarget(KinesisLegacyDeviceKind.KdcServo, serial, 0);
            return true;
        }

        return false;
    }
}

internal sealed class KinesisLegacySdk
{
    private const int DevicePollingIntervalMs = 100;
    private static readonly object ResolverSync = new();
    private static readonly Dictionary<string, string> ManagedAssemblyMap = new(StringComparer.OrdinalIgnoreCase);
    private static bool _resolverRegistered;

    private readonly ILogger _logger;
    private readonly Assembly _deviceManagerAsm;
    private readonly Assembly _dcServoAsm;
    private readonly Assembly? _verticalStageAsm;
    private readonly Assembly? _kCubeDcServoAsm;

    private readonly Type _deviceManagerType;
    private readonly Type _benchtopDcServoType;
    private readonly Type? _verticalStageType;
    private readonly Type? _kCubeDcServoType;
    private readonly bool _enableBdcDiscovery;
    private readonly bool _enableKdcDiscovery;
    private readonly bool _strictDiscovery;
    private readonly int _connectSettleMs;
    private readonly int _discoveryRetryCount;
    private readonly int _discoveryRetryDelayMs;

    private KinesisLegacySdk(ILogger logger, Assembly deviceManagerAsm, Assembly dcServoAsm, Assembly? verticalStageAsm, Assembly? kCubeDcServoAsm)
    {
        _logger = logger;
        _deviceManagerAsm = deviceManagerAsm;
        _dcServoAsm = dcServoAsm;
        _verticalStageAsm = verticalStageAsm;
        _kCubeDcServoAsm = kCubeDcServoAsm;

        _deviceManagerType = deviceManagerAsm.GetType("Thorlabs.MotionControl.DeviceManagerCLI.DeviceManagerCLI")
            ?? throw new InvalidOperationException("Type DeviceManagerCLI not found");

        _benchtopDcServoType = dcServoAsm.GetType("Thorlabs.MotionControl.Benchtop.DCServoCLI.BenchtopDCServo")
            ?? throw new InvalidOperationException("Type BenchtopDCServo not found");

        _verticalStageType = verticalStageAsm?.GetType("Thorlabs.MotionControl.VerticalStageCLI.VerticalStage");
        _kCubeDcServoType = kCubeDcServoAsm?.GetType("Thorlabs.MotionControl.KCube.DCServoCLI.KCubeDCServo");
        // Default to BDC discovery enabled. Allow explicit disable via env var.
        _enableBdcDiscovery = !string.Equals(Environment.GetEnvironmentVariable("VISION_LEGACY_DISCOVER_BDC"), "0", StringComparison.OrdinalIgnoreCase);
        // KDC is not part of the production routing. Keep its reflection adapter opt-in for diagnostics only.
        _enableKdcDiscovery = string.Equals(Environment.GetEnvironmentVariable("VISION_LEGACY_DISCOVER_KDC"), "1", StringComparison.OrdinalIgnoreCase);
        _strictDiscovery = string.Equals(Environment.GetEnvironmentVariable("VISION_LEGACY_DISCOVERY_STRICT"), "1", StringComparison.OrdinalIgnoreCase);

        var settleText = Environment.GetEnvironmentVariable("VISION_LEGACY_CONNECT_SETTLE_MS");
        _connectSettleMs = int.TryParse(settleText, out var settleMs)
            ? Math.Clamp(settleMs, 50, 1000)
            : 500;

        var retriesText = Environment.GetEnvironmentVariable("VISION_LEGACY_DISCOVERY_RETRIES");
        _discoveryRetryCount = int.TryParse(retriesText, out var retries)
            ? Math.Clamp(retries, 1, 8)
            : 1;

        var retryDelayText = Environment.GetEnvironmentVariable("VISION_LEGACY_DISCOVERY_RETRY_DELAY_MS");
        _discoveryRetryDelayMs = int.TryParse(retryDelayText, out var retryDelayMs)
            ? Math.Clamp(retryDelayMs, 50, 1500)
            : 80;
    }

    public static KinesisLegacySdk? TryCreate(ILogger logger, out string error)
    {
        try
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
            var searchRoots = BuildSearchRoots(baseDir);
            logger.LogInformation("Kinesis SDK 搜索目录：{Roots}", string.Join("; ", searchRoots));

            if (searchRoots.Count == 0)
            {
                error = "No SDK search directory found. Expected plugin libs folder or repository Kinesis folder.";
                return null;
            }

            EnsureManagedAssemblyResolver(searchRoots);
            EnsureNativeSearchPath(searchRoots);

            var deviceManagerPath = ResolveDllPath(searchRoots, "Thorlabs.MotionControl.DeviceManagerCLI.dll");
            var genericMotorPath = ResolveDllPath(searchRoots, "Thorlabs.MotionControl.GenericMotorCLI.dll");
            var benchtopDcServoPath = ResolveDllPath(searchRoots,
                "Thorlabs.MotionControl.Benchtop.DCServoCLI.dll",
                "ThorLabs.MotionControl.Benchtop.DCServoCLI.dll");

            if (string.IsNullOrWhiteSpace(deviceManagerPath) || string.IsNullOrWhiteSpace(genericMotorPath) || string.IsNullOrWhiteSpace(benchtopDcServoPath))
            {
                var rootsText = string.Join("; ", searchRoots);
                error = $"Missing required Kinesis legacy CLI DLLs. Need DeviceManagerCLI, GenericMotorCLI, Benchtop.DCServoCLI. Search roots: {rootsText}";
                return null;
            }

            var archError = ValidateProcessAndDllArchitecture(deviceManagerPath);
            if (!string.IsNullOrWhiteSpace(archError))
            {
                error = archError;
                return null;
            }

            var verticalStagePath = ResolveDllPath(searchRoots,
                "Thorlabs.MotionControl.VerticalStageCLI.dll",
                "ThorLabs.MotionControl.VerticalStageCLI.dll");
            var kCubeDcServoPath = ResolveDllPath(searchRoots,
                "Thorlabs.MotionControl.KCube.DCServoCLI.dll",
                "ThorLabs.MotionControl.KCube.DCServoCLI.dll");

            logger.LogInformation(
                "Kinesis DLL 已解析：设备管理={DeviceManager}；通用电机={GenericMotor}；BDC={Bdc}；KVS={Kvs}；KDC={Kdc}",
                deviceManagerPath,
                genericMotorPath,
                benchtopDcServoPath,
                verticalStagePath ?? "<not found>",
                kCubeDcServoPath ?? "<not found>");

            var deviceManagerAsm = LoadAssemblyInCurrentContext(deviceManagerPath);
            _ = LoadAssemblyInCurrentContext(genericMotorPath);
            var dcServoAsm = LoadAssemblyInCurrentContext(benchtopDcServoPath);
            var verticalStageAsm = !string.IsNullOrWhiteSpace(verticalStagePath)
                ? LoadAssemblyInCurrentContext(verticalStagePath)
                : null;
            var kCubeDcServoAsm = !string.IsNullOrWhiteSpace(kCubeDcServoPath)
                ? LoadAssemblyInCurrentContext(kCubeDcServoPath)
                : null;

            error = string.Empty;
            return new KinesisLegacySdk(logger, deviceManagerAsm, dcServoAsm, verticalStageAsm, kCubeDcServoAsm);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public void BuildDeviceList()
    {
        // Official Kinesis discovery entry point. Production KVS/BDC paths are strongly typed.
        DeviceManagerCLI.BuildDeviceList();
    }

    public void BuildDeviceListWithRetry()
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= _discoveryRetryCount; attempt++)
        {
            try
            {
                BuildDeviceList();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Kinesis 官方 BuildDeviceList 第 {Attempt}/{MaxAttempt} 次调用失败", attempt, _discoveryRetryCount);
                if (attempt < _discoveryRetryCount)
                {
                    Thread.Sleep(_discoveryRetryDelayMs);
                }
            }
        }

        if (lastError != null)
        {
            throw new InvalidOperationException($"Legacy BuildDeviceList failed after {_discoveryRetryCount} attempts.", lastError);
        }
    }

    public IReadOnlyList<KinesisLegacyTarget> DiscoverTargetsWithRetry()
    {
        for (var attempt = 1; attempt <= _discoveryRetryCount; attempt++)
        {
            try
            {
                BuildDeviceList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kinesis 设备发现构建阶段第 {Attempt}/{MaxAttempt} 次失败", attempt, _discoveryRetryCount);
            }

            try
            {
                var currentTargets = DiscoverTargets();
                if (currentTargets.Count > 0)
                {
                    return currentTargets;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kinesis 目标探测第 {Attempt}/{MaxAttempt} 次失败", attempt, _discoveryRetryCount);
            }

            if (attempt < _discoveryRetryCount)
            {
                Thread.Sleep(_discoveryRetryDelayMs);
            }
        }

        return Array.Empty<KinesisLegacyTarget>();
    }

    public IReadOnlyList<KinesisLegacyTarget> DiscoverTargets()
    {
        var list = new List<KinesisLegacyTarget>();

        var bdcSerials = _enableBdcDiscovery ? GetBdcSerials() : Array.Empty<string>();
        // KDC is diagnosed through the official Kinesis device list only. It is never
        // added as a production target; both actuator roles remain XA-only.
        var detectedKdcSerials = GetKdcSerials();
        var kdcSerials = _enableKdcDiscovery ? detectedKdcSerials : Array.Empty<string>();
        var bdcSerialSet = new HashSet<string>(bdcSerials, StringComparer.OrdinalIgnoreCase);
        var kvsSerials = GetVerticalStageSerials();
        _logger.LogInformation(
            "Kinesis 序列号探测结果：BDC={BdcCount}，KVS={KvsCount}，KDC 只读诊断={KdcCount}（{KdcIds}），KDC 生产路由已禁用",
            bdcSerials.Count,
            kvsSerials.Count,
            detectedKdcSerials.Count,
            string.Join(", ", detectedKdcSerials));

        foreach (var serial in kdcSerials)
        {
            list.Add(new KinesisLegacyTarget(KinesisLegacyDeviceKind.KdcServo, serial, 0));
        }

        foreach (var serial in bdcSerials)
        {
            var channels = GetAvailableBdcChannels(serial);
            if (channels.Count == 0)
            {
                _logger.LogDebug("BDC 序列号 {Serial} 未确认到可用通道，已跳过", serial);
                continue;
            }

            foreach (var channel in channels)
            {
                list.Add(new KinesisLegacyTarget(KinesisLegacyDeviceKind.BdcChannel, serial, channel));
            }
        }

        foreach (var serial in kvsSerials)
        {
            if (bdcSerialSet.Contains(serial))
            {
                _logger.LogDebug("KVS 目标 {Serial} 已被识别为 BDC，已跳过", serial);
                continue;
            }

            list.Add(new KinesisLegacyTarget(KinesisLegacyDeviceKind.VerticalStage, serial, 0));
        }

        _logger.LogInformation("Kinesis 共发现 {Count} 个受支持目标", list.Count);
        return list;
    }

    public KinesisLegacyConnection Connect(KinesisLegacyTarget target, TimeSpan settingsTimeout)
    {
        return target.Kind switch
        {
            KinesisLegacyDeviceKind.BdcChannel => ConnectBdc(target.SerialNumber, target.Channel, settingsTimeout),
            KinesisLegacyDeviceKind.VerticalStage => ConnectVertical(target.SerialNumber, settingsTimeout),
            KinesisLegacyDeviceKind.KdcServo => ConnectKdc(target.SerialNumber, settingsTimeout),
            _ => throw new NotSupportedException($"Unsupported legacy target kind: {target.Kind}")
        };
    }

    private IReadOnlyList<string> GetKdcSerials()
    {
        if (_kCubeDcServoType == null)
        {
            return Array.Empty<string>();
        }

        var byPrefix = GetSerialsByDevicePrefixes(_kCubeDcServoType);
        if (byPrefix.Count > 0)
        {
            if (_strictDiscovery)
            {
                return byPrefix.Where(CanConnectKdc).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            return byPrefix;
        }

        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var prefixMembers = _kCubeDcServoType
                .GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property)
                .Where(m => m.Name.StartsWith("DevicePrefix", StringComparison.OrdinalIgnoreCase));

            foreach (var member in prefixMembers)
            {
                object? prefixValue = member switch
                {
                    FieldInfo fi => fi.GetValue(null),
                    PropertyInfo pi => pi.GetValue(null),
                    _ => null
                };

                if (prefixValue == null)
                {
                    continue;
                }

                try
                {
                    var listObj = InvokeStatic(_deviceManagerType, "GetDeviceList", prefixValue);
                    foreach (var serial in ToStringList(listObj))
                    {
                        if (_strictDiscovery)
                        {
                            if (CanConnectKdc(serial))
                            {
                                serials.Add(serial);
                            }
                        }
                        else
                        {
                            serials.Add(serial);
                        }
                    }
                }
                catch
                {
                    // Ignore unsupported prefixes and continue probing.
                }
            }
        }
        catch
        {
            // Best-effort discovery.
        }

        if (serials.Count > 0)
        {
            return serials.ToList();
        }

        // Fallback: probe unfiltered serial list when prefix-based enumeration misses devices.
        var all = GetAllSerials();
        if (all.Count == 0)
        {
            return Array.Empty<string>();
        }

        foreach (var serial in all)
        {
            if (_strictDiscovery)
            {
                if (CanConnectVerticalStage(serial))
                {
                    serials.Add(serial);
                }
            }
            else
            {
                serials.Add(serial);
            }
        }

        return serials.ToList();
    }

    private bool CanConnectKdc(string serial)
    {
        if (_kCubeDcServoType == null)
        {
            return false;
        }

        object? device = null;
        try
        {
            device = InvokeStatic(_kCubeDcServoType, "CreateKCubeDCServo", serial);
            if (device == null)
            {
                return false;
            }

            InvokeInstance(device, "Connect", serial);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (device != null)
            {
                TryInvokeInstance(device, "Disconnect", true);
                if (device is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { }
                }
            }
        }
    }

    private KinesisLegacyConnection ConnectKdc(string serialNumber, TimeSpan settingsTimeout)
    {
        if (_kCubeDcServoType == null)
            throw new InvalidOperationException("KCube.DCServoCLI type not available. Please provide Thorlabs.MotionControl.KCube.DCServoCLI.dll");

        var create = InvokeStatic(_kCubeDcServoType, "CreateKCubeDCServo", serialNumber);
        if (create == null)
            throw new InvalidOperationException($"CreateKCubeDCServo returned null for {serialNumber}");

        var device = create;

        InvokeInstance(device, "Connect", serialNumber);

        var isInit = Convert.ToBoolean(InvokeInstance(device, "IsSettingsInitialized") ?? false);
        if (!isInit)
        {
            var timeoutMs = (int)settingsTimeout.TotalMilliseconds;
            InvokeInstance(device, "WaitForSettingsInitialized", timeoutMs);
        }

        InvokeInstance(device, "StartPolling", DevicePollingIntervalMs);
        Thread.Sleep(_connectSettleMs);

        InvokeInstance(device, "EnableDevice");
        Thread.Sleep(_connectSettleMs);

        _ = InvokeInstance(device, "LoadMotorConfiguration", serialNumber);

        var info = InvokeInstance(device, "GetDeviceInfo");
        var sn = Convert.ToString(GetPropertyValue(info, "SerialNumber")) ?? serialNumber;
        var nm = Convert.ToString(GetPropertyValue(info, "Name")) ?? "Kinesis KCube DC Servo";

        return new KinesisLegacyConnection(device, device, device, sn, nm);
    }

    private IReadOnlyList<string> GetBdcSerials()
    {
        var preferred = GetBdcSerialsByPrefix();
        if (preferred.Count > 0)
        {
            return preferred;
        }

        var all = GetAllSerials();
        if (all.Count == 0)
        {
            return Array.Empty<string>();
        }

        var serials = new List<string>();
        foreach (var serial in all)
        {
            if (CanConnectBdc(serial))
            {
                serials.Add(serial);
            }
        }

        return serials.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<string> GetBdcSerialsByPrefix()
    {
        // Match Thorlabs' BDC sample: query each official BenchtopDCServo prefix.
        var officialPrefixes = new[]
        {
            BenchtopDCServo.DevicePrefix43,
            BenchtopDCServo.DevicePrefix79,
            BenchtopDCServo.DevicePrefix101,
            BenchtopDCServo.DevicePrefix105,
            BenchtopDCServo.DevicePrefix117
        };
        var byPrefix = officialPrefixes
            .SelectMany(DeviceManagerCLI.GetDeviceList)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byPrefix.Count > 0)
        {
            return _strictDiscovery
                ? byPrefix.Where(CanConnectBdc).ToList()
                : byPrefix;
        }

        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var prefixMembers = _benchtopDcServoType
                .GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property)
                .Where(m => m.Name.StartsWith("DevicePrefix", StringComparison.OrdinalIgnoreCase));

            foreach (var member in prefixMembers)
            {
                object? prefixValue = member switch
                {
                    FieldInfo fi => fi.GetValue(null),
                    PropertyInfo pi => pi.GetValue(null),
                    _ => null
                };

                if (prefixValue == null)
                {
                    continue;
                }

                try
                {
                    var listObj = InvokeStatic(_deviceManagerType, "GetDeviceList", prefixValue);
                    foreach (var serial in ToStringList(listObj))
                    {
                        if (_strictDiscovery)
                        {
                            if (CanConnectBdc(serial))
                            {
                                serials.Add(serial);
                            }
                        }
                        else
                        {
                            serials.Add(serial);
                        }
                    }
                }
                catch
                {
                    // Ignore unsupported prefixes and continue probing.
                }
            }
        }
        catch
        {
            // Fall back to generic list probing.
        }

        return serials.ToList();
    }

    private bool CanConnectBdc(string serial)
    {
        BenchtopDCServo? device = null;
        try
        {
            device = BenchtopDCServo.CreateBenchtopDCServo(serial);
            if (device == null) return false;
            device.Connect(serial);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (device != null)
            {
                try { device.Disconnect(true); } catch { }
                device.Dispose();
            }
        }
    }

    private IReadOnlyList<int> GetAvailableBdcChannels(string serialNumber)
    {
        var channels = new List<int>();
        try
        {
            // Discovery must not connect and disconnect the controller. On some Kinesis
            // versions BDC_IsChannelValid returns false until the controller is open, so
            // fall back to the official maximum bay count and validate the selected bay
            // once during the real connection.
            var maxChannels = KinesisBdcNative.BDC_MaxChannelCount(serialNumber);
            for (short channel = 1; channel <= maxChannels; channel++)
            {
                if (KinesisBdcNative.BDC_IsChannelValid(serialNumber, channel))
                {
                    channels.Add(channel);
                }
            }

            if (channels.Count == 0)
            {
                // BDC_MaxChannelCount is documented as the number of available bays,
                // not the number of populated bays. The production M30X target is CH1;
                // the real Connect/GetChannel path below still validates it.
                if (maxChannels > 0)
                {
                    channels.Add(1);
                }

                _logger.LogWarning(
                    "Kinesis 原生接口未标记 BDC {Serial} 的已占用通道；官方接口报告最大槽位数 {MaxChannels}，本机 M30X 仅保留 CH1 并在真正连接时校验",
                    serialNumber,
                    maxChannels);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通过 Kinesis 官方原生接口读取 BDC {Serial} 通道失败；发现阶段不打开控制器", serialNumber);
        }

        return channels;
    }

    private static class KinesisBdcNative
    {
        private const string DllName = "Thorlabs.MotionControl.Benchtop.DCServo.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int BDC_MaxChannelCount(string serialNo);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool BDC_IsChannelValid(string serialNo, short channel);
    }

    private KinesisLegacyConnection ConnectBdc(string serialNumber, int channel, TimeSpan settingsTimeout)
    {
        var device = BenchtopDCServo.CreateBenchtopDCServo(serialNumber)
            ?? throw new InvalidOperationException($"CreateBenchtopDCServo returned null for {serialNumber}");

        try
        {
            device.Connect(serialNumber);
            var channelDevice = device.GetChannel(checked((short)channel))
                ?? throw new InvalidOperationException($"Channel {channel} unavailable for {serialNumber}");

            if (!channelDevice.IsSettingsInitialized())
            {
                channelDevice.WaitForSettingsInitialized((int)settingsTimeout.TotalMilliseconds);
            }

            channelDevice.StartPolling(DevicePollingIntervalMs);
            Thread.Sleep(_connectSettleMs);
            channelDevice.EnableDevice();
            Thread.Sleep(_connectSettleMs);
            _ = channelDevice.LoadMotorConfiguration(channelDevice.DeviceID);

            var info = channelDevice.GetDeviceInfo();
            return new KinesisLegacyConnection(device, channelDevice, info.SerialNumberText, info.Name);
        }
        catch
        {
            try { device.Disconnect(true); } catch { }
            device.Dispose();
            throw;
        }
    }

    private KinesisLegacyConnection ConnectVertical(string serialNumber, TimeSpan settingsTimeout)
    {
        var device = VerticalStage.CreateVerticalStage(serialNumber)
            ?? throw new InvalidOperationException($"CreateVerticalStage returned null for {serialNumber}");

        try
        {
            device.Connect(serialNumber);
            if (!device.IsSettingsInitialized())
            {
                device.WaitForSettingsInitialized((int)settingsTimeout.TotalMilliseconds);
            }

            device.StartPolling(DevicePollingIntervalMs);
            Thread.Sleep(_connectSettleMs);
            device.EnableDevice();
            Thread.Sleep(_connectSettleMs);
            _ = device.LoadMotorConfiguration(serialNumber);

            var info = device.GetDeviceInfo();
            return new KinesisLegacyConnection(device, info.SerialNumberText, info.Name);
        }
        catch
        {
            try { device.Disconnect(true); } catch { }
            device.Dispose();
            throw;
        }
    }

    private IReadOnlyList<string> GetVerticalStageSerials()
    {
        if (_verticalStageType == null)
        {
            return Array.Empty<string>();
        }

        var byPrefix = DeviceManagerCLI.GetDeviceList(VerticalStage.DevicePrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byPrefix.Count > 0)
        {
            if (_strictDiscovery)
            {
                return byPrefix.Where(CanConnectVerticalStage).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            return byPrefix;
        }

        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var prefixMembers = _verticalStageType
                .GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property)
                .Where(m => m.Name.StartsWith("DevicePrefix", StringComparison.OrdinalIgnoreCase));

            foreach (var member in prefixMembers)
            {
                object? prefixValue = member switch
                {
                    FieldInfo fi => fi.GetValue(null),
                    PropertyInfo pi => pi.GetValue(null),
                    _ => null
                };

                if (prefixValue == null)
                {
                    continue;
                }

                try
                {
                    var listObj = InvokeStatic(_deviceManagerType, "GetDeviceList", prefixValue);
                    foreach (var serial in ToStringList(listObj))
                    {
                        if (_strictDiscovery)
                        {
                            if (CanConnectVerticalStage(serial))
                            {
                                serials.Add(serial);
                            }
                        }
                        else
                        {
                            serials.Add(serial);
                        }
                    }
                }
                catch
                {
                    // Ignore unsupported prefixes and continue probing.
                }
            }
        }
        catch
        {
            // Best-effort discovery.
        }

        return serials.ToList();
    }

    private bool CanConnectVerticalStage(string serial)
    {
        VerticalStage? device = null;
        try
        {
            device = VerticalStage.CreateVerticalStage(serial);
            if (device == null) return false;

            device.Connect(serial);
            if (!device.IsSettingsInitialized()) device.WaitForSettingsInitialized(5000);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (device != null)
            {
                try { device.Disconnect(true); } catch { }
                device.Dispose();
            }
        }
    }

    private IReadOnlyList<string> GetAllSerials()
    {
        try
        {
            var all = InvokeStatic(_deviceManagerType, "GetDeviceList");
            return ToStringList(all);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "调用未筛选的 Kinesis GetDeviceList 失败");
            return Array.Empty<string>();
        }
    }

    private static bool ChannelExists(object device, int channel)
    {
        try
        {
            var chObj = InvokeInstance(device, "GetChannel", channel);
            return chObj != null;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ToStringList(object? listObj)
    {
        if (listObj is IEnumerable<string> typed)
        {
            return typed.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (listObj is System.Collections.IEnumerable enumerable)
        {
            var list = new List<string>();
            foreach (var item in enumerable)
            {
                var text = item?.ToString();
                if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
            }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return Array.Empty<string>();
    }

    private IReadOnlyList<string> GetSerialsByDevicePrefixes(Type deviceType)
    {
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var prefixMembers = deviceType
                .GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property)
                .Where(m => m.Name.StartsWith("DevicePrefix", StringComparison.OrdinalIgnoreCase));

            foreach (var member in prefixMembers)
            {
                object? prefixValue = member switch
                {
                    FieldInfo fi => fi.GetValue(null),
                    PropertyInfo pi => pi.GetValue(null),
                    _ => null
                };

                if (prefixValue == null)
                {
                    continue;
                }

                try
                {
                    var listObj = InvokeStatic(_deviceManagerType, "GetDeviceList", prefixValue);
                    foreach (var serial in ToStringList(listObj))
                    {
                        serials.Add(serial);
                    }
                }
                catch
                {
                    // Ignore unsupported prefixes and continue probing.
                }
            }
        }
        catch
        {
            // Best-effort discovery.
        }

        return serials.ToList();
    }

    private static object? InvokeStatic(Type type, string method, params object[] args)
    {
        var m = FindMethod(type, method, args, isStatic: true);
        if (m == null)
            throw new MissingMethodException(type.FullName, method);

        return m.Invoke(null, PrepareArguments(m, args));
    }

    private static object? InvokeInstance(object target, string method, params object[] args)
    {
        var t = target.GetType();
        var m = FindMethod(t, method, args, isStatic: false);
        if (m == null)
            throw new MissingMethodException(t.FullName, method);

        return m.Invoke(target, PrepareArguments(m, args));
    }

    private static void TryInvokeInstance(object target, string method, params object[] args)
    {
        try
        {
            _ = InvokeInstance(target, method, args);
        }
        catch
        {
            // best effort
        }
    }

    private static MethodInfo? FindMethod(Type type, string method, object[] args, bool isStatic)
    {
        var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        return type
            .GetMethods(flags)
            .Where(m => string.Equals(m.Name, method, StringComparison.Ordinal))
            .FirstOrDefault(m => IsCompatible(m.GetParameters(), args));
    }

    private static bool IsCompatible(ParameterInfo[] parameters, object[] args)
    {
        if (parameters.Length != args.Length) return false;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (args[i] == null)
            {
                if (parameters[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[i].ParameterType) == null)
                {
                    return false;
                }
                continue;
            }

            var argType = args[i].GetType();
            var target = parameters[i].ParameterType;
            if (target.IsAssignableFrom(argType)) continue;

            try
            {
                _ = Convert.ChangeType(args[i], target);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static object?[] PrepareArguments(MethodInfo method, object[] args)
    {
        var ps = method.GetParameters();
        var outArgs = new object?[args.Length];

        for (var i = 0; i < args.Length; i++)
        {
            var target = ps[i].ParameterType;
            var arg = args[i];

            if (arg == null)
            {
                outArgs[i] = null;
                continue;
            }

            if (target.IsInstanceOfType(arg))
            {
                outArgs[i] = arg;
                continue;
            }

            outArgs[i] = Convert.ChangeType(arg, target);
        }

        return outArgs;
    }

    private static object? GetPropertyValue(object? target, string propName)
    {
        if (target == null) return null;
        return target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }

    private static string? ResolveDllPath(IEnumerable<string> roots, params string[] fileNames)
    {
        foreach (var root in roots)
        {
            foreach (var name in fileNames)
            {
                var direct = Path.Combine(root, name);
                if (File.Exists(direct)) return direct;

                var found = Directory.EnumerateFiles(root, name, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }

        return null;
    }

    private static string? FindKinesisDirectory(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Kinesis");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static List<string> BuildSearchRoots(string baseDir)
    {
        var roots = new List<string>();

        void AddIfExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Directory.Exists(path)) return;
            if (roots.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase))) return;
            roots.Add(path);
        }

        // Plugin assembly directory and common subfolders.
        AddIfExists(baseDir);
        AddIfExists(Path.Combine(baseDir, "libs"));
        AddIfExists(Path.Combine(baseDir, "Kinesis"));

        // Explicit override, useful when packing x86/x64 Kinesis runtimes separately.
        AddIfExists(Environment.GetEnvironmentVariable("VISION_KINESIS_ROOT"));

        var pluginLibs = Path.Combine(baseDir, "libs");
        AddIfExists(pluginLibs);

        var nearestRepoKinesis = FindKinesisDirectory(baseDir);
        AddIfExists(nearestRepoKinesis);

        // Common installation locations on target machines.
        AddIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Thorlabs", "Kinesis"));
        AddIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Thorlabs", "Kinesis"));

        // If app is published with a sibling Kinesis folder, include it.
        AddIfExists(Path.Combine(AppContext.BaseDirectory, "Kinesis"));

        return roots;
    }

    private static string? ValidateProcessAndDllArchitecture(string managedCliDllPath)
    {
        ushort machine;
        try
        {
            machine = ReadPeMachine(managedCliDllPath);
        }
        catch (Exception ex)
        {
            return $"Failed to inspect Kinesis DLL architecture for {managedCliDllPath}: {ex.Message}";
        }

        var processArch = Environment.Is64BitProcess ? "x64" : "x86";
        var dllArch = machine switch
        {
            0x8664 => "x64",
            0x014c => "x86",
            _ => $"unknown(0x{machine:X4})"
        };

        if ((Environment.Is64BitProcess && machine == 0x014c) || (!Environment.Is64BitProcess && machine == 0x8664))
        {
            return $"Kinesis CLI architecture mismatch: process={processArch}, DeviceManagerCLI={dllArch}. "
                + "Use matching Kinesis DLLs for this build, or set VISION_KINESIS_ROOT to a compatible runtime folder.";
        }

        return null;
    }

    private static ushort ReadPeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        stream.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = reader.ReadInt32();

        stream.Seek(peOffset + 4, SeekOrigin.Begin);
        return reader.ReadUInt16();
    }

    private static Assembly LoadAssemblyInCurrentContext(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var asmName = AssemblyName.GetAssemblyName(fullPath).Name;
        var context = AssemblyLoadContext.Default;

        var existing = context.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, asmName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        return context.LoadFromAssemblyPath(fullPath);
    }

    private static void EnsureManagedAssemblyResolver(IEnumerable<string> roots)
    {
        lock (ResolverSync)
        {
            foreach (var root in roots)
            {
                foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var name = AssemblyName.GetAssemblyName(dll).Name;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        ManagedAssemblyMap[name] = Path.GetFullPath(dll);
                    }
                    catch (BadImageFormatException)
                    {
                        // Native DLLs are expected in SDK folders; skip.
                    }
                    catch
                    {
                        // Best-effort map build.
                    }
                }
            }

            if (_resolverRegistered)
            {
                return;
            }

            var context = AssemblyLoadContext.Default;
            context.Resolving += ResolveManagedAssembly;
            _resolverRegistered = true;
        }
    }

    private static Assembly? ResolveManagedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!ManagedAssemblyMap.TryGetValue(name, out var path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var existing = context.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            return context.LoadFromAssemblyPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureNativeSearchPath(IEnumerable<string> roots)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var append = new List<string>();
        foreach (var root in roots)
        {
            if (parts.Contains(root))
            {
                continue;
            }

            append.Add(root);
            parts.Add(root);
        }

        if (append.Count == 0)
        {
            return;
        }

        var updated = string.IsNullOrWhiteSpace(current)
            ? string.Join(Path.PathSeparator, append)
            : string.Concat(current, Path.PathSeparator, string.Join(Path.PathSeparator, append));
        Environment.SetEnvironmentVariable("PATH", updated);
    }
}

internal sealed class KinesisLegacyConnection : IDisposable
{
    private readonly object _disconnectTarget;
    private readonly object _pollingTarget;
    private readonly object _motionTarget;
    private readonly BenchtopDCServo? _officialBdcController;
    private readonly DCServoChannel? _officialBdcChannel;
    private readonly VerticalStage? _officialVerticalStage;

    public string SerialNumber { get; }
    public string DeviceDisplayName { get; }

    public KinesisLegacyConnection(object disconnectTarget, object pollingTarget, object motionTarget, string serialNumber, string deviceDisplayName)
    {
        _disconnectTarget = disconnectTarget;
        _pollingTarget = pollingTarget;
        _motionTarget = motionTarget;
        SerialNumber = serialNumber;
        DeviceDisplayName = deviceDisplayName;
    }

    public KinesisLegacyConnection(BenchtopDCServo controller, DCServoChannel channel, string serialNumber, string deviceDisplayName)
        : this(controller, channel, channel, serialNumber, deviceDisplayName)
    {
        _officialBdcController = controller;
        _officialBdcChannel = channel;
    }

    public KinesisLegacyConnection(VerticalStage stage, string serialNumber, string deviceDisplayName)
        : this(stage, stage, stage, serialNumber, deviceDisplayName)
    {
        _officialVerticalStage = stage;
    }

    public void Enable()
    {
        if (_officialBdcChannel != null) { _officialBdcChannel.EnableDevice(); return; }
        if (_officialVerticalStage != null) { _officialVerticalStage.EnableDevice(); return; }
        Invoke(_motionTarget, "EnableDevice");
    }

    public void Home()
    {
        if (_officialBdcChannel != null) { _officialBdcChannel.Home(60000); return; }
        if (_officialVerticalStage != null) { _officialVerticalStage.Home(60000); return; }
        try
        {
            Invoke(_motionTarget, "Home", 60000);
        }
        catch (MissingMethodException)
        {
            Invoke(_motionTarget, "Home");
        }
    }

    public void MoveTo(decimal position)
    {
        if (_officialBdcChannel != null) { _officialBdcChannel.MoveTo(position, 60000); return; }
        if (_officialVerticalStage != null) { _officialVerticalStage.MoveTo(position, 60000); return; }
        try
        {
            Invoke(_motionTarget, "MoveTo", position, 60000);
        }
        catch (MissingMethodException)
        {
            Invoke(_motionTarget, "MoveTo", position);
        }
    }

    public bool TryMoveRelative(decimal distance, ILogger logger)
    {
        try
        {
            if (_officialBdcChannel != null)
            {
                _officialBdcChannel.MoveRelative(distance >= 0 ? MotorDirection.Forward : MotorDirection.Backward, Math.Abs(distance), 60000);
                return true;
            }
            if (_officialVerticalStage != null)
            {
                _officialVerticalStage.MoveRelative(distance >= 0 ? MotorDirection.Forward : MotorDirection.Backward, Math.Abs(distance), 60000);
                return true;
            }
            if (TryInvokeIfExists(_motionTarget, "MoveRelative", distance, 60000)
                || TryInvokeIfExists(_motionTarget, "MoveRelative", distance)
                || TryInvokeIfExists(_motionTarget, "MoveBy", distance, 60000)
                || TryInvokeIfExists(_motionTarget, "MoveBy", distance))
            {
                return true;
            }

            var configured = TryInvokeIfExists(_motionTarget, "SetMoveRelativeDistance", distance)
                || TryInvokeIfExists(_motionTarget, "SetRelativeMoveDistance", distance)
                || TrySetProperty(_motionTarget, "MoveRelativeDistance", distance)
                || TrySetProperty(_motionTarget, "RelativeMoveDistance", distance);

            if (!configured)
            {
                return false;
            }

            return TryInvokeIfExists(_motionTarget, "MoveRelative", 60000)
                || TryInvokeIfExists(_motionTarget, "MoveRelative")
                || TryInvokeIfExists(_motionTarget, "MoveBy", 60000)
                || TryInvokeIfExists(_motionTarget, "MoveBy");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Relative move probe failed for {DeviceType}", _motionTarget.GetType().FullName);
            return false;
        }
    }

    public void Stop()
    {
        if (_officialBdcChannel != null) { _officialBdcChannel.StopImmediate(); return; }
        if (_officialVerticalStage != null) { _officialVerticalStage.StopImmediate(); return; }
        if (TryInvokeIfExists(_motionTarget, "StopImmediate"))
        {
            return;
        }

        if (!TryInvokeIfExists(_motionTarget, "StopProfiled"))
        {
            throw new MissingMethodException(_motionTarget.GetType().FullName, "StopImmediate");
        }
    }

    public double GetPosition()
    {
        if (_officialBdcChannel != null) return (double)_officialBdcChannel.Position;
        if (_officialVerticalStage != null) return (double)_officialVerticalStage.Position;

        var raw = GetProperty(_motionTarget, "Position");
        if (raw == null) return 0;

        if (raw is decimal d) return (double)d;
        if (raw is double db) return db;
        if (raw is float f) return f;

        if (double.TryParse(raw.ToString(), out var parsed)) return parsed;
        return 0;
    }

    public string BuildMotionLimitSnapshot()
    {
        var parts = new List<string>
        {
            $"motionType={_motionTarget.GetType().FullName}",
            $"position={GetPosition():F6}"
        };

        if (TryReadVelocityLimits(out var maxVel, out var maxAcc))
        {
            parts.Add($"maxVel={maxVel:F3}");
            parts.Add($"maxAcc={maxAcc:F3}");
        }

        AddIfPresent(parts, "softMode", TryReadScalar(
            () => Invoke(_motionTarget, "GetSoftLimitMode"),
            () => Invoke(_motionTarget, "GetLimitsSoftwareApproachPolicy"),
            () => GetProperty(_motionTarget, "SoftLimitMode"),
            () => GetProperty(_motionTarget, "LimitsSoftwareApproachPolicy")));

        AddIfPresent(parts, "minPos", TryReadScalar(
            () => Invoke(_motionTarget, "GetStageAxisMinPos"),
            () => GetProperty(_motionTarget, "MinPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorDeviceSettings", "Physical", "MinPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorConfiguration", "Physical", "MinPos")));

        AddIfPresent(parts, "maxPos", TryReadScalar(
            () => Invoke(_motionTarget, "GetStageAxisMaxPos"),
            () => GetProperty(_motionTarget, "MaxPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorDeviceSettings", "Physical", "MaxPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorConfiguration", "Physical", "MaxPos")));

        AddIfPresent(parts, "ccwSoft", TryReadScalar(
            () => GetProperty(_motionTarget, "CCWSoftLimit"),
            () => GetNestedPropertyValue(_motionTarget, "MotorDeviceSettings", "Limits", "CCWSoftLimit"),
            () => GetNestedPropertyValue(_motionTarget, "MotorConfiguration", "Limits", "CCWSoftLimit")));

        AddIfPresent(parts, "cwSoft", TryReadScalar(
            () => GetProperty(_motionTarget, "CWSoftLimit"),
            () => GetNestedPropertyValue(_motionTarget, "MotorDeviceSettings", "Limits", "CWSoftLimit"),
            () => GetNestedPropertyValue(_motionTarget, "MotorConfiguration", "Limits", "CWSoftLimit")));

        return string.Join(", ", parts);
    }

    public bool TryGetTravelBounds(out double minPos, out double maxPos)
    {
        minPos = double.NaN;
        maxPos = double.NaN;

        var minRaw = TryReadScalar(
            () => Invoke(_motionTarget, "GetStageAxisMinPos"),
            () => GetProperty(_motionTarget, "MinPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorDeviceSettings", "Physical", "MinPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorConfiguration", "Physical", "MinPos"));

        var maxRaw = TryReadScalar(
            () => Invoke(_motionTarget, "GetStageAxisMaxPos"),
            () => GetProperty(_motionTarget, "MaxPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorDeviceSettings", "Physical", "MaxPos"),
            () => GetNestedPropertyValue(_motionTarget, "MotorConfiguration", "Physical", "MaxPos"));

        var hasMin = TryConvertToDouble(minRaw, out minPos);
        var hasMax = TryConvertToDouble(maxRaw, out maxPos);
        return hasMin && hasMax;
    }

    public void TryNormalizeActuatorNegativeRange(ILogger logger)
    {
        var name = DeviceDisplayName ?? string.Empty;
        var shouldAdjust = name.Contains("KDC", StringComparison.OrdinalIgnoreCase)
            || name.Contains("M30", StringComparison.OrdinalIgnoreCase);

        if (!shouldAdjust)
        {
            return;
        }

        if (!TryGetTravelBounds(out var minPos, out var maxPos))
        {
            logger.LogWarning("无法读取设备 {Device} 的行程范围；参数快照={Snapshot}", DeviceDisplayName, BuildMotionLimitSnapshot());
            return;
        }

        // Only auto-fix suspicious non-negative ranges on actuator-like devices.
        if (minPos < -1e-6)
        {
            return;
        }

        if (maxPos <= 0 || maxPos > 20)
        {
            logger.LogInformation("设备 {Device} 的范围无需自动修正：最小值={Min:F6}，最大值={Max:F6}", DeviceDisplayName, minPos, maxPos);
            return;
        }

        var newMin = -Math.Min(15.0, Math.Max(1.0, maxPos));
        var newMax = maxPos;

        var setOk = TrySetTravelBounds(newMin, newMax);
        if (!setOk)
        {
            logger.LogWarning(
                "自动修正设备 {Device} 的范围失败：原范围=[{OldMin:F6}, {OldMax:F6}]，目标范围=[{NewMin:F6}, {NewMax:F6}]，参数快照={Snapshot}",
                DeviceDisplayName,
                minPos,
                maxPos,
                newMin,
                newMax,
                BuildMotionLimitSnapshot());
            return;
        }

        TrySetSoftwareApproachPolicy(1);

        if (TryGetTravelBounds(out var appliedMin, out var appliedMax))
        {
            logger.LogInformation(
                "设备 {Device} 的范围已自动修正：[{OldMin:F6}, {OldMax:F6}] -> [{NewMin:F6}, {NewMax:F6}]，参数快照={Snapshot}",
                DeviceDisplayName,
                minPos,
                maxPos,
                appliedMin,
                appliedMax,
                BuildMotionLimitSnapshot());
        }
        else
        {
            logger.LogInformation(
                "设备 {Device} 的范围已自动修正：[{OldMin:F6}, {OldMax:F6}] -> 目标 [{NewMin:F6}, {NewMax:F6}]（无法回读）",
                DeviceDisplayName,
                minPos,
                maxPos,
                newMin,
                newMax);
        }
    }

    public void TryApplyMotionLimits(double maxVelocityMmPerSec, double maxAccelerationMmPerSec2, ILogger logger)
    {
        try
        {
            var targetVel = maxVelocityMmPerSec;
            var targetAcc = maxAccelerationMmPerSec2;

            if (TryReadVelocityLimits(out var existingVel, out var existingAcc))
            {
                // Only cap ceilings; never increase limits above current effective settings.
                if (existingVel > 0)
                {
                    targetVel = Math.Min(targetVel, existingVel);
                }

                if (existingAcc > 0)
                {
                    targetAcc = Math.Min(targetAcc, existingAcc);
                }
            }

            var applied = false;
            if (TryApplyViaParameterObject(targetVel, targetAcc))
            {
                applied = true;
            }

            if (!applied && TryApplyViaTwoArgSetter(targetVel, targetAcc))
            {
                applied = true;
            }

            if (!applied)
            {
                logger.LogDebug("Legacy stage API does not expose writable velocity/acceleration limits on this model.");
                return;
            }

            if (TryReadVelocityLimits(out var finalVel, out var finalAcc))
            {
                var velExceeded = finalVel > maxVelocityMmPerSec + 1e-6;
                var accExceeded = finalAcc > maxAccelerationMmPerSec2 + 1e-6;

                if (velExceeded || accExceeded)
                {
                    _ = TryApplyViaParameterObject(maxVelocityMmPerSec, maxAccelerationMmPerSec2)
                        || TryApplyViaTwoArgSetter(maxVelocityMmPerSec, maxAccelerationMmPerSec2);

                    if (TryReadVelocityLimits(out finalVel, out finalAcc))
                    {
                        velExceeded = finalVel > maxVelocityMmPerSec + 1e-6;
                        accExceeded = finalAcc > maxAccelerationMmPerSec2 + 1e-6;
                    }
                }

                logger.LogInformation(
                    "Kinesis 位移设备运动限制已生效：最大速度={Vel:F3} mm/s，最大加速度={Acc:F3} mm/s^2",
                    finalVel,
                    finalAcc);

                if (velExceeded || accExceeded)
                {
                    logger.LogWarning(
                        "当前型号无法应用请求的运动上限：请求不超过（{ReqVel:F3}, {ReqAcc:F3}），回读为（{CurVel:F3}, {CurAcc:F3}）",
                        maxVelocityMmPerSec,
                        maxAccelerationMmPerSec2,
                        finalVel,
                        finalAcc);
                }

                return;
            }

            logger.LogInformation(
                "已请求应用 Kinesis 位移设备运动限制：最大速度<={Vel:F3} mm/s，最大加速度<={Acc:F3} mm/s^2（无法回读）",
                maxVelocityMmPerSec,
                maxAccelerationMmPerSec2);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Applying legacy stage limits failed.");
        }
    }

    public void Dispose()
    {
        if (_officialBdcChannel != null && _officialBdcController != null)
        {
            try { _officialBdcChannel.StopPolling(); } catch { }
            try { _officialBdcController.Disconnect(true); } catch { }
            _officialBdcController.Dispose();
            return;
        }
        if (_officialVerticalStage != null)
        {
            try { _officialVerticalStage.StopPolling(); } catch { }
            try { _officialVerticalStage.Disconnect(true); } catch { }
            _officialVerticalStage.Dispose();
            return;
        }

        // Reflection is retained only for the unused legacy KDC diagnostic path.
        TryInvoke(_pollingTarget, "StopPolling");
        TryInvoke(_motionTarget, "StopPolling");

        // Disconnect both channel-level and device-level targets as best effort.
        TryInvoke(_motionTarget, "Disconnect", true);
        TryInvoke(_motionTarget, "Disconnect");
        TryInvoke(_disconnectTarget, "Disconnect", true);
        TryInvoke(_disconnectTarget, "Disconnect");

        if (!ReferenceEquals(_motionTarget, _pollingTarget))
        {
            TryInvoke(_pollingTarget, "Disconnect", true);
            TryInvoke(_pollingTarget, "Disconnect");
        }

        if (_disconnectTarget is IDisposable d1)
        {
            try { d1.Dispose(); } catch { }
        }

        if (!ReferenceEquals(_motionTarget, _disconnectTarget) && _motionTarget is IDisposable d2)
        {
            try { d2.Dispose(); } catch { }
        }

        if (!ReferenceEquals(_pollingTarget, _disconnectTarget)
            && !ReferenceEquals(_pollingTarget, _motionTarget)
            && _pollingTarget is IDisposable d3)
        {
            try { d3.Dispose(); } catch { }
        }
    }

    private bool TryApplyViaParameterObject(double maxVelocityMmPerSec, double maxAccelerationMmPerSec2)
    {
        if (!TryGetVelocityParamsObject(out var velParams, out var vpType))
        {
            return false;
        }

        var maxVelProp = vpType.GetProperty("MaxVelocity", BindingFlags.Public | BindingFlags.Instance);
        var accProp = vpType.GetProperty("Acceleration", BindingFlags.Public | BindingFlags.Instance)
            ?? vpType.GetProperty("MaxAcceleration", BindingFlags.Public | BindingFlags.Instance);

        var changed = false;
        if (maxVelProp?.CanWrite == true)
        {
            maxVelProp.SetValue(velParams, ConvertTo(maxVelocityMmPerSec, maxVelProp.PropertyType));
            changed = true;
        }

        if (accProp?.CanWrite == true)
        {
            accProp.SetValue(velParams, ConvertTo(maxAccelerationMmPerSec2, accProp.PropertyType));
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        var setMethod = _motionTarget.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name is "SetVelocityParams" or "SetVelParams"
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.IsAssignableFrom(vpType));
        if (setMethod == null)
        {
            return false;
        }

        setMethod.Invoke(_motionTarget, new[] { velParams });
        return true;
    }

    private bool TryApplyViaTwoArgSetter(double maxVelocityMmPerSec, double maxAccelerationMmPerSec2)
    {
        var setMethod = _motionTarget.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name is "SetVelocityParams" or "SetVelParams"
                && m.GetParameters().Length == 2);
        if (setMethod == null)
        {
            return false;
        }

        var p = setMethod.GetParameters();
        var args = new[]
        {
            ConvertTo(maxVelocityMmPerSec, p[0].ParameterType),
            ConvertTo(maxAccelerationMmPerSec2, p[1].ParameterType)
        };
        setMethod.Invoke(_motionTarget, args);
        return true;
    }

    private bool TryReadVelocityLimits(out double maxVelocityMmPerSec, out double maxAccelerationMmPerSec2)
    {
        maxVelocityMmPerSec = double.NaN;
        maxAccelerationMmPerSec2 = double.NaN;

        if (!TryGetVelocityParamsObject(out var velParams, out var vpType))
        {
            return false;
        }

        var maxVelRaw = vpType.GetProperty("MaxVelocity", BindingFlags.Public | BindingFlags.Instance)?.GetValue(velParams)
            ?? vpType.GetProperty("Velocity", BindingFlags.Public | BindingFlags.Instance)?.GetValue(velParams);

        var maxAccRaw = vpType.GetProperty("Acceleration", BindingFlags.Public | BindingFlags.Instance)?.GetValue(velParams)
            ?? vpType.GetProperty("MaxAcceleration", BindingFlags.Public | BindingFlags.Instance)?.GetValue(velParams);

        var hasVel = TryConvertToDouble(maxVelRaw, out maxVelocityMmPerSec);
        var hasAcc = TryConvertToDouble(maxAccRaw, out maxAccelerationMmPerSec2);
        return hasVel && hasAcc;
    }

    private bool TryGetVelocityParamsObject(out object velParams, out Type velParamsType)
    {
        velParams = default!;
        velParamsType = default!;

        var getMethod = _motionTarget.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name is "GetVelocityParams" or "GetVelParams"
                && (m.GetParameters().Length == 0 || m.GetParameters().Length == 1));
        if (getMethod == null)
        {
            return false;
        }

        object? fetched;
        var getArgs = getMethod.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { 60000 };
        try
        {
            fetched = getMethod.Invoke(_motionTarget, getArgs);
        }
        catch
        {
            return false;
        }

        if (fetched == null)
        {
            return false;
        }

        velParams = fetched;
        velParamsType = fetched.GetType();
        return true;
    }

    private static object ConvertTo(double value, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t == typeof(decimal)) return (decimal)value;
        if (t == typeof(float)) return (float)value;
        if (t == typeof(int)) return (int)Math.Round(value);
        if (t == typeof(long)) return (long)Math.Round(value);
        if (t == typeof(short)) return (short)Math.Round(value);
        if (t == typeof(double)) return value;
        return Convert.ChangeType(value, t);
    }

    private static object? Invoke(object target, string method, params object[] args)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.Name == method)
            .ToArray();

        foreach (var candidate in methods)
        {
            if (!TryPrepareArguments(candidate.GetParameters(), args, out var prepared))
            {
                continue;
            }

            return candidate.Invoke(target, prepared);
        }

        if (methods.Length == 0)
            throw new MissingMethodException(target.GetType().FullName, method);

        throw new MissingMethodException($"No compatible overload for {target.GetType().FullName}.{method} with {args.Length} argument(s)");

    }

    private static object? GetNestedPropertyValue(object root, params string[] propertyPath)
    {
        object? current = root;
        foreach (var name in propertyPath)
        {
            if (current == null)
            {
                return null;
            }

            current = GetProperty(current, name);
        }

        return current;
    }

    private static object? TryReadScalar(params Func<object?>[] readers)
    {
        foreach (var reader in readers)
        {
            try
            {
                var value = reader();
                if (value != null)
                {
                    return value;
                }
            }
            catch
            {
                // ignore and keep probing
            }
        }

        return null;
    }

    private static void AddIfPresent(List<string> parts, string key, object? value)
    {
        if (value == null)
        {
            return;
        }

        parts.Add($"{key}={value}");
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        result = double.NaN;
        if (value == null)
        {
            return false;
        }

        switch (value)
        {
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
        }

        return double.TryParse(value.ToString(), out result);
    }

    private bool TrySetTravelBounds(double minPos, double maxPos)
    {
        // CLI wrappers usually expose one of these APIs.
        if (TryInvokeIfExists(_motionTarget, "SetMotorTravelLimits", minPos, maxPos)
            || TryInvokeIfExists(_motionTarget, "SetMotorTravelLimits", (decimal)minPos, (decimal)maxPos))
        {
            return true;
        }

        var minInt = (int)Math.Round(minPos);
        var maxInt = (int)Math.Round(maxPos);

        return TryInvokeIfExists(_motionTarget, "SetStageAxisLimits", minInt, maxInt)
            || TryInvokeIfExists(_motionTarget, "SetStageAxisLimits", minPos, maxPos)
            || TryInvokeIfExists(_motionTarget, "SetStageAxisLimits", (decimal)minPos, (decimal)maxPos);
    }

    private bool TrySetSoftwareApproachPolicy(int policy)
    {
        return TryInvokeIfExists(_motionTarget, "SetLimitsSoftwareApproachPolicy", policy)
            || TrySetProperty(_motionTarget, "LimitsSoftwareApproachPolicy", policy)
            || TrySetProperty(_motionTarget, "SoftLimitMode", policy);
    }

    private static bool TryPrepareArguments(ParameterInfo[] parameters, object[] args, out object?[] prepared)
    {
        prepared = Array.Empty<object?>();
        if (parameters.Length != args.Length)
        {
            return false;
        }

        var buffer = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            if (parameterType.IsByRef)
            {
                parameterType = parameterType.GetElementType() ?? parameterType;
            }

            var arg = args[i];
            if (arg == null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                {
                    return false;
                }

                buffer[i] = null;
                continue;
            }

            if (parameterType.IsInstanceOfType(arg))
            {
                buffer[i] = arg;
                continue;
            }

            try
            {
                if (parameterType.IsEnum)
                {
                    buffer[i] = arg is string s
                        ? Enum.Parse(parameterType, s, ignoreCase: true)
                        : Enum.ToObject(parameterType, arg);
                }
                else
                {
                    buffer[i] = Convert.ChangeType(arg, parameterType);
                }
            }
            catch
            {
                return false;
            }
        }

        prepared = buffer;
        return true;

    }

    private static void TryInvoke(object target, string method, params object[] args)
    {
        try
        {
            _ = Invoke(target, method, args);
        }
        catch
        {
            // best effort
        }
    }

    private static bool TryInvokeIfExists(object target, string method, params object[] args)
    {
        try
        {
            _ = Invoke(target, method, args);
            return true;
        }
        catch (MissingMethodException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetProperty(object target, string propertyName, object value)
    {
        try
        {
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.CanWrite != true)
            {
                return false;
            }

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object converted;
            if (propType.IsEnum)
            {
                converted = value is string s
                    ? Enum.Parse(propType, s, ignoreCase: true)
                    : Enum.ToObject(propType, value);
            }
            else
            {
                converted = Convert.ChangeType(value, propType);
            }

            prop.SetValue(target, converted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? GetProperty(object target, string name)
    {
        return target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }
}
