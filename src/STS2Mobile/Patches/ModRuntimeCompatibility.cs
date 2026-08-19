using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using STS2Mobile.Launcher;

namespace STS2Mobile.Patches;

// Some desktop mods contain obfuscator-generated IL that CoreCLR accepts but
// Mono Android's JIT rejects with InvalidProgramException. The launcher cannot
// safely rewrite arbitrary control flow or reconfigure an already embedded
// runtime. This boundary preserves the original exception while attributing the
// incompatible class, keeping the broken assembly out of GetLoadedMods(), and
// presenting a concrete localized explanation instead of an opaque DLL error.
public static class ModRuntimeCompatibility
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _observed = new(StringComparer.Ordinal);
    private static readonly HashSet<Assembly> _incompatibleAssemblies = new();

    public static object InvokeInitializer(MethodBase method, object target, object[] parameters)
    {
        try
        {
            return method.Invoke(target, parameters);
        }
        catch (Exception ex)
        {
            ObserveFailure(ex, method?.DeclaringType?.Assembly);
            throw;
        }
    }

    public static void ObserveFailure(Exception exception, Assembly expectedModAssembly = null)
    {
        try
        {
            if (
                !TryFindRejectedType(exception, expectedModAssembly, out var rejectedType)
                || rejectedType?.Assembly == null
                || !ModAssemblyRegistry.IsModAssembly(rejectedType.Assembly)
            )
            {
                return;
            }

            string assemblyName = rejectedType.Assembly.GetName().Name ?? "<unknown>";
            string key =
                rejectedType.Assembly.ManifestModule.ModuleVersionId + "|" + rejectedType.FullName;
            lock (_lock)
            {
                _incompatibleAssemblies.Add(rejectedType.Assembly);
                if (!_observed.Add(key))
                    return;
            }

            PatchHelper.Log(
                $"[ModCompat] '{assemblyName}' contains code rejected by the bundled "
                    + $"Mono Android runtime ({rejectedType.FullName}). The launcher left the "
                    + "DLL unchanged; use a Mono-compatible/unobfuscated mod build."
            );
            LauncherModel
                .GetGodotApp()
                ?.Call("showModRuntimeCompatibilityNotice", SanitizeDisplayName(assemblyName));
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[ModCompat] InvalidProgramException attribution failed: {ex.GetType().Name}"
            );
        }
    }

    public static bool IsIncompatible(Assembly assembly)
    {
        if (assembly == null)
            return false;
        lock (_lock)
            return _incompatibleAssemblies.Contains(assembly);
    }

    private static bool TryFindRejectedType(
        Exception exception,
        Assembly expectedModAssembly,
        out Type rejectedType
    )
    {
        rejectedType = null;
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is not InvalidProgramException)
                continue;

            if (TryAcceptMethod(current.TargetSite, expectedModAssembly, out rejectedType))
                return true;

            StackFrame[] frames;
            try
            {
                frames = new StackTrace(current, false).GetFrames();
            }
            catch
            {
                frames = null;
            }

            if (frames == null)
                continue;
            foreach (var frame in frames)
            {
                MethodBase method;
                try
                {
                    method = frame.GetMethod();
                }
                catch
                {
                    continue;
                }
                if (TryAcceptMethod(method, expectedModAssembly, out rejectedType))
                    return true;
            }
        }
        return false;
    }

    private static bool TryAcceptMethod(
        MethodBase method,
        Assembly expectedModAssembly,
        out Type rejectedType
    )
    {
        rejectedType = method?.DeclaringType;
        if (rejectedType?.Assembly == null)
            return false;
        if (expectedModAssembly != null && rejectedType.Assembly != expectedModAssembly)
            return false;
        return ModAssemblyRegistry.IsModAssembly(rejectedType.Assembly);
    }

    private static string SanitizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "mod";
        value = value.Replace('\r', '_').Replace('\n', '_').Replace('\t', '_');
        return value.Length <= 96 ? value : value.Substring(0, 96);
    }
}
