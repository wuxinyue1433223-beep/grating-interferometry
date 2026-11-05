// 文件职责：映射变更事件模型，描述某 Feature 的映射更新内容。

using System.Collections.Generic;

namespace VisionShell.Core.Models;

// 类型职责：映射变更事件参数记录。
public record MappingChangedEventArgs(string FeatureId, IDictionary<string, string> Mapping, string ChangeType);
