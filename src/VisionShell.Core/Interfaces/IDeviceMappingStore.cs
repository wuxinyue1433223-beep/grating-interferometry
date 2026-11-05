// 文件职责：设备映射存储抽象：Feature 角色与设备关系的持久化契约。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VisionShell.Core.Models;

namespace VisionShell.Core.Interfaces;

// 类型职责：设备映射存储接口。
public interface IDeviceMappingStore
{
    IReadOnlyDictionary<string, IDictionary<string, string>> GetAllMappings();
    IReadOnlyDictionary<string, string>? GetMappingForFeature(string featureId);
    Task SetMappingForFeatureAsync(string featureId, IDictionary<string, string> mapping);
    Task RemoveMappingForFeatureAsync(string featureId);
    event EventHandler<MappingChangedEventArgs>? MappingChanged;
}
