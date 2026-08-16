using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STS2Mobile.Launcher;

internal enum RecoveryAction
{
    SafeMode,
    ExcludeCandidate,
    BisectFirstHalf,
    ContinueNormally,
}

internal sealed class RecoveryModDescriptor
{
    public RecoveryModDescriptor(
        string id,
        string topLevelDirectory,
        IReadOnlyCollection<string> dependencies
    )
    {
        Id = id ?? "";
        TopLevelDirectory = topLevelDirectory ?? "";
        Dependencies = dependencies ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string TopLevelDirectory { get; }
    public IReadOnlyCollection<string> Dependencies { get; }
}

internal sealed class ModRecoveryPlan
{
    private readonly HashSet<string> _allowedTopLevelDirectories;

    internal ModRecoveryPlan(
        RecoveryAction action,
        string candidate,
        IEnumerable<string> allowedTopLevelDirectories,
        bool filtersMods,
        bool skipOptionalWarmup,
        int selectedModCount,
        int totalModCount
    )
    {
        Action = action;
        Candidate = candidate ?? "";
        _allowedTopLevelDirectories = new HashSet<string>(
            allowedTopLevelDirectories.Select(Normalize),
            StringComparer.Ordinal
        );
        FiltersMods = filtersMods;
        SkipOptionalWarmup = skipOptionalWarmup;
        SelectedModCount = selectedModCount;
        TotalModCount = totalModCount;
    }

    public static ModRecoveryPlan Normal { get; } =
        new(
            RecoveryAction.ContinueNormally,
            "",
            Array.Empty<string>(),
            filtersMods: false,
            skipOptionalWarmup: false,
            selectedModCount: 0,
            totalModCount: 0
        );

    public RecoveryAction Action { get; }
    public string Candidate { get; }
    public bool FiltersMods { get; }
    public bool SkipOptionalWarmup { get; }
    public int SelectedModCount { get; }
    public int TotalModCount { get; }

    public bool ShouldExposeDirectory(string externalRoot, string path)
    {
        if (!FiltersMods)
            return true;
        if (!TryNormalizeUnderRoot(externalRoot, path, out var root, out var normalized))
            return false;
        if (normalized == root)
            return true;
        return IsUnderAllowedDirectory(normalized);
    }

    public bool ShouldExposeFile(string externalRoot, string path)
    {
        if (!FiltersMods)
            return true;
        if (!TryNormalizeUnderRoot(externalRoot, path, out var root, out var normalized))
            return false;
        if (Path.GetDirectoryName(normalized) == root)
        {
            // Safe Mode and bisect cannot safely classify unmanaged root-level
            // manifests. Candidate exclusion, however, promises to hide only the
            // candidate's containing directory, so unrelated root mods remain.
            return Action == RecoveryAction.ExcludeCandidate;
        }
        return IsUnderAllowedDirectory(normalized);
    }

    private bool IsUnderAllowedDirectory(string normalizedPath)
    {
        foreach (var allowed in _allowedTopLevelDirectories)
        {
            if (
                normalizedPath == allowed
                || normalizedPath.StartsWith(
                    allowed + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                )
            )
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryNormalizeUnderRoot(
        string externalRoot,
        string path,
        out string root,
        out string normalized
    )
    {
        root = "";
        normalized = "";
        try
        {
            root = Normalize(externalRoot);
            normalized = Normalize(path);
            return normalized == root
                || normalized.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                );
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path ?? "")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

internal static class ModRecoveryPolicy
{
    public static ModRecoveryPlan Build(
        RecoveryAction action,
        string candidate,
        IReadOnlyCollection<RecoveryModDescriptor> source
    )
    {
        var mods = (source ?? Array.Empty<RecoveryModDescriptor>())
            .Where(mod =>
                !string.IsNullOrWhiteSpace(mod.Id)
                && !string.IsNullOrWhiteSpace(mod.TopLevelDirectory)
            )
            .OrderBy(mod => mod.Id, StringComparer.Ordinal)
            .ThenBy(mod => mod.TopLevelDirectory, StringComparer.Ordinal)
            .ToList();

        if (action == RecoveryAction.ContinueNormally)
            return ModRecoveryPlan.Normal;

        if (action == RecoveryAction.SafeMode)
            return SafeMode(candidate, mods.Count);

        if (action == RecoveryAction.ExcludeCandidate)
        {
            var excluded = mods.Where(mod =>
                    string.Equals(mod.Id, candidate, StringComparison.Ordinal)
                )
                .Select(mod => mod.TopLevelDirectory)
                .ToHashSet(StringComparer.Ordinal);
            if (excluded.Count == 0)
                return SafeMode(candidate, mods.Count);

            var allowed = mods.Where(mod => !excluded.Contains(mod.TopLevelDirectory))
                .Select(mod => mod.TopLevelDirectory)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new ModRecoveryPlan(
                action,
                candidate,
                allowed,
                filtersMods: true,
                skipOptionalWarmup: false,
                selectedModCount: mods.Count(mod => allowed.Contains(mod.TopLevelDirectory)),
                totalModCount: mods.Count
            );
        }

        var groups = mods.GroupBy(mod => mod.TopLevelDirectory, StringComparer.Ordinal)
            .OrderBy(group => group.Min(mod => mod.Id), StringComparer.Ordinal)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        if (groups.Count == 0)
            return SafeMode(candidate, 0);

        var selectedDirectories = groups
            .Take((groups.Count + 1) / 2)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        AddDependencyClosure(mods, selectedDirectories);

        return new ModRecoveryPlan(
            RecoveryAction.BisectFirstHalf,
            candidate,
            selectedDirectories,
            filtersMods: true,
            skipOptionalWarmup: false,
            selectedModCount: mods.Count(mod =>
                selectedDirectories.Contains(mod.TopLevelDirectory)
            ),
            totalModCount: mods.Count
        );
    }

    private static ModRecoveryPlan SafeMode(string candidate, int totalModCount) =>
        new(
            RecoveryAction.SafeMode,
            candidate,
            Array.Empty<string>(),
            filtersMods: true,
            skipOptionalWarmup: true,
            selectedModCount: 0,
            totalModCount: totalModCount
        );

    private static void AddDependencyClosure(
        IReadOnlyCollection<RecoveryModDescriptor> mods,
        HashSet<string> selectedDirectories
    )
    {
        var byId = mods.GroupBy(mod => mod.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var pending = new Queue<string>(
            mods.Where(mod => selectedDirectories.Contains(mod.TopLevelDirectory))
                .SelectMany(mod => mod.Dependencies)
        );
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!visited.Add(id) || !byId.TryGetValue(id, out var dependency))
                continue;
            if (selectedDirectories.Add(dependency.TopLevelDirectory))
            {
                foreach (var transitive in dependency.Dependencies)
                    pending.Enqueue(transitive);
            }
        }
    }
}
