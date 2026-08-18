using System;
using System.Collections.Generic;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Watches only the launcher's shared styled controls. Dynamic text assigned by
// upstream controllers is translated without modifying those high-churn files.
// Provenance prevents the localization layer from rewriting third-party text.
internal static class LocalizedTextRegistry
{
    private static readonly List<WatchedText> Watched = new();

    public static void Watch(Label label, TextProvenance provenance)
    {
        Watch(label, WatchedProperty.Text, provenance);
    }

    public static void Watch(Button button, TextProvenance provenance)
    {
        Watch(button, WatchedProperty.Text, provenance);
        Watch(button, WatchedProperty.Tooltip, provenance);
    }

    public static void Watch(LineEdit lineEdit, TextProvenance provenance)
    {
        Watch(lineEdit, WatchedProperty.Placeholder, provenance);
    }

    public static LocalizationAuditSnapshot Refresh(bool useEnglish)
    {
        var visibleText = 0;
        var untranslatedLauncherText = 0;
        var preservedExternalText = 0;

        for (var i = Watched.Count - 1; i >= 0; i--)
        {
            var item = Watched[i];
            if (!item.Target.TryGetTarget(out var target) || !GodotObject.IsInstanceValid(target))
            {
                Watched.RemoveAt(i);
                continue;
            }

            var current = GetText(target, item.Property);
            var rendered = LocalizedTextPolicy.Render(current, useEnglish, item.Provenance);
            if (rendered != current)
            {
                if (useEnglish)
                {
                    item.LastKorean = current;
                    item.LastEnglish = rendered;
                }
                SetText(target, item.Property, rendered);
                current = rendered;
            }
            else if (
                !useEnglish
                && item.LastKorean != null
                && item.LastEnglish != null
                && current == item.LastEnglish
            )
            {
                SetText(target, item.Property, item.LastKorean);
                current = item.LastKorean;
            }

            if (!IsVisibleText(target, current))
                continue;

            visibleText++;
            if (useEnglish)
            {
                if (LocalizedTextPolicy.IsUntranslatedLauncherText(current, item.Provenance))
                    untranslatedLauncherText++;
                else if (LocalizedTextPolicy.IsPreservedExternalText(current, item.Provenance))
                    preservedExternalText++;
            }
        }

        return new LocalizationAuditSnapshot(
            visibleText,
            untranslatedLauncherText,
            preservedExternalText
        );
    }

    private static void Watch(Control control, WatchedProperty property, TextProvenance provenance)
    {
        var item = new WatchedText(control, property, provenance);
        Watched.Add(item);

        var current = GetText(control, property);
        var rendered = LocalizedTextPolicy.Render(current, Loc.IsEnglish, provenance);
        if (rendered == current)
            return;

        item.LastKorean = current;
        item.LastEnglish = rendered;
        SetText(control, property, rendered);
    }

    private static bool IsVisibleText(Control control, string value) =>
        !string.IsNullOrEmpty(value) && control.IsVisibleInTree();

    private static string GetText(Control control, WatchedProperty property) =>
        property switch
        {
            WatchedProperty.Text when control is Label label => label.Text,
            WatchedProperty.Text when control is Button button => button.Text,
            WatchedProperty.Placeholder when control is LineEdit lineEdit =>
                lineEdit.PlaceholderText,
            WatchedProperty.Tooltip => control.TooltipText,
            _ => "",
        };

    private static void SetText(Control control, WatchedProperty property, string value)
    {
        switch (property)
        {
            case WatchedProperty.Text when control is Label label:
                label.Text = value;
                break;
            case WatchedProperty.Text when control is Button button:
                button.Text = value;
                break;
            case WatchedProperty.Placeholder when control is LineEdit lineEdit:
                lineEdit.PlaceholderText = value;
                break;
            case WatchedProperty.Tooltip:
                control.TooltipText = value;
                break;
        }
    }

    private enum WatchedProperty
    {
        Text,
        Placeholder,
        Tooltip,
    }

    private sealed class WatchedText
    {
        public readonly WeakReference<Control> Target;
        public readonly WatchedProperty Property;
        public readonly TextProvenance Provenance;
        public string LastKorean;
        public string LastEnglish;

        public WatchedText(Control target, WatchedProperty property, TextProvenance provenance)
        {
            Target = new WeakReference<Control>(target);
            Property = property;
            Provenance = provenance;
        }
    }
}

public readonly record struct LocalizationAuditSnapshot(
    int VisibleText,
    int UntranslatedLauncherText,
    int PreservedExternalText
);
