using System;
using Godot;

namespace STS2Mobile.Launcher;

internal enum LoginAutofillField
{
    Username,
    Password,
}

// This bridge carries only a closed field-kind token. Credential values stay in
// Godot's existing native text-input path and are never read, stored, or logged here.
internal static class AndroidLoginAutofillBridge
{
    public static void Configure(LoginAutofillField field, Control anchor)
    {
        if (!OS.HasFeature("android") || anchor == null)
            return;

        var fieldType = field switch
        {
            LoginAutofillField.Username => "username",
            LoginAutofillField.Password => "password",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var anchorRect = anchor.GetGlobalRect();
        var viewportRect = anchor.GetViewportRect();
        if (viewportRect.Size.X <= 0 || viewportRect.Size.Y <= 0)
            return;

        var left = Mathf.Clamp(
            (anchorRect.Position.X - viewportRect.Position.X) / viewportRect.Size.X,
            0.0f,
            1.0f
        );
        var top = Mathf.Clamp(
            (anchorRect.Position.Y - viewportRect.Position.Y) / viewportRect.Size.Y,
            0.0f,
            1.0f
        );
        var right = Mathf.Clamp(
            (anchorRect.End.X - viewportRect.Position.X) / viewportRect.Size.X,
            0.0f,
            1.0f
        );
        var bottom = Mathf.Clamp(
            (anchorRect.End.Y - viewportRect.Position.Y) / viewportRect.Size.Y,
            0.0f,
            1.0f
        );
        if (right <= left || bottom <= top)
            return;

        var normalizedAnchor = FormattableString.Invariant(
            $"{left:F6}|{top:F6}|{right:F6}|{bottom:F6}"
        );

        try
        {
            LauncherModel
                .GetGodotApp()
                ?.Call("configureLoginAutofill", fieldType, normalizedAnchor);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Autofill] configure bridge unavailable: {ex.GetType().Name}");
        }
    }

    public static void Clear()
    {
        if (!OS.HasFeature("android"))
            return;

        try
        {
            LauncherModel.GetGodotApp()?.Call("clearLoginAutofill");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Autofill] clear bridge unavailable: {ex.GetType().Name}");
        }
    }
}
