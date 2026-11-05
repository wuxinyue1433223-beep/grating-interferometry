// ?????????????????????????

using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace VisionShell.App.Services;

/// <summary>
/// Custom load context for isolating plugin dependencies.
/// </summary>
// ??????????????????????????
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginPath;

    public PluginLoadContext(string pluginPath) : base(isCollectible: false)
    {
        _pluginPath = pluginPath;
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        var directPath = Path.Combine(_pluginPath, $"{assemblyName.Name}.dll");
        if (File.Exists(directPath))
        {
            return LoadFromAssemblyPath(directPath);
        }

        var libsPath = Path.Combine(_pluginPath, "libs", $"{assemblyName.Name}.dll");
        return File.Exists(libsPath) ? LoadFromAssemblyPath(libsPath) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        var fileName = Path.HasExtension(unmanagedDllName) ? unmanagedDllName : $"{unmanagedDllName}.dll";
        var directPath = Path.Combine(_pluginPath, fileName);
        if (File.Exists(directPath))
        {
            return LoadUnmanagedDllFromPath(directPath);
        }

        var libsPath = Path.Combine(_pluginPath, "libs", fileName);
        return File.Exists(libsPath) ? LoadUnmanagedDllFromPath(libsPath) : IntPtr.Zero;
    }
}
