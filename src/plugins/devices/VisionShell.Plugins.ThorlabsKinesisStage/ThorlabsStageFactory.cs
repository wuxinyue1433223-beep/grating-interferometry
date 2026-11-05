// 鏂囦欢鑱岃矗锛歍horlabs 浣嶇Щ鍙板伐鍘傦紝璐熻矗璁惧鍙戠幇涓庡疄渚嬫瀯寤恒€?

using Microsoft.Extensions.Logging;
using Thorlabs.MotionControl.XA;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.ThorlabsKinesisStage;

// 绫诲瀷鑱岃矗锛歍horlabs 宸ュ巶锛屽彂鐜拌澶囧苟鍒涘缓浣嶇Щ鍙板璞°€?
public sealed class ThorlabsStageFactory : IDeviceFactory, IDeviceDescriptorProvider
{
 private readonly ILoggerFactory _loggerFactory;
 private readonly ILogger<ThorlabsStageFactory> _logger;
 private readonly List<IDeviceDescriptor> _descriptors = new();

 // 绫诲瀷鑱岃矗锛歍horlabs 璁惧鎻忚堪璁板綍锛岀敤浜庢槧灏勭晫闈㈠睍绀轰笌鍒涘缓銆?
 private record Descriptor(string DescriptorId, string DisplayName, string DeviceTypeIdentifier, string FactoryType, bool IsCloneable) : IDeviceDescriptor;

 public ThorlabsStageFactory(ILoggerFactory loggerFactory)
 {
 _loggerFactory = loggerFactory;
 _logger = loggerFactory.CreateLogger<ThorlabsStageFactory>();
 }

 public string DeviceType => "thorlabs-xa-stage";
 public string DisplayName => "Thorlabs XA Stage";
 public bool IsCloneable => true;

 public IEnumerable<IDeviceDescriptor> GetDescriptors() => _descriptors;

 public async Task<bool> DiscoverAsync(CancellationToken token = default)
 {
     var discovered = await Task.Run(() =>
     {
         token.ThrowIfCancellationRequested();
         using var sm = SystemManager.Create();
         try
         {
             sm.Startup();
             var apiVersion = sm.GetApiVersion();
             var list = sm.GetDeviceList();
             var descriptorsById = new Dictionary<string, IDeviceDescriptor>(StringComparer.OrdinalIgnoreCase);
             foreach (var di in list)
             {
                 token.ThrowIfCancellationRequested();
                 var openId = GetDeviceInfoOpenId(di);
                 if (string.IsNullOrWhiteSpace(openId)) continue;

                 var pn = SafeGet(di, "PartNumber");
                 var dtd = SafeGet(di, "DeviceTypeDescription");
                 var display = !string.IsNullOrWhiteSpace(pn) && !string.IsNullOrWhiteSpace(dtd)
                     ? $"{pn} - {dtd}"
                     : (!string.IsNullOrWhiteSpace(pn) ? pn : (!string.IsNullOrWhiteSpace(dtd) ? dtd : "Thorlabs Stage"));

                 descriptorsById[openId] = new Descriptor(openId, display, "IStage", DeviceType, false);
             }

             _logger.LogInformation(
                 "XA 设备发现完成：API={Major}.{Minor}.{Patch}+{Build}，数量={Count}，设备编号={DeviceIds}",
                 apiVersion.Major,
                 apiVersion.Minor,
                 apiVersion.Patch,
                 apiVersion.Build,
                 descriptorsById.Count,
                 string.Join(", ", descriptorsById.Keys));

             foreach (var deviceInfo in list)
             {
                 _logger.LogInformation(
                     "XA 设备详情：编号={Id}，型号={PartNumber}，类型={Description}，传输={Transport}，父设备={Parent}",
                     GetDeviceInfoOpenId(deviceInfo),
                     SafeGet(deviceInfo, "PartNumber"),
                     SafeGet(deviceInfo, "DeviceTypeDescription"),
                     SafeGet(deviceInfo, "Transport"),
                     SafeGet(deviceInfo, "ParentDevice"));
             }

             return descriptorsById.Values.ToList();
         }
         finally
         {
             try { sm.Shutdown(); } catch { }
         }
     }, token);

     _descriptors.Clear();
     _descriptors.AddRange(discovered);
     return _descriptors.Count > 0;
 }
 public VisionShell.Core.Interfaces.IDevice CreateDevice(ILogger logger)
 {
 var l = _loggerFactory.CreateLogger<ThorlabsStage>();
 return new ThorlabsStage(l);
 }

 private static string GetDeviceInfoOpenId(DeviceInfo deviceInfo)
 {
 // Prefer Device (typical in XA samples), then DeviceId/SerialNumber as fallback
 var props = new[] { "Device", "DeviceId", "SerialNumber" };
 foreach (var p in props)
 {
 try
 {
 var val = deviceInfo.GetType().GetProperty(p)?.GetValue(deviceInfo)?.ToString();
 if (!string.IsNullOrWhiteSpace(val)) return val;
 }
 catch { }
 }
 return deviceInfo.ToString() ?? string.Empty;
 }

 private static string? SafeGet(object obj, string prop)
 {
 try { return obj.GetType().GetProperty(prop)?.GetValue(obj)?.ToString(); }
 catch { return null; }
 }
}

