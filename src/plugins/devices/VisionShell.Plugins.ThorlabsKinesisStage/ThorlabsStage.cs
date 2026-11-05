// 文件职责：Thorlabs Kinesis 位移台驱动实现，封装 SDK 连接与运动控制。

using Microsoft.Extensions.Logging;
using Thorlabs.MotionControl.XA;
using Thorlabs.MotionControl.XA.DeviceFeatures;
using VisionShell.Core.Interfaces;
using XaTimeout = Thorlabs.MotionControl.XA.Timeout;

namespace VisionShell.Plugins.ThorlabsKinesisStage;

// 类型职责：Thorlabs 位移台实现，封装连接、移动与状态查询。
public sealed class ThorlabsStage : IStage, IHomingStatus
{
 private static readonly object SystemManagerLock = new();
 private static readonly SemaphoreSlim SystemManagerConnectGate = new(1, 1);
 private static SystemManager? _systemManager;
 private static int _systemManagerRefCount;
 private const int OpenRetryCount = 3;
 private const int OpenRetryDelayMs = 750;

 private readonly ILogger _logger;
 private readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(10);
 private readonly TimeSpan _homeTimeout = TimeSpan.FromSeconds(120);
 private readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(10);

 private Thorlabs.MotionControl.XA.IDevice? _device;
 private bool _disposed;
 private bool _systemManagerLeaseHeld;
 private bool _homeCompletedThisConnection;
 private readonly SemaphoreSlim _ioGate = new(1,1);

 // These identify the particular physical device instance.
 // In a final implementation, DeviceId should be the device serial / unique hardware id.
 public string DeviceId { get; private set; }
 public string Name { get; private set; }

 // Mapping/open identifier (DeviceInfo.Device) stored in DeviceId.
 // The SDK-reported device id after opening is kept separately.
 public string? SdkDeviceId { get; private set; }

 private const double DefaultMaxVelocityMmPerSec =0.5;
 private const double DefaultMaxAccelerationMmPerSec2 =0.5;

 /// <summary>
 /// Set the XA open identifier (DeviceInfo.Device) before ConnectAsync.
 /// </summary>
 public void SetOpenDeviceId(string deviceId)
 {
 if (State != DeviceState.Disconnected)
 throw new InvalidOperationException("Cannot change device id while connected");
 DeviceId = deviceId ?? string.Empty;
 }

 public bool IsCloneable => true;
 public DeviceState State { get; private set; }
 public bool IsEnabled { get; private set; }
 public bool IsHomed => _homeCompletedThisConnection || ReadHomedState();

 // Position in mm (consistent with other stages and UI)
 public double Position { get; private set; }

 public event EventHandler<DeviceState>? StateChanged;
 public event EventHandler<string>? ErrorOccurred;

 public ThorlabsStage(ILogger logger, string? deviceId = null, string? name = null)
 {
 _logger = logger;
 DeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId;
 Name = string.IsNullOrWhiteSpace(name) ? "Thorlabs Stage" : name;
 State = DeviceState.Disconnected;
 }

