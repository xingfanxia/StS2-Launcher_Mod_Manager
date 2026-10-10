using Godot;

namespace STS2Mobile.Platform;

// A finger cannot hover, so whatever was last touched stays "hovered" and the
// game keeps its tooltip on screen, over the end-turn button or the hand, until
// something else is touched. A few seconds after the finger lifts, the emulated
// pointer is moved off screen so the tooltip closes by itself.
internal static class IosTouchHover
{
    private const double LingerSeconds = 3;
    private static bool _wasPressed;
    private static double _releasedAt = -1;

    public static void Start() => Callable.From(Attach).CallDeferred();

    private static void Attach()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Callable.From(Attach).CallDeferred();
            return;
        }
        tree.ProcessFrame += OnFrame;
    }

    private static void OnFrame()
    {
        var pressed = Input.IsMouseButtonPressed(MouseButton.Left);
        var now = Time.GetTicksMsec() / 1000.0;
        if (pressed)
            _releasedAt = -1;
        else if (_wasPressed)
            _releasedAt = now;
        _wasPressed = pressed;

        if (_releasedAt < 0 || now - _releasedAt < LingerSeconds)
            return;
        _releasedAt = -1;
        var away = new Vector2(-64, -64);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = away, GlobalPosition = away });
    }
}
