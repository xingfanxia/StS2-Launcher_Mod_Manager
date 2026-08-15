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

    private readonly string _version;

    public ShaderWarmupState(string dataDirectory, int version)
    {
        _version = version.ToString();
        CompletedMarkerPath = Path.Combine(dataDirectory, CompletedMarkerName);
        AttemptMarkerPath = Path.Combine(dataDirectory, AttemptMarkerName);
    }

    internal string CompletedMarkerPath { get; }
    internal string AttemptMarkerPath { get; }

    public ShaderWarmupCheck Check()
    {
        if (MarkerMatches(CompletedMarkerPath))
        {
            DeleteAttemptMarker();
            return ShaderWarmupCheck.Completed();
        }

        if (MarkerMatches(AttemptMarkerPath))
        {
            // The previous process died after Begin() but before Complete().
            // Shader precompilation is an optimization, so permanently skip
            // this warmup version and let a clean process start the game.
            Complete();
            return ShaderWarmupCheck.Recovered();
        }

        return ShaderWarmupCheck.Required();
    }

    public void Begin() => WriteMarkerAtomically(AttemptMarkerPath);

    public void Complete()
    {
        // Publish completion before removing the attempt marker. If the process
        // dies between these operations, Check() still observes completion.
        WriteMarkerAtomically(CompletedMarkerPath);
        DeleteAttemptMarker();
    }

    private bool MarkerMatches(string path) =>
        File.Exists(path) && File.ReadAllText(path).Trim() == _version;

    private void WriteMarkerAtomically(string path)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, _version);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private void DeleteAttemptMarker()
    {
        if (File.Exists(AttemptMarkerPath))
            File.Delete(AttemptMarkerPath);
    }
}

internal readonly struct ShaderWarmupCheck
{
    private ShaderWarmupCheck(bool needsWarmup, bool recovered, string reason)
    {
        NeedsWarmup = needsWarmup;
        RecoveredInterruptedAttempt = recovered;
        Reason = reason;
    }

    public bool NeedsWarmup { get; }
    public bool RecoveredInterruptedAttempt { get; }
    public string Reason { get; }

    public static ShaderWarmupCheck Required() =>
        new(true, false, "no marker for the current warmup version");

    public static ShaderWarmupCheck Completed() =>
        new(false, false, "current warmup version already completed");

    public static ShaderWarmupCheck Recovered() =>
        new(false, true, "previous warmup attempt was interrupted");
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
