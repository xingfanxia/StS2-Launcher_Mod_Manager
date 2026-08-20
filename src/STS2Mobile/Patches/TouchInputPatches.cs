using System;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace STS2Mobile.Patches;

// Cancels card play when the touch is released outside the play zone.
// The desktop game relies on mouse-up position, but on mobile the drag target
// can drift below the play zone threshold during a swipe.
public static class TouchInputPatches
{
    private static readonly TwoFingerTapGesture TwoFingerTap = new();

    public static void Apply(Harmony harmony)
    {
        PatchHelper.Patch(
            harmony,
            typeof(MegaCrit.Sts2.Core.Nodes.NGame),
            "_Input",
            prefix: PatchHelper.Method(typeof(TouchInputPatches), nameof(GameInputPrefix))
        );

        var mouseCardPlayType = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly.GetType(
            "MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay"
        );
        if (mouseCardPlayType != null)
        {
            PatchHelper.Patch(
                harmony,
                mouseCardPlayType,
                "_Input",
                postfix: PatchHelper.Method(
                    typeof(TouchInputPatches),
                    nameof(MouseCardPlayInputPostfix)
                )
            );
        }
    }

    // Global game input prefix. Godot dispatches raw ScreenTouch/ScreenDrag
    // events here before GUI handling, while the first finger also produces an
    // emulated primary mouse sequence. Once a second finger takes over, consume
    // the raw gesture and its primary release, then enqueue one centered right
    // mouse press/release pair.
    public static bool GameInputPrefix(MegaCrit.Sts2.Core.Nodes.NGame __instance, object __0)
    {
        try
        {
            var inputEvent = (InputEvent)__0;
            ulong now = Time.GetTicksMsec();

            if (inputEvent is InputEventScreenTouch touch)
            {
                var result = TwoFingerTap.Touch(
                    touch.Index,
                    touch.Pressed,
                    touch.Position.X,
                    touch.Position.Y,
                    now
                );
                if (!result.ConsumeOriginal)
                    return true;

                ConsumeInput(__instance);
                if (result.EmitRightClick)
                {
                    EmitRightClick(new Vector2(result.X, result.Y));
                    PatchHelper.Log("[Input] two-finger right click");
                }
                return false;
            }

            if (inputEvent is InputEventScreenDrag drag)
            {
                if (!TwoFingerTap.Move(drag.Index, drag.Position.X, drag.Position.Y, now))
                    return true;

                ConsumeInput(__instance);
                return false;
            }

            if (
                inputEvent is InputEventMouseButton mouseButton
                && mouseButton.ButtonIndex == MouseButton.Left
                && TwoFingerTap.SuppressPrimaryEvent(mouseButton.Pressed, now)
            )
            {
                ConsumeInput(__instance);
                return false;
            }
        }
        catch (Exception ex)
        {
            TwoFingerTap.Reset();
            PatchHelper.Log($"GameInputPrefix: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    private static void ConsumeInput(MegaCrit.Sts2.Core.Nodes.NGame game)
    {
        game.GetViewport()?.SetInputAsHandled();
    }

    private static void EmitRightClick(Vector2 position)
    {
        using var pressed = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = true,
            Position = position,
            GlobalPosition = position,
        };
        using var released = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = false,
            Position = position,
            GlobalPosition = position,
        };
        Input.ParseInputEvent(pressed);
        Input.ParseInputEvent(released);
    }

    // On left mouse button release, check if the card is still in the play zone.
    // If not, cancel the card play to prevent accidental plays from imprecise touches.
    public static void MouseCardPlayInputPostfix(object __instance, object inputEvent)
    {
        try
        {
            var inputEvt = (InputEvent)inputEvent;
            if (
                inputEvt is InputEventMouseButton mouseBtn
                && mouseBtn.ButtonIndex == MouseButton.Left
                && mouseBtn.IsReleased()
            )
            {
                var instanceType = __instance.GetType();

                var isInPlayZone = instanceType.GetMethod(
                    "IsCardInPlayZone",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (isInPlayZone == null)
                    return;

                bool inPlayZone = (bool)isInPlayZone.Invoke(__instance, null);

                if (!inPlayZone)
                {
                    var cancelMethod = instanceType.GetMethod(
                        "CancelPlayCard",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                    cancelMethod?.Invoke(__instance, null);
                    PatchHelper.Log("Card play cancelled: touch released below play zone");
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"MouseCardPlayInputPostfix: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
