#if NETCOREAPP
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Mister.Version;

/// <summary>
/// Defense in depth for locating libgit2. MSBuild's plugin load context normally resolves
/// tasks/net8.0/runtimes/&lt;rid&gt;/native/* through the task's deps.json. If a host loads the task without
/// deps.json-aware resolution, this resolves the library for the current OS/architecture from the same layout
/// (musl hosts fall back to the glibc build of the same architecture).
/// </summary>
internal static class NativeLibraryBootstrap
{
    private static IntPtr _handle;

#pragma warning disable CA2255 // Intentional: the task assembly is a plugin that must hook resolution before LibGit2Sharp loads
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            var context = AssemblyLoadContext.GetLoadContext(typeof(LibGit2Sharp.Repository).Assembly);
            if (context != null)
            {
                context.ResolvingUnmanagedDll += Resolve;
            }
        }
        catch
        {
            // Never fail module initialization; normal probing still applies.
        }
    }
#pragma warning restore CA2255

    private static IntPtr Resolve(System.Reflection.Assembly assembly, string name)
    {
        if (!name.StartsWith("git2-", StringComparison.Ordinal))
            return IntPtr.Zero;
        if (_handle != IntPtr.Zero)
            return _handle;

        var directory = Path.GetDirectoryName(typeof(NativeLibraryBootstrap).Assembly.Location);
        if (string.IsNullOrEmpty(directory))
            return IntPtr.Zero;

        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string[] rids;
        string file;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            rids = new[] { $"win-{arch}" };
            file = name + ".dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            rids = new[] { $"osx-{arch}" };
            file = "lib" + name + ".dylib";
        }
        else
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            var musl = rid.Contains("musl", StringComparison.OrdinalIgnoreCase) || rid.StartsWith("alpine", StringComparison.OrdinalIgnoreCase);
            rids = musl ? new[] { $"linux-musl-{arch}", $"linux-{arch}" } : new[] { $"linux-{arch}" };
            file = "lib" + name + ".so";
        }

        foreach (var rid in rids)
        {
            var path = Path.Combine(directory, "runtimes", rid, "native", file);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                _handle = handle;
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}
#endif
