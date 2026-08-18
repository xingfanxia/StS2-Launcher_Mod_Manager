using System.Threading.Tasks;

namespace STS2Mobile.Launcher.Components;

// One-shot result used by modal dialogs that are awaited by launcher flows.
// Android Back and parent teardown remove a dialog without invoking its button
// handlers, so every dialog wires CompleteFallback to TreeExiting.
internal sealed class DialogCompletion<T>
{
    private readonly T _fallback;
    private readonly TaskCompletionSource<T> _source = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public DialogCompletion(T fallback) => _fallback = fallback;

    public Task<T> Task => _source.Task;

    public void Complete(T result) => _source.TrySetResult(result);

    public void CompleteFallback() => _source.TrySetResult(_fallback);
}
