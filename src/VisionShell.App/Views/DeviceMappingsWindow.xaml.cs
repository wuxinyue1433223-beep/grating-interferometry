// 文件职责：设备映射窗口逻辑：可选项过滤、唯一性约束与保存。

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VisionShell.App.Services;
using VisionShell.Core.Interfaces;

namespace VisionShell.App.Views;

// 类型职责：设备映射界面的设备候选项模型。
public class DeviceListItem
{
    public string DeviceId { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string TypeIndicator { get; set; } = "IDevice";
    public string FactoryType { get; set; } = string.Empty;
    public bool IsCloneable { get; set; }
}

// 类型职责：设备映射界面的单角色映射模型。
public class RoleMappingModel : INotifyPropertyChanged
{
    private string _deviceId = string.Empty;

    public string Role { get; set; } = string.Empty;
    public string AllowedDeviceType { get; set; } = "IDevice";

    public ObservableCollection<DeviceListItem> AvailableOptions { get; } = new();

    public string DeviceId
    {
        get => _deviceId;
        set
        {
            if (_deviceId == value) return;
            _deviceId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeviceId)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// 类型职责：设备映射窗口，负责展示和保存角色绑定。
public partial class DeviceMappingsWindow : Window
{
    private readonly IDeviceMappingStore _store;
    private readonly IDeviceManager _deviceManager;
    private readonly VisionShell.Core.Interfaces.IFeatureDeviceRequirements? _activeFeature;

    private List<KeyValuePair<string, IReadOnlyDictionary<string, string>>> _features = new();
    public List<DeviceListItem> AvailableDevices { get; } = new();
    private List<RoleMappingModel> _roleModels = new();
    private bool _isRefreshing = false;

    public DeviceMappingsWindow(IDeviceMappingStore store, IDeviceManager deviceManager, IEnumerable<VisionShell.Core.Interfaces.IFeatureDeviceRequirements> featureRequirements)
    {
        InitializeComponent();
        _store = store;
        _deviceManager = deviceManager;
        _activeFeature = featureRequirements?.FirstOrDefault();
        DataContext = this;
        ReloadAll();
    }

    private void ReloadAll()
    {
        _isRefreshing = true;
        try
        {
            // Load discovered descriptors
            AvailableDevices.Clear();
            var descriptors = _deviceManager.GetDiscoveredDescriptors();
            foreach (var d in descriptors)
            {
                // ComboBox 的 SelectedValue 绑定到 DeviceId。
                // 这里的 DeviceId 使用“可唯一标识映射目标”的值（真实设备 ID 或 DescriptorId）。
                // Display 用“显示名 + (ID)”格式，便于人工核对。
                var id = d.DescriptorId;
                var display = string.IsNullOrWhiteSpace(d.DisplayName) ? id : $"{d.DisplayName} ({id})";

                AvailableDevices.Add(new DeviceListItem
                {
                    DeviceId = id,
                    Display = display,
                    TypeIndicator = d.DeviceTypeIdentifier,
                    FactoryType = d.FactoryType,
                    IsCloneable = d.IsCloneable
                });
            }

            // Load mappings for active feature (if available)
            MappingsPanel.ItemsSource = null;
            _roleModels.Clear();
            if (_activeFeature != null)
            {
                var existing = _store.GetMappingForFeature(_activeFeature.FeatureId) ?? new Dictionary<string, string>();

                foreach (var r in _activeFeature.Roles)
                {
                    var saved = existing.TryGetValue(r.Name, out var v) ? v : string.Empty;
                    var rm = new RoleMappingModel 
                    { 
                        Role = r.Name, 
                        AllowedDeviceType = r.DeviceType, 
                        DeviceId = saved ?? string.Empty 
                    };
                    rm.PropertyChanged += RoleModel_PropertyChanged;
                    _roleModels.Add(rm);
                }

                // initialize options and bind
                RefreshOptionsForAllRoles();
                MappingsPanel.ItemsSource = _roleModels;
            }
            else
            {
                MappingsPanel.ItemsSource = null;
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshOptionsForAllRoles()
    {
        // Keep current selections stable while option lists are rebuilt.
        var selectedBeforeRefresh = _roleModels.ToDictionary(r => r.Role, r => r.DeviceId ?? string.Empty);

        // Find assigned unique (non-cloneable) devices
        var assignedUnique = new HashSet<string>(_roleModels
            .Where(r => !string.IsNullOrEmpty(r.DeviceId))
            .Select(r => new { r.DeviceId, Model = r })
            .Where(x => 
            {
                var dev = AvailableDevices.FirstOrDefault(d => d.DeviceId == x.DeviceId);
                return dev != null && !dev.IsCloneable;
            })
            .Select(x => x.DeviceId));

        foreach (var role in _roleModels)
        {
            var candidates = new List<DeviceListItem>();

            foreach (var dev in AvailableDevices)
            {
                // type filter: allow if types match or role is generic IDevice
                if (role.AllowedDeviceType != "IDevice" && dev.TypeIndicator != role.AllowedDeviceType) continue;

                // uniqueness logic:
                // if device is NOT cloneable and is assigned to OTHER role, skip it.
                if (!dev.IsCloneable && assignedUnique.Contains(dev.DeviceId) && dev.DeviceId != role.DeviceId)
                {
                    continue;
                }

                candidates.Add(dev);
            }

            // Hardware roles are strict contracts. Never offer an unrelated IStage as a
            // fallback merely because the expected controller is temporarily absent.
            var finalDevices = IsHardwareConstrainedRole(role.Role)
                ? candidates.Where(d => IsPreferredForRole(role.Role, d)).ToList()
                : candidates;

            if (!string.IsNullOrWhiteSpace(role.DeviceId)
                && finalDevices.All(d => !string.Equals(d.DeviceId, role.DeviceId, StringComparison.OrdinalIgnoreCase))
                && CreateUnavailableSavedDevice(role) is { } unavailableSavedDevice)
            {
                finalDevices.Insert(0, unavailableSavedDevice);
            }

            var newOptions = new List<DeviceListItem>();
            // empty option
            newOptions.Add(new DeviceListItem { DeviceId = string.Empty, Display = "<None>", TypeIndicator = role.AllowedDeviceType });
            newOptions.AddRange(finalDevices);

            // Update collection without clearing everything if possible to avoid losing selection
            UpdateCollection(role.AvailableOptions, newOptions);
        }

        // ComboBox can transiently null out SelectedValue during collection refresh.
        // Restore the previous selection when it is still a valid option.
        foreach (var role in _roleModels)
        {
            if (!selectedBeforeRefresh.TryGetValue(role.Role, out var previous) || string.IsNullOrWhiteSpace(previous))
            {
                continue;
            }

            if (role.AvailableOptions.Any(o => o.DeviceId == previous) && role.DeviceId != previous)
            {
                role.DeviceId = previous;
            }
        }
    }

    private static bool IsPreferredForRole(string roleName, DeviceListItem device)
    {
        var factory = device.FactoryType ?? string.Empty;
        var id = device.DeviceId ?? string.Empty;

        if (string.Equals(roleName, "Actuator1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, "Actuator2", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(factory, "thorlabs-xa-stage", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(roleName, "VerticalStage", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(factory, "thorlabs-kinesis-stage", StringComparison.OrdinalIgnoreCase)
                && id.StartsWith("kinesis-legacy:kvs:", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(roleName, "HorizontalStage", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(factory, "thorlabs-kinesis-stage", StringComparison.OrdinalIgnoreCase)
                && id.StartsWith("kinesis-legacy:bdc:", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(roleName, "MechanicalShutter", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(factory, "thorlabs-sc30-shutter", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool IsHardwareConstrainedRole(string roleName)
    {
        return roleName is "Actuator1" or "Actuator2" or "VerticalStage" or "HorizontalStage" or "MechanicalShutter";
    }

    private static DeviceListItem? CreateUnavailableSavedDevice(RoleMappingModel role)
    {
        return role.Role switch
        {
            "Actuator1" or "Actuator2" => new DeviceListItem
            {
                DeviceId = role.DeviceId,
                Display = $"KDC101 - XA（已保存，当前未发现）({role.DeviceId})",
                TypeIndicator = "IStage",
                FactoryType = "thorlabs-xa-stage"
            },
            "VerticalStage" when role.DeviceId.StartsWith("kinesis-legacy:kvs:", StringComparison.OrdinalIgnoreCase) => new DeviceListItem
            {
                DeviceId = role.DeviceId,
                Display = $"Kinesis KVS（已保存，当前未发现）({role.DeviceId})",
                TypeIndicator = "IStage",
                FactoryType = "thorlabs-kinesis-stage"
            },
            "HorizontalStage" when role.DeviceId.StartsWith("kinesis-legacy:bdc:", StringComparison.OrdinalIgnoreCase) => new DeviceListItem
            {
                DeviceId = role.DeviceId,
                Display = $"Kinesis BDC（已保存，当前未发现）({role.DeviceId})",
                TypeIndicator = "IStage",
                FactoryType = "thorlabs-kinesis-stage"
            },
            "MechanicalShutter" when role.DeviceId.StartsWith("tl-sc30:", StringComparison.OrdinalIgnoreCase) => new DeviceListItem
            {
                DeviceId = role.DeviceId,
                Display = $"SC30（已保存，当前未发现）({role.DeviceId})",
                TypeIndicator = "IShutter",
                FactoryType = "thorlabs-sc30-shutter"
            },
            _ => null
        };
    }

    private void UpdateCollection(ObservableCollection<DeviceListItem> collection, List<DeviceListItem> newItems)
    {
        // Apply in-place diff to avoid ComboBox losing SelectedValue during Clear()+Add().
        var i = 0;
        while (i < newItems.Count)
        {
            if (i >= collection.Count)
            {
                collection.Add(newItems[i]);
                i++;
                continue;
            }

            if (collection[i].DeviceId == newItems[i].DeviceId)
            {
                if (!string.Equals(collection[i].Display, newItems[i].Display, StringComparison.Ordinal)
                    || !string.Equals(collection[i].TypeIndicator, newItems[i].TypeIndicator, StringComparison.Ordinal)
                    || collection[i].IsCloneable != newItems[i].IsCloneable)
                {
                    collection[i] = newItems[i];
                }

                i++;
                continue;
            }

            var existingIndex = -1;
            for (var j = i + 1; j < collection.Count; j++)
            {
                if (collection[j].DeviceId == newItems[i].DeviceId)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                collection.Move(existingIndex, i);
                if (!string.Equals(collection[i].Display, newItems[i].Display, StringComparison.Ordinal)
                    || !string.Equals(collection[i].TypeIndicator, newItems[i].TypeIndicator, StringComparison.Ordinal)
                    || collection[i].IsCloneable != newItems[i].IsCloneable)
                {
                    collection[i] = newItems[i];
                }

                i++;
                continue;
            }

            collection.Insert(i, newItems[i]);
            i++;
        }

        while (collection.Count > newItems.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private void RoleModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRefreshing) return;

        if (e.PropertyName == nameof(RoleMappingModel.DeviceId) && sender is RoleMappingModel rm)
        {
            _isRefreshing = true;
            try
            {
                // ensure unique assignment across roles
                RefreshOptionsForAllRoles();
                _ = SaveCurrentMappingsAsync();
            }
            finally
            {
                _isRefreshing = false;
            }
        }
    }

    private async Task SaveCurrentMappingsAsync()
    {
        var featureId = _activeFeature?.FeatureId;
        if (string.IsNullOrEmpty(featureId)) return;

        var map = _roleModels.ToDictionary(r => r.Role, r => r.DeviceId ?? string.Empty);
        await _store.SetMappingForFeatureAsync(featureId, map);
    }
}

