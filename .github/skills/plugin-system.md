# Plugin System Capability

## Description
Loading and managing external device drivers.

## Components
- **PluginLoader**: Scans `plugins/directory`, parses `plugin.json`.
- **AssemblyLoadContext**: Isolates plugin dependencies.
- **ServiceRegistry**: Maps `IDeviceFactory` to `IServiceCollection`.

## Rules
- Plugin output MUST NOT include `VisionShell.Core.dll` (host provided).
- Plugin dependencies MUST be local to the plugin folder.
- Exceptions during load should be logged and skipped.
