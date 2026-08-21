using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace STS2Mobile.Patches;

// Resolves a completed two-finger tap to one desktop-equivalent action. Keep
// this separate from the Harmony entry point so upstream input patches retain
// a small merge surface.
public static class TwoFingerRightClickDispatcher
{
    private const int GodotInternalDeviceId = -2;
    private static Control _target;

    public static void Reset()
    {
        _target = null;
    }

    public static void Capture(MegaCrit.Sts2.Core.Nodes.NGame game, Vector2 position)
    {
        var connectedGui = FindConnectedGuiTargetAt(game.GetTree()?.Root, position);
        if (connectedGui != null)
        {
            _target = connectedGui;
            return;
        }

        var handCard = FindHandCardAt(position);
        if (handCard != null)
        {
            _target = handCard;
            return;
        }

        var hovered = game.GetViewport()?.GuiGetHoveredControl();
        _target = IsUsableTarget(hovered) ? hovered : null;
    }

    public static void Complete(bool emitRightClick, Vector2 position)
    {
        var target = TakeTarget();
        if (emitRightClick)
            DispatchRightClick(target, position);
    }

    // Touch input does not reliably update Godot's hovered-control cache before
    // the second finger arrives. Resolve combat cards from their real hitboxes
    // so the gesture works even when GuiGetHoveredControl() is null.
    private static NHandCardHolder FindHandCardAt(Vector2 position)
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !GodotObject.IsInstanceValid(hand))
            return null;

        if (hand.InCardPlay)
        {
            foreach (var child in hand.GetChildren())
            {
                if (
                    child is NCardPlay currentCardPlay
                    && GodotObject.IsInstanceValid(currentCardPlay)
                    && currentCardPlay.Holder != null
                    && GodotObject.IsInstanceValid(currentCardPlay.Holder)
                )
                    return currentCardPlay.Holder;
            }
        }

        var focused = hand.FocusedHolder;
        if (
            focused != null
            && GodotObject.IsInstanceValid(focused)
            && focused.IsInsideTree()
            && focused.CardModel != null
            && (hand.InCardPlay || ContainsPoint(focused.Hitbox, position))
        )
            return focused;

        NHandCardHolder best = null;
        int bestZIndex = int.MinValue;
        foreach (var holder in hand.ActiveHolders)
        {
            if (
                holder == null
                || !GodotObject.IsInstanceValid(holder)
                || !holder.IsVisibleInTree()
                || holder.CardModel == null
                || !ContainsPoint(holder.Hitbox, position)
            )
                continue;

            if (best == null || holder.ZIndex >= bestZIndex)
            {
                best = holder;
                bestZIndex = holder.ZIndex;
            }
        }
        return best;
    }

    // Mods commonly attach right-click behavior directly to Control.GuiInput.
    // Prefer the smallest visible connected control under the touch instead of
    // a stale hover cache or a full-screen parent panel.
    private static Control FindConnectedGuiTargetAt(Node root, Vector2 position)
    {
        if (root == null || !GodotObject.IsInstanceValid(root))
            return null;

        Control best = null;
        float bestArea = float.PositiveInfinity;
        FindConnectedGuiTargetAt(root, position, ref best, ref bestArea);
        return best;
    }

    private static void FindConnectedGuiTargetAt(
        Node node,
        Vector2 position,
        ref Control best,
        ref float bestArea
    )
    {
        if (node is Control control && IsUsableTarget(control) && control.IsVisibleInTree())
        {
            if (
                control.MouseFilter != Control.MouseFilterEnum.Ignore
                && control.GetSignalConnectionList(Control.SignalName.GuiInput).Count > 0
                && ContainsPoint(control, position)
            )
            {
                float area = MathF.Abs(control.Size.X * control.Size.Y);
                if (area > 0f && area <= bestArea)
                {
                    best = control;
                    bestArea = area;
                }
            }
        }

        foreach (var child in node.GetChildren())
            FindConnectedGuiTargetAt(child, position, ref best, ref bestArea);
    }

    private static bool ContainsPoint(Control control, Vector2 globalPosition)
    {
        if (!IsUsableTarget(control))
            return false;
        var localPosition = control.GetGlobalTransformWithCanvas().AffineInverse() * globalPosition;
        return control._HasPoint(localPosition);
    }

    private static Control TakeTarget()
    {
        var target = _target;
        _target = null;
        return IsUsableTarget(target) ? target : null;
    }

    private static bool IsUsableTarget(Control target) =>
        target != null && GodotObject.IsInstanceValid(target) && target.IsInsideTree();

    private static void DispatchRightClick(Control target, Vector2 position)
    {
        if (TryOpenCombatCardDetail(target))
        {
            PatchHelper.Log("[Input] two-finger right click route=card-detail");
            return;
        }

        if (IsUsableTarget(target))
        {
            CancelCapturedPrimaryPress(target);
            EmitGuiRightClick(target, position);
            PatchHelper.Log("[Input] two-finger right click route=gui");
            return;
        }

        EmitGlobalRightClick(position);
        PatchHelper.Log("[Input] two-finger right click route=global");
    }

    // The first touch has already produced an emulated left-button press by
    // the time the second finger identifies this as a right click. Release it
    // outside the captured control so pressed/drag-pending state is cleared
    // without running the control's ordinary left-click action.
    private static void CancelCapturedPrimaryPress(Control target)
    {
        var outsideGlobal = new Vector2(-1_000_000f, -1_000_000f);
        var outsideLocal = target.GetGlobalTransformWithCanvas().AffineInverse() * outsideGlobal;
        using var released = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = 0,
            Pressed = false,
            Position = outsideLocal,
            GlobalPosition = outsideGlobal,
        };
        EmitGuiInput(target, released);
    }

    private static bool TryOpenCombatCardDetail(Control target)
    {
        var current = target as Node;
        while (current != null && current is not NHandCardHolder)
            current = current.GetParent();
        if (current is not NHandCardHolder holder || holder.CardModel == null)
            return false;

        var card = holder.CardModel;
        NPlayerHand.Instance?.CancelAllCardPlay();
        Callable
            .From(() =>
            {
                var game = MegaCrit.Sts2.Core.Nodes.NGame.Instance;
                if (game == null || !GodotObject.IsInstanceValid(game))
                    return;
                game.GetInspectCardScreen()
                    .Open(new List<MegaCrit.Sts2.Core.Models.CardModel> { card }, 0);
            })
            .CallDeferred();
        return true;
    }

    private static void EmitGlobalRightClick(Vector2 position)
    {
        using var pressed = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            ButtonMask = MouseButtonMask.Right,
            Device = GodotInternalDeviceId,
            Pressed = true,
            Position = position,
            GlobalPosition = position,
        };
        using var released = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            ButtonMask = 0,
            Device = GodotInternalDeviceId,
            Pressed = false,
            Position = position,
            GlobalPosition = position,
        };
        Input.ParseInputEvent(pressed);
        Input.ParseInputEvent(released);
    }

    private static void EmitGuiRightClick(Control target, Vector2 globalPosition)
    {
        var localPosition = target.GetGlobalTransformWithCanvas().AffineInverse() * globalPosition;
        using var pressed = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            ButtonMask = MouseButtonMask.Right,
            Pressed = true,
            Position = localPosition,
            GlobalPosition = globalPosition,
        };
        using var released = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            ButtonMask = 0,
            Pressed = false,
            Position = localPosition,
            GlobalPosition = globalPosition,
        };
        EmitGuiInput(target, pressed);
        EmitGuiInput(target, released);
    }

    private static void EmitGuiInput(Control target, InputEventMouseButton mouseButton)
    {
        if (target.GetSignalConnectionList(Control.SignalName.GuiInput).Count > 0)
            target.EmitSignal(Control.SignalName.GuiInput, mouseButton);
        else if (IsUsableTarget(target))
            target._GuiInput(mouseButton);
    }
}
