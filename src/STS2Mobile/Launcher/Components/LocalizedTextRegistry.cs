using System;
using System.Collections.Generic;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Watches only the launcher's shared styled controls. Dynamic text assigned by
// upstream controllers is translated without modifying those high-churn files.
internal static class LocalizedTextRegistry
{
    private static readonly List<WatchedText> Watched = new();

    public static void Watch(Label label)
    {
        Watch(label, WatchedProperty.Text);
    }

    public static void Watch(Button button)
    {
        Watch(button, WatchedProperty.Text);
        Watch(button, WatchedProperty.Tooltip);
    }

    public static void Watch(LineEdit lineEdit)
    {
        Watch(lineEdit, WatchedProperty.Placeholder);
    }

    public static void Refresh(bool useEnglish)
    {
        for (var i = Watched.Count - 1; i >= 0; i--)
        {
            var item = Watched[i];
            if (!item.Target.TryGetTarget(out var target) || !GodotObject.IsInstanceValid(target))
            {
                Watched.RemoveAt(i);
                continue;
            }

            var current = GetText(target, item.Property);
            if (useEnglish)
            {
                if (!EnglishLocalization.ContainsKorean(current))
                    continue;

                var translated = EnglishLocalization.Translate(current);
                if (translated == current)
                    continue;

                item.LastKorean = current;
                item.LastEnglish = translated;
                SetText(target, item.Property, translated);
            }
            else if (
                item.LastKorean != null
                && item.LastEnglish != null
                && current == item.LastEnglish
            )
            {
                SetText(target, item.Property, item.LastKorean);
            }
            else
            {
                var korean = EnglishLocalization.RestoreKorean(current);
                if (korean != current)
                    SetText(target, item.Property, korean);
            }
        }
    }

    private static void Watch(Control control, WatchedProperty property)
    {
        var item = new WatchedText(control, property);
        Watched.Add(item);

        if (!Loc.IsEnglish)
            return;

        var current = GetText(control, property);
        var translated = EnglishLocalization.Translate(current);
        if (translated == current)
            return;

        item.LastKorean = current;
        item.LastEnglish = translated;
        SetText(control, property, translated);
    }

    private static string GetText(Control control, WatchedProperty property) =>
        property switch
        {
            WatchedProperty.Text when control is Label label => label.Text,
            WatchedProperty.Text when control is Button button => button.Text,
            WatchedProperty.Placeholder when control is LineEdit lineEdit => lineEdit.PlaceholderText,
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
        public string LastKorean;
        public string LastEnglish;

        public WatchedText(Control target, WatchedProperty property)
        {
            Target = new WeakReference<Control>(target);
            Property = property;
        }
    }
}
