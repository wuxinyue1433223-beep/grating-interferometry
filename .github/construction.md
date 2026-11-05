# Project Construction Guidelines

## Naming Conventions
- **Solution**: VisionShell
- **Projects**: VisionShell.<Layer>.<Module>
  - Core: `VisionShell.Core`
  - App: `VisionShell.App`
  - Features: `VisionShell.Features.<Name>`
  - Plugins: `VisionShell.Plugins.<DeviceName>`

## Output Directories
- **App & Core**: `bin/$(Configuration)/app/`
- **Features**: Referenced by App, output to App folder.
- **Device Plugins**: `bin/$(Configuration)/app/plugins/$(PluginName)/` (Clean output, no intermediate files)

## Plugin Architecture
- **Discovery**: Folder scan `plugins/*/*/plugin.json`.
- **Isolation**: Each plugin loaded in its own `AssemblyLoadContext`.
- **Logging**: Host injects `ILogger`. Plugins MUST handle their own exceptions and log errors; do not crash the host.
- **Manifest**: `plugin.json` must include `id`, `name`, `version`, `type` (`camera` | `stage`), `entryType`, `dependencies`.

## Collaboration
- Follow `.github/copilot-prompts.md` for AI interactions.
- Update `docs/` when changing contracts.
