# Contributing to VisionShell

## Development Flow
1. **Feature Branches**: `feature/<name>` for functional modules.
2. **Plugin Branches**: `plugin/<device>` for new hardware drivers.
3. **Core Changes**: Discuss architectural changes before touching `VisionShell.Core`.

## Code Style
- Use standard C# .NET 8 conventions.
- Namespace: `VisionShell.*`
- Async/Await: Use `ConfigureAwait(false)` in libraries; `Dispatcher` in UI.

## Testing
- (Currently opt-in) Add unit tests for Core logic where possible.
- Manual verification using `VisionShell.App` and Sample Plugins.

## Documentation
- Update `README.md` in plugin folders.
- Maintain `docs/plugins.md` if the contract evolves.
