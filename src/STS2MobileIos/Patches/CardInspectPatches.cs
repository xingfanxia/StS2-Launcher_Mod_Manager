using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace STS2MobileIos.Patches;

public static class CardInspectPatches
{
    private static readonly LongPressGesture Gesture = new();
    private static NCardHolder _holder;
    private static bool _consumeRelease;

    public static void PressPostfix(NCardHolder __instance, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            || __instance.CardModel == null || __instance is NHandCardHolder)
            return;
        if (PatchHelper.Field(typeof(NCardHolder), "_isClickable")?.GetValue(__instance) is not true)
            return;
        _holder = __instance;
        var position = __instance.GetViewport().GetMousePosition();
        Gesture.Begin(Time.GetTicksUsec(), position.X, position.Y);
    }

    public static bool ReleasePrefix(NCardHolder __instance, InputEvent inputEvent)
    {
        if (__instance != _holder)
            return true;
        Gesture.Cancel();
        return !_consumeRelease;
    }

    public static bool GameInputPrefix(Node __instance, InputEvent inputEvent)
    {
        if (inputEvent is InputEventScreenTouch { Pressed: true, Index: > 0 })
        {
            Gesture.Cancel();
            return true;
        }
        if (!_consumeRelease)
            return true;
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _consumeRelease = false;
            _holder = null;
            __instance.GetViewport().SetInputAsHandled();
            return false;
        }
        if (inputEvent is InputEventScreenTouch { Pressed: false })
            __instance.GetViewport().SetInputAsHandled();
        return true;
    }

    public static void Tick()
    {
        if (_holder == null || !GodotObject.IsInstanceValid(_holder) || !_holder.IsVisibleInTree()
            || _holder.GetTree().Paused)
        {
            Cancel();
            return;
        }
        var position = _holder.GetViewport().GetMousePosition();
        if (!Gesture.Update(Time.GetTicksUsec(), position.X, position.Y, Input.IsMouseButtonPressed(MouseButton.Left)))
            return;
        var model = _holder.CardModel;
        if (model == null)
            return;
        _consumeRelease = true;
        // Clear the pending left click so releasing cannot select, buy, or remove this card.
        PatchHelper.Field(typeof(NCardHolder), "_currentPressedAction")?.SetValue(_holder, null);
        NGame.Instance.GetInspectCardScreen().Open(new() { model }, 0, viewAllUpgraded: true);
    }

    public static void Cancel()
    {
        Gesture.Cancel();
        _holder = null;
        _consumeRelease = false;
    }
}
