using System.IO;
using System.Threading.Tasks;

namespace STS2Mobile.Launcher;

// Persistent state for the optional first-run shader warmup. The in-progress
// marker is deliberately written before resource scanning starts: if Android
// kills the process for native/OOM reasons, the next process can distinguish an
// interrupted warmup from a fresh install and avoid repeating the same crash.
internal sealed class ShaderWarmupState
{
    private const string CompletedMarkerName = "shader_warmup_version";
    private const string AttemptMarkerName = "shader_warmup_in_progress";
    private const string ResultMarkerName = "shader_warmup_result";

    private readonly string _version;

    public ShaderWarmupState(string dataDirectory, int version)
    {
        _version = version.ToString();
        CompletedMarkerPath = Path.Combine(dataDirectory, CompletedMarkerName);
        AttemptMarkerPath = Path.Combine(dataDirectory, AttemptMarkerName);
        ResultMarkerPath = Path.Combine(dataDirectory, ResultMarkerName);
    }

    internal string CompletedMarkerPath { get; }
    internal string AttemptMarkerPath { get; }
    internal string ResultMarkerPath { get; }

    public ShaderWarmupCheck Check()
    {
        if (MarkerMatches(CompletedMarkerPath))
        {
            DeleteAttemptMarker();
            return ReadCompletedResult();
        }

        if (MarkerMatches(AttemptMarkerPath))
        {
            // The previous process died after Begin() but before Complete().
            // Shader precompilation is an optimization, so permanently skip
            // this warmup version and let a clean process start the game.
            Complete(
                ShaderWarmupOutcome.Interrupted,
                "previous warmup process ended before publishing a result"
            );
            return ShaderWarmupCheck.Recovered();
        }

        return ShaderWarmupCheck.Required();
    }

    public void Begin() => WriteMarkerAtomically(AttemptMarkerPath);

    public void Complete() =>
        Complete(ShaderWarmupOutcome.Completed, "all scheduled shaders were processed");

    public void Complete(ShaderWarmupOutcome outcome, string reason)
    {
        // Publish the diagnostic result and then completion before removing the
        // attempt marker. If the process dies between writes, Check() observes
        // the attempt and safely records an interrupted/bypassed result.
        WriteTextAtomically(ResultMarkerPath, $"{_version}\n{outcome}\n{SanitizeReason(reason)}");
        WriteMarkerAtomically(CompletedMarkerPath);
        DeleteAttemptMarker();
    }

    private ShaderWarmupCheck ReadCompletedResult()
    {
        try
        {
            var fields = File.ReadAllLines(ResultMarkerPath);
            if (
                fields.Length >= 2
                && fields[0].Trim() == _version
                && System.Enum.TryParse<ShaderWarmupOutcome>(fields[1], out var outcome)
            )
            {
                var reason = fields.Length >= 3 ? fields[2] : "result reason unavailable";
                return ShaderWarmupCheck.Completed(outcome, reason);
            }
        }
        catch
        {
            // Older installations have only the version marker. Completion is
            // still authoritative; the additional result is diagnostic only.
        }

        return ShaderWarmupCheck.Completed(
            ShaderWarmupOutcome.Completed,
            "current warmup version already completed"
        );
    }

    private bool MarkerMatches(string path) =>
        File.Exists(path) && File.ReadAllText(path).Trim() == _version;

    private void WriteMarkerAtomically(string path)
    {
        WriteTextAtomically(path, _version);
    }

    private static void WriteTextAtomically(string path, string content)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string SanitizeReason(string reason)
    {
        var sanitized = (reason ?? "reason unavailable").Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length <= 200 ? sanitized : sanitized[..200];
    }

    private void DeleteAttemptMarker()
    {
        if (File.Exists(AttemptMarkerPath))
            File.Delete(AttemptMarkerPath);
    }
}

internal readonly struct ShaderWarmupCheck
{
    private ShaderWarmupCheck(
        bool needsWarmup,
        bool recovered,
        ShaderWarmupOutcome? outcome,
        string reason
    )
    {
        NeedsWarmup = needsWarmup;
        RecoveredInterruptedAttempt = recovered;
        Outcome = outcome;
        Reason = reason;
    }

    public bool NeedsWarmup { get; }
    public bool RecoveredInterruptedAttempt { get; }
    public ShaderWarmupOutcome? Outcome { get; }
    public string Reason { get; }

    public static ShaderWarmupCheck Required() =>
        new(true, false, null, "no marker for the current warmup version");

    public static ShaderWarmupCheck Completed(ShaderWarmupOutcome outcome, string reason) =>
        new(false, false, outcome, reason);

    public static ShaderWarmupCheck Recovered() =>
        new(
            false,
            true,
            ShaderWarmupOutcome.Interrupted,
            "previous warmup attempt was interrupted"
        );
}

internal enum ShaderWarmupOutcome
{
    Completed,
    DeferredMemoryPressure,
    FailedButBypassed,
    Interrupted,
}

// The completion source exists before Initialize() can produce a result. This
// prevents synchronous UI initialization failures from being lost merely
// because the caller has not started awaiting yet.
internal sealed class ShaderWarmupOperation
{
    private readonly TaskCompletionSource<bool> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public Task<bool> Completion => _completion.Task;

    public void Complete(bool restartRequired) => _completion.TrySetResult(restartRequired);
}
