using System;
using System.Collections.Generic;

namespace STS2Mobile.Patches;

internal static class BaseLibTreasurePatchPolicy
{
    private static readonly Version FirstKnownBadVersion = new(3, 4, 4);
    private static readonly Version FirstUnknownVersion = new(3, 4, 6);

    public static bool RequiresReplacement(
        Version baseLibVersion,
        IEnumerable<string> directlyLoadedTreasureFields,
        bool inspectionSucceeded
    )
    {
        if (inspectionSucceeded)
        {
            var fields = new HashSet<string>(
                directlyLoadedTreasureFields ?? Array.Empty<string>(),
                StringComparer.Ordinal
            );
            return fields.Contains("_runState") && fields.Contains("_chestButton");
        }

        return baseLibVersion != null
            && baseLibVersion.CompareTo(FirstKnownBadVersion) >= 0
            && baseLibVersion.CompareTo(FirstUnknownVersion) < 0;
    }
}
