// 文件职责：插件清单模型，对应 plugin.json 的反序列化结构。

using System.Reflection;

namespace VisionShell.Core.Models;

/// <summary>
/// Metadata parsed from scan of plugin.json
/// </summary>
// 类型职责：插件清单数据模型。
public class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Type { get; set; } = string.Empty; // "camera", "stage"
    public string EntryType { get; set; } = string.Empty;
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    
    // Runtime info
    public string? PluginDirectory { get; set; }
    public string? AssemblyPath { get; set; }
}