 public async Task ConnectAsync(CancellationToken token = default)
 {
 ThrowIfDisposed();

 await _ioGate.WaitAsync(token);
 try
 {
 await SystemManagerConnectGate.WaitAsync(token);
 try
 {
 await Task.Run(() =>
 {
 bool systemManagerAcquired = false;
 try
 {
 EnsureSystemManagerStarted();
 systemManagerAcquired = true;

 var list = _systemManager!.GetDeviceList();
 if (list == null || list.Count ==0)
 throw new InvalidOperationException("No Thorlabs XA devices found.");

 // Determine which ID to open:
 // XA sample code shows: device.DeviceId is the string identifier; and SystemManager.TryOpenDevice takes that string.
 // So the mapping value should be the XA DeviceId string.
 string idToOpen = DeviceId;

 if (string.IsNullOrWhiteSpace(idToOpen))
 {
 // If not specified, default to first device.
 idToOpen = GetDeviceId(list[0]);
            _logger.LogWarning("未指定 XA 设备编号，默认使用首个发现的设备：{Id}", idToOpen);
 }

 Thorlabs.MotionControl.XA.IDevice? opened = null;
 for (var attempt = 1; attempt <= OpenRetryCount; attempt++)
 {
 if (_systemManager.TryOpenDevice(idToOpen, string.Empty, OperatingModes.Default, out var candidate))
 {
 opened = candidate;
 break;
 }

 if (attempt < OpenRetryCount)
 {
 _logger.LogWarning(
 "XA 设备 {Id} 第 {Attempt}/{MaxAttempt} 次打开未就绪，等待 {DelayMs} 毫秒后重试",
 idToOpen,
 attempt,
 OpenRetryCount,
 OpenRetryDelayMs);
 Thread.Sleep(OpenRetryDelayMs);
 }
 }

 if (opened == null)
 throw new InvalidOperationException($"连续 {OpenRetryCount} 次尝试后仍无法打开 XA 设备 {idToOpen}");

 _device = opened;
 _homeCompletedThisConnection = false;

 // Set default max velocity/acceleration if supported
 try
 {
 if (_device is IVelocityParams vp)
 {
 var p = vp.GetVelocityParams(_commandTimeout);
 // Distance units are device units; convert desired mm/s to device units using IUnitConverter
 if (_device is IUnitConverter uc)
 {
 // Determine which velocity unit the converter uses
 var velInfo = uc.FromDeviceUnitToPhysical(ScaleType.Velocity, p.MaxVelocity);
 var unitType = velInfo?.UnitType ?? Unit.Millimetres;
 var maxVelDev = uc.FromPhysicalToDeviceUnit(ScaleType.Velocity, unitType, DefaultMaxVelocityMmPerSec);
 p.MaxVelocity = (int)maxVelDev;

 // Best effort: not all models expose writable acceleration in velocity params.
 var accProp = p.GetType().GetProperty("Acceleration") ?? p.GetType().GetProperty("MaxAcceleration");
 if (accProp != null && accProp.CanWrite)
 {
 try
 {
 var accInfo = uc.FromDeviceUnitToPhysical(ScaleType.Acceleration, Convert.ToInt64(accProp.GetValue(p) ?? 0));
 var accUnitType = accInfo?.UnitType ?? Unit.Millimetres;
 var maxAccDev = uc.FromPhysicalToDeviceUnit(ScaleType.Acceleration, accUnitType, DefaultMaxAccelerationMmPerSec2);
 if (accProp.PropertyType == typeof(int))
 {
 accProp.SetValue(p, (int)maxAccDev);
 }
 else if (accProp.PropertyType == typeof(long))
 {
 accProp.SetValue(p, maxAccDev);
 }
 else
 {
 accProp.SetValue(p, Convert.ChangeType(maxAccDev, accProp.PropertyType));
 }
 }
 catch (Exception ex)
 {
            _logger.LogDebug(ex, "设置默认最大加速度失败");
 }
 }

 vp.SetVelocityParams(p);
 }
 }
 }
 catch (Exception ex)
 {
            _logger.LogDebug(ex, "设置默认最大速度失败");
 }

 // Preserve the open-id for mapping, but also capture the SDK's device id.
 SdkDeviceId = _device.DeviceId;

 // Show both ids to avoid confusion:
 var openId = idToOpen;
 var sdkId = string.IsNullOrWhiteSpace(SdkDeviceId) ? string.Empty : SdkDeviceId;
 var idText = string.IsNullOrWhiteSpace(sdkId) || string.Equals(openId, sdkId, StringComparison.OrdinalIgnoreCase)
 ? openId
 : $"{sdkId} / Open:{openId}";

 Name = string.IsNullOrWhiteSpace(_device.PartNumber)
 ? $"Thorlabs Stage (ID: {idText})"
 : $"{_device.PartNumber} (ID: {idText})";

 LogOfficialConnectedProductInfo();

 // Enable by default (feature expects stage can move)
 try
 {
 (_device as IEnableState)?.SetEnableState(EnableState.Enabled, _commandTimeout);
 IsEnabled = true;
 }
 catch (Exception ex)
 {
 IsEnabled = false;
            _logger.LogWarning(ex, "连接 XA 设备时启用设备失败");
 }

 // Prime position cache
 try { Position = ReadPositionMm(); }
 catch (Exception ex)
 {
            _logger.LogDebug(ex, "连接后初始化位置缓存失败");
 }

 State = DeviceState.Connected;
 _systemManagerLeaseHeld = true;
 StateChanged?.Invoke(this, State);
        _logger.LogInformation("XA 设备连接成功：{Name}", Name);
 }
 catch (Exception ex)
 {
 try { (_device as IDisconnect)?.Disconnect(); } catch { }
 try { _device?.Close(); } catch { }
 _device = null;
 _homeCompletedThisConnection = false;
 if (systemManagerAcquired)
 {
 ReleaseSystemManager();
 }
 State = DeviceState.Disconnected;
 StateChanged?.Invoke(this, State);
 ErrorOccurred?.Invoke(this, ex.Message);
 throw;
 }
 }, token);
 }
 finally
 {
 SystemManagerConnectGate.Release();
 }
 }
 finally
 {
 _ioGate.Release();
 }
 }

