using Godot;

namespace STS2Mobile.Launcher.Components;

// Keeps game input and SceneTree auto-quit disabled while a full-screen startup
// transition owns the display after LauncherUI has been freed. Cloud sync and
// shader warmup can overlap for one deferred frame, so this is reference-counted.
public static class StartupInputGate
{
    private static int _holders;
    private static SceneTree _tree;

    public static bool Active => _holders > 0;

    public static System.IDisposable Hold(Node owner)
    {
        Enter(owner);
        return new Lease(owner);
    }

    public static void Enter(Node owner)
    {
        _holders++;
        if (_holders != 1)
            return;

        try
        {
            _tree = owner?.GetTree();
            if (_tree != null)
                _tree.AutoAcceptQuit = false;
        }
        catch (System.Exception ex)
        {
            PatchHelper.Log($"[StartupInputGate] Enter failed: {ex.Message}");
        }
    }

    public static void Exit(Node owner)
    {
        if (_holders <= 0)
            return;

        _holders--;
        if (_holders != 0)
            return;

        try
        {
            var tree = owner?.GetTree() ?? _tree;
            if (tree != null && GodotObject.IsInstanceValid(tree))
                tree.AutoAcceptQuit = true;
        }
        catch (System.Exception ex)
        {
            PatchHelper.Log($"[StartupInputGate] Exit failed: {ex.Message}");
        }
        finally
        {
            _tree = null;
        }
    }

    // WM_GO_BACK_REQUEST is broadcast rather than consumed by one node. During a
    // transition, close the existing top modal (for example a cloud conflict)
    // or swallow Back so it cannot quit the half-initialized game underneath.
    public static void HandleBack()
    {
        if (!Active)
            return;

        if (ModalGate.TryHandleBack())
            PatchHelper.Log("[StartupInputGate] Back closed the top startup modal");
        else
            PatchHelper.Log("[StartupInputGate] Back ignored during startup transition");
    }

    private sealed class Lease : System.IDisposable
    {
        private Node _owner;

        public Lease(Node owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner == null)
                return;

            var owner = _owner;
            _owner = null;
            Exit(owner);
        }
    }
}
