using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STS2Mobile.Modding;

internal readonly record struct ModAutoDisableCandidate(
    string ModId,
    string TopLevelDir,
    bool IsExclusiveFolder
);

// Pure fail-closed selection policy, split from filesystem mutation so duplicate
// ids, path traversal, and assembly/manifest mismatches have deterministic tests.
internal static class ModAutoDisablePolicy
{
    internal static string SelectTopLevelDirectory(
        string modId,
        string assemblyLocation,
        string enabledRoot,
        IEnumerable<ModAutoDisableCandidate> candidates
    )
    {
        if (
            string.IsNullOrWhiteSpace(modId)
            || string.IsNullOrWhiteSpace(enabledRoot)
            || candidates == null
        )
            return null;

        var matchingDirectories = candidates
            .Where(c =>
                string.Equals(c.ModId, modId, StringComparison.Ordinal)
                && c.IsExclusiveFolder
                && IsDirectChild(enabledRoot, c.TopLevelDir)
            )
            .Select(c => NormalizePath(c.TopLevelDir))
            .Where(path => path != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (matchingDirectories.Count != 1)
            return null;

        var selected = matchingDirectories[0];
        if (
            !string.IsNullOrWhiteSpace(assemblyLocation)
            && !IsWithinDirectory(selected, assemblyLocation)
        )
            return null;

        return selected;
    }

    internal static bool PathsEqual(string left, string right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        return normalizedLeft != null
            && normalizedRight != null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool IsDirectChild(string root, string candidate)
    {
        var normalizedRoot = NormalizePath(root);
        var normalizedCandidate = NormalizePath(candidate);
        if (normalizedRoot == null || normalizedCandidate == null)
            return false;
        return string.Equals(
            Path.GetDirectoryName(normalizedCandidate)?.Replace('\\', '/'),
            normalizedRoot,
            StringComparison.Ordinal
        );
    }

    private static bool IsWithinDirectory(string directory, string path)
    {
        var normalizedDirectory = NormalizePath(directory);
        var normalizedPath = NormalizePath(path);
        return normalizedDirectory != null
            && normalizedPath != null
            && normalizedPath.StartsWith(normalizedDirectory + "/", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }
        catch
        {
            return null;
        }
    }
}
