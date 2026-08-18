using System;

namespace STS2Mobile.Launcher;

internal enum QuickRestartProcessAction
{
    Enable,
    KeepEnabled,
    ResetAndDisable,
    Disable,
}

// Pure policy for the narrowly scoped Quick Restart v2.0.0 compatibility fix.
// Unknown or updated binaries deliberately fail open and keep their own behavior.
internal static class QuickRestartPerformancePolicy
{
    internal const string AssemblyName = "QuickRestart";
    internal static readonly Version AssemblyVersion = new(1, 0, 0, 0);
    internal static readonly Guid ModuleVersionId =
        Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb");
    internal const string AssemblySha256 =
        "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973";

    internal static bool MatchesIdentity(
        string assemblyName,
        Version assemblyVersion,
        Guid moduleVersionId,
        string assemblySha256,
        bool isExternalModAssembly
    ) =>
        isExternalModAssembly
        && string.Equals(assemblyName, AssemblyName, StringComparison.Ordinal)
        && Equals(assemblyVersion, AssemblyVersion)
        && moduleVersionId == ModuleVersionId
        && string.Equals(assemblySha256, AssemblySha256, StringComparison.OrdinalIgnoreCase);

    internal static QuickRestartProcessAction AfterInput(bool isHolding, bool triggered)
    {
        if (triggered)
            return QuickRestartProcessAction.Disable;
        return isHolding
            ? QuickRestartProcessAction.Enable
            : QuickRestartProcessAction.ResetAndDisable;
    }

    internal static QuickRestartProcessAction AfterProcess(bool isHolding, bool triggered) =>
        isHolding && !triggered
            ? QuickRestartProcessAction.KeepEnabled
            : QuickRestartProcessAction.Disable;
}
