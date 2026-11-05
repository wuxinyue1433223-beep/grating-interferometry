// 文件职责：设备映射存储：负责角色到设备 ID 的读写与变更通知。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VisionShell.App.Services;

using VisionShell.Core.Interfaces;
using VisionShell.Core.Models;

// 类型职责：映射文件序列化传输对象。
internal class DeviceMappingsDto
{
    public int Version { get; set; } = 1;
    public Dictionary<string, Dictionary<string, string>> FeatureMappings { get; set; } = new();
}

// 类型职责：映射持久化实现，负责读写 JSON 并触发变更事件。
public class DeviceMappingStore : IDeviceMappingStore
{
    private readonly ILogger<DeviceMappingStore> _logger;
    private readonly string _path;
    private readonly object _lock = new();
    private DeviceMappingsDto _cache = new();

    public event EventHandler<MappingChangedEventArgs>? MappingChanged;

    public DeviceMappingStore(ILogger<DeviceMappingStore> logger)
    {
        _logger = logger;
        _path = GetDefaultPath();
        Load();
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VisionShell");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "device-mappings.json");
    }

    private void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                _cache = new DeviceMappingsDto();
                SaveInternal();
                return;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var dto = JsonSerializer.Deserialize<DeviceMappingsDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dto != null) _cache = dto;
            }
            catch (Exception ex)
            {
            _logger.LogWarning(ex, "读取设备映射失败，将创建空映射");
                // attempt backup recovery
                var bak = _path + ".bak";
                if (File.Exists(bak))
                {
                    try
                    {
                        File.Copy(bak, _path, true);
                        var json2 = File.ReadAllText(_path);
                        var dto2 = JsonSerializer.Deserialize<DeviceMappingsDto>(json2);
                        if (dto2 != null) _cache = dto2;
                    }
                    catch (Exception ex2)
                    {
                _logger.LogError(ex2, "从备份恢复设备映射失败");
                        _cache = new DeviceMappingsDto();
                    }
                }
                else
                {
                    _cache = new DeviceMappingsDto();
                }
            }
        }
    }

    private void SaveInternal()
    {
        var tmp = _path + ".tmp";
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tmp, json);
        try
        {
            if (File.Exists(_path))
            {
                var bak = _path + ".bak";
                File.Copy(_path, bak, true);
                File.Delete(_path);
            }
            File.Move(tmp, _path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "原子保存设备映射失败，将尝试直接写入");
            File.WriteAllText(_path, json);
        }
    }

    public IReadOnlyDictionary<string, IDictionary<string, string>> GetAllMappings()
    {
        lock (_lock)
        {
            return _cache.FeatureMappings.ToDictionary(k => k.Key, v => (IDictionary<string,string>)v.Value);
        }
    }

    public IReadOnlyDictionary<string, string>? GetMappingForFeature(string featureId)
    {
        lock (_lock)
        {
            if (_cache.FeatureMappings.TryGetValue(featureId, out var map))
            {
                return map;
            }
            return null;
        }
    }

    public Task SetMappingForFeatureAsync(string featureId, IDictionary<string, string> mapping)
    {
        if (featureId == null) throw new ArgumentNullException(nameof(featureId));
        lock (_lock)
        {
            _cache.FeatureMappings[featureId] = new Dictionary<string, string>(mapping);
            SaveInternal();
        }
        MappingChanged?.Invoke(this, new MappingChangedEventArgs(featureId, new Dictionary<string,string>(mapping), "Set"));
        return Task.CompletedTask;
    }

    public Task RemoveMappingForFeatureAsync(string featureId)
    {
        if (featureId == null) throw new ArgumentNullException(nameof(featureId));
        lock (_lock)
        {
            _cache.FeatureMappings.Remove(featureId);
            SaveInternal();
        }
        MappingChanged?.Invoke(this, new MappingChangedEventArgs(featureId, new Dictionary<string,string>(), "Remove"));
        return Task.CompletedTask;
    }
}