 public Task DisconnectAsync()
 {
 try
 {
 if (_device != null)
 {
        try { (_device as IStop)?.Stop(StopMode.Immediate, _stopTimeout); }
        catch (Exception ex) { _logger.LogDebug(ex, "断开连接时停止 XA 设备失败"); }
 try { (_device as IDisconnect)?.Disconnect(); }
        catch (Exception ex) { _logger.LogDebug(ex, "调用 XA 官方断开接口失败"); }
 try { _device.Close(); }
        catch (Exception ex) { _logger.LogDebug(ex, "调用 XA 官方关闭接口失败"); }
 }
 }
 finally
 {
 _device = null;
 _homeCompletedThisConnection = false;
 IsEnabled = false;
 State = DeviceState.Disconnected;
 StateChanged?.Invoke(this, State);
 if (_systemManagerLeaseHeld)
 {
 _systemManagerLeaseHeld = false;
 ReleaseSystemManager();
 }
 }

 return Task.CompletedTask;
 }

 public void Dispose()
 {
 if (_disposed) return;
 try
 {
 if (_device != null || _systemManagerLeaseHeld || State != DeviceState.Disconnected)
 {
 DisconnectAsync().GetAwaiter().GetResult();
 }
 }
 catch (Exception ex)
 {
            _logger.LogDebug(ex, "释放 XA 位移设备时断开连接失败");
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

 try
 {
 (_device as IEnableState)?.SetEnableState(state ? EnableState.Enabled : EnableState.Disabled, _commandTimeout);
 IsEnabled = state;
 }
 catch (Exception ex)
 {
 ErrorOccurred?.Invoke(this, ex.Message);
 throw;
 }

 return Task.CompletedTask;
 }

 public async Task HomeAsync()
 {
 ThrowIfDisposed();
 EnsureConnected();
 if (!IsEnabled) await EnableAsync(true);

 await _ioGate.WaitAsync();
 try
 {
 try
 {
 State = DeviceState.Busy;
 StateChanged?.Invoke(this, State);

 if (_device is not IHome home)
throw new NotSupportedException("XA 设备未提供官方 IHome 接口");

 var homeTask = Task.Run(() => home.Home(_homeTimeout));
 while (!homeTask.IsCompleted)
 {
 TryRefreshPositionCache();
 await Task.Delay(80);
 }

 await homeTask;
 _homeCompletedThisConnection = true;

 var homedStatusConfirmed = false;
 for (var attempt = 0; attempt < 20; attempt++)
 {
 try
 {
 if (ReadHomedState())
 {
 homedStatusConfirmed = true;
 break;
 }
 }
 catch (Exception ex)
 {
 _logger.LogDebug(ex, "XA Home 完成后读取 Homed 状态失败：{DeviceId}", DeviceId);
 break;
 }

 await Task.Delay(100);
 }

 if (!homedStatusConfirmed)
 {
 _logger.LogWarning("XA Home 官方命令已成功返回，但 Homed 状态项在 2 秒内尚未同步：{DeviceId}", DeviceId);
 }

 // Refresh position after homing
 Position = ReadPositionMm();
 State = DeviceState.Connected;
 StateChanged?.Invoke(this, State);
 }
 catch (Exception ex)
 {
 State = DeviceState.Connected;
 StateChanged?.Invoke(this, State);
 ErrorOccurred?.Invoke(this, ex.Message);
 throw;
 }
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
 if (!IsEnabled) await EnableAsync(true);

 await _ioGate.WaitAsync();
 try
 {
 var move = _device as IMove;
 if (move == null) throw new NotSupportedException("Device does not support IMove");

 State = DeviceState.Busy;
 StateChanged?.Invoke(this, State);

 var moveTask = Task.Run(() =>
 {
 if (mode == DefaultMoveMode.Absolute)
 {
 var targetDev = ToDeviceUnitsDistance(value);
 move.Move(MoveMode.Absolute, (int)targetDev, _commandTimeout);
 }
 else
 {
 var deltaDev = ToDeviceUnitsDistance(value);
 move.Move(MoveMode.RelativeMove, (int)deltaDev, _commandTimeout);
 }
 });

 while (!moveTask.IsCompleted)
 {
 TryRefreshPositionCache();
 await Task.Delay(80);
 }

 await moveTask;
 TryRefreshPositionCache();
 State = DeviceState.Connected;
 StateChanged?.Invoke(this, State);
 }
 catch (Exception ex)
 {
 State = DeviceState.Connected;
 StateChanged?.Invoke(this, State);
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

 // XA exposes an immediate stop specifically for an in-flight move. Keep it out of the normal command queue.
 var stop = _device as IStop;
 try
 {
 await Task.Run(() => stop?.Stop(StopMode.Immediate, _stopTimeout));
 }
 finally
 {
 State = DeviceState.Connected;
 StateChanged?.Invoke(this, State);
 }
 }

 public async Task<double> GetPositionAsync()
 {
 ThrowIfDisposed();
 EnsureConnected();

 // MoveAsync refreshes this cache every 80 ms using XA IStatusItems. Do not queue
 // a second command behind an in-flight move just to update the UI.
 if (State == DeviceState.Busy)
 {
 return Position;
 }

 await _ioGate.WaitAsync();
 try
 {
 Position = await Task.Run(ReadPositionMm);
 return Position;
 }
 finally
 {
 _ioGate.Release();
 }
 }
 private static void EnsureSystemManagerStarted()
 {
 lock (SystemManagerLock)
 {
 if (_systemManager == null)
 {
 _systemManager = SystemManager.Create();
 _systemManager.Startup();
 _systemManagerRefCount =0;
 }
 _systemManagerRefCount++;
 }
 }

 private static void ReleaseSystemManager()
 {
 lock (SystemManagerLock)
 {
 if (_systemManager == null) return;
 _systemManagerRefCount = Math.Max(0, _systemManagerRefCount -1);
 if (_systemManagerRefCount ==0)
 {
 try { _systemManager.Shutdown(); } catch { }
 try { _systemManager.Dispose(); } catch { }
 _systemManager = null;
 }
 }
 }

 private void EnsureConnected()
 {
 if (_device == null) throw new InvalidOperationException("Stage is not connected");
 }

 private void ThrowIfDisposed()
 {
 if (_disposed) throw new ObjectDisposedException(nameof(ThorlabsStage));
 }

 private long ToDeviceUnitsDistance(double mm)
 {
 EnsureConnected();

 if (_device is not IUnitConverter converter)
 throw new NotSupportedException("The XA device does not expose the official unit converter.");

 return converter.FromPhysicalToDeviceUnit(ScaleType.Distance, Unit.Millimetres, mm);
 }

 private bool ReadHomedState()
 {
 EnsureConnected();

 if (_device is not IStatusItems statusItems)
 throw new NotSupportedException("The XA device does not expose the official status-items interface.");

 var item = statusItems.GetStatusItem(StatusItemId.Homed);
 if (item == null || !item.IsBoolean)
throw new NotSupportedException("XA 设备未将 Homed 作为布尔状态项提供");

 return item.GetBoolean();
 }

 private double ReadPositionMm()
 {
 EnsureConnected();

 // Prefer status items (fast)
 try
 {
 if (_device is IStatusItems s)
 {
 var item = s.GetStatusItem(StatusItemId.Position);
 if (item != null && item.IsInteger)
 {
 var devUnits = item.GetInteger();
 return FromDeviceUnitsDistance(devUnits);
 }
 }
 }
 catch
 {
 // ignore and fallback below
 }

 // Fallback: position counter
 try
 {
 if (_device is IPositionCounter pc)
 {
 var devUnits = pc.GetPositionCounter(_commandTimeout);
 return FromDeviceUnitsDistance(devUnits);
 }
 }
 catch
 {
 // ignore
 }

 throw new NotSupportedException("The XA device does not expose an official position readback interface.");
 }

 private void TryRefreshPositionCache()
 {
 try
 {
 Position = ReadPositionMm();
 }
 catch (Exception ex)
 {
                _logger.LogDebug(ex, "运动期间刷新 XA 位置缓存失败");
 }
 }

 private double FromDeviceUnitsDistance(long deviceUnits)
 {
 EnsureConnected();

 if (_device is not IUnitConverter converter)
 throw new NotSupportedException("The XA device does not expose the official unit converter.");

 var result = converter.FromDeviceUnitToPhysical(ScaleType.Distance, deviceUnits);
 if (result == null)
 throw new InvalidOperationException("The XA unit converter returned no distance result.");

 return result.Value;
 }

 private void LogOfficialConnectedProductInfo()
 {
 try
 {
 if (_device is IConnectedProduct connectedProduct)
 {
 var info = connectedProduct.GetConnectedProductInfo();
 _logger.LogInformation(
 "XA 已连接产品：设备 {DeviceId}，型号 {PartNumber}，名称 {ProductName}，单位 {Unit}，范围 {MinPosition:F6} 至 {MaxPosition:F6}，距离比例因子 {DistanceScaleFactor}",
 DeviceId,
 string.IsNullOrWhiteSpace(info.PartNumber) ? "未知" : info.PartNumber,
 string.IsNullOrWhiteSpace(info.ProductName) ? "未知" : info.ProductName,
 info.UnitType,
 info.MinPosition,
 info.MaxPosition,
 info.DistanceScaleFactor);
 }

 if (_device is IUnitConverter converter)
 {
 var zero = converter.FromDeviceUnitToPhysical(ScaleType.Distance, 0);
 var one = converter.FromDeviceUnitToPhysical(ScaleType.Distance, 1);
 if (zero != null && one != null && zero.UnitType == one.UnitType)
 {
 var quantum = Math.Abs(one.Value - zero.Value);
 if (one.UnitType == Unit.Millimetres)
 {
 _logger.LogInformation("XA 位置量化：设备 {DeviceId} 的 1 个设备计数对应 {QuantumUm:F6} 微米", DeviceId, quantum * 1000.0);
 }
 else if (one.UnitType == Unit.Micrometres)
 {
 _logger.LogInformation("XA 位置量化：设备 {DeviceId} 的 1 个设备计数对应 {QuantumUm:F6} 微米", DeviceId, quantum);
 }
 else
 {
 _logger.LogInformation("XA 位置量化：设备 {DeviceId} 的 1 个设备计数对应 {Quantum:G9} {Unit}", DeviceId, quantum, one.UnitType);
 }
 }
 }
 }
 catch (Exception ex)
 {
 _logger.LogDebug(ex, "读取 XA 官方连接产品及位置量化信息失败：{DeviceId}", DeviceId);
 }
 }

 private static string GetDeviceId(DeviceInfo deviceInfo)
 {
 // In XA samples, the identifier used to open a device is exposed as property 'Device'.
 try
 {
 var p = deviceInfo.GetType().GetProperty("Device");
 if (p != null) return p.GetValue(deviceInfo)?.ToString() ?? string.Empty;
 }
 catch { }

 return deviceInfo.ToString() ?? string.Empty;
 }
 public double ReadVelocityMmPerSec()
 {
 EnsureConnected();

 // Preferred: status items (fast)
 try
 {
 if (_device is IStatusItems s)
 {
 // Attempt common ids; not all devices expose these
 var ids = new[] { "Velocity", "ActualVelocity", "VelocityActual" };
 foreach (var name in ids)
 {
 try
 {
 // reflect enum member if exists
 var fi = typeof(StatusItemId).GetField(name);
 if (fi?.GetValue(null) is StatusItemId id)
 {
 var item = s.GetStatusItem(id);
 if (item != null && item.IsInteger)
 {
 var dev = item.GetInteger();
 if (_device is IUnitConverter uc)
 {
 var r = uc.FromDeviceUnitToPhysical(ScaleType.Velocity, dev);
 if (r != null) return r.Value;
 }
 }
 }
 }
 catch { }
 }
 }
 }
 catch { }

 // Fallback: if we can at least read set max velocity, return0 when unknown
 return 0.0;
 }

 internal double GetVelocityMmPerSec() => ReadVelocityMmPerSec();
}
