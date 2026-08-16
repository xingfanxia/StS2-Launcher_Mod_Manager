using System;
using System.Globalization;

namespace STS2Mobile.Launcher;

internal readonly struct StartupRecoveryRequest
{
    private StartupRecoveryRequest(
        bool pending,
        int failureCount,
        string stage,
        string modCandidate,
        string reason
    )
    {
        Pending = pending;
        FailureCount = failureCount;
        Stage = stage;
        ModCandidate = modCandidate;
        Reason = reason;
    }

    public static StartupRecoveryRequest None => new(false, 0, "", "", "");

    public bool Pending { get; }
    public int FailureCount { get; }
    public string Stage { get; }
    public string ModCandidate { get; }
    public string Reason { get; }

    public static StartupRecoveryRequest Parse(string encoded)
    {
        try
        {
            var fields = encoded?.Split('\n');
            if (
                fields == null
                || fields.Length != 5
                || fields[0] != "1"
                || !int.TryParse(
                    fields[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var failureCount
                )
                || failureCount < 2
                || !IsAsciiToken(fields[2], 48)
                || !IsCandidate(fields[3])
                || !IsAsciiToken(fields[4], 48)
            )
            {
                return None;
            }

            return new StartupRecoveryRequest(true, failureCount, fields[2], fields[3], fields[4]);
        }
        catch
        {
            return None;
        }
    }

    private static bool IsAsciiToken(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
            return false;
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                return false;
        }
        return true;
    }

    private static bool IsCandidate(string value)
    {
        if (value == null || value.Length > 80)
            return false;
        foreach (var c in value)
        {
            if (char.IsControl(c) || c is '/' or '\\')
                return false;
        }
        return value != "." && value != "..";
    }
}
