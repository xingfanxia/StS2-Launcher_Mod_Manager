using System;
using System.Collections.Generic;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Watches the launcher's shared styled controls and registered dropdown items.
// Dynamic text assigned by upstream controllers is translated without modifying
// those high-churn files. Provenance prevents rewriting third-party text.
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

    public static void Watch(
        OptionButton optionButton,
        int itemIndex,
        TextProvenance provenance
    )
    {
        Watch(optionButton, WatchedProperty.OptionItem, provenance, itemIndex);
    }

    public static LocalizationAuditSnapshot Refresh(LauncherLanguage language)
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

            var current = GetText(target, item.Property, item.ItemIndex);
            if (item.LastRendered == null || current != item.LastRendered)
                item.SourceText = current;

            var rendered = LocalizedTextPolicy.Render(item.SourceText, language, item.Provenance);
            if (rendered != current)
            {
                SetText(target, item.Property, rendered, item.ItemIndex);
                current = rendered;
            }
            item.LastRendered = current;

            if (!IsVisibleText(target, current))
                continue;

            visibleText++;
            if (language != LauncherLanguage.Korean)
            {
                if (
                    LocalizedTextPolicy.IsUntranslatedLauncherText(
                        current,
                        language,
                        item.Provenance
                    )
                )
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

    private static void Watch(
        Control control,
        WatchedProperty property,
        TextProvenance provenance,
        int itemIndex = -1
    )
    {
        var item = new WatchedText(control, property, provenance, itemIndex);
        Watched.Add(item);

        var current = GetText(control, property, itemIndex);
        item.SourceText = current;
        var rendered = LocalizedTextPolicy.Render(current, Loc.CurrentLanguage, provenance);
        if (rendered == current)
            return;

        item.LastRendered = rendered;
        SetText(control, property, rendered, itemIndex);
    }

    private static bool IsVisibleText(Control control, string value) =>
        !string.IsNullOrEmpty(value) && control.IsVisibleInTree();

    private static string GetText(Control control, WatchedProperty property, int itemIndex = -1) =>
        property switch
        {
            WatchedProperty.Text when control is Label label => label.Text,
            WatchedProperty.Text when control is Button button => button.Text,
            WatchedProperty.Placeholder when control is LineEdit lineEdit =>
                lineEdit.PlaceholderText,
            WatchedProperty.Tooltip => control.TooltipText,
            WatchedProperty.OptionItem when control is OptionButton optionButton
                && itemIndex >= 0
                && itemIndex < optionButton.ItemCount => optionButton.GetItemText(itemIndex),
            _ => "",
        };

    private static void SetText(
        Control control,
        WatchedProperty property,
        string value,
        int itemIndex = -1
    )
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
            case WatchedProperty.OptionItem when control is OptionButton optionButton
                && itemIndex >= 0
                && itemIndex < optionButton.ItemCount:
                optionButton.SetItemText(itemIndex, value);
                break;
        }
    }

    private enum WatchedProperty
    {
        Text,
        Placeholder,
        Tooltip,
        OptionItem,
    }

    private sealed class WatchedText
    {
        public readonly WeakReference<Control> Target;
        public readonly WatchedProperty Property;
        public readonly TextProvenance Provenance;
        public readonly int ItemIndex;
        public string SourceText;
        public string LastRendered;

        public WatchedText(
            Control target,
            WatchedProperty property,
            TextProvenance provenance,
            int itemIndex
        )
        {
            Target = new WeakReference<Control>(target);
            Property = property;
            Provenance = provenance;
            ItemIndex = itemIndex;
        }
    }
}

public readonly record struct LocalizationAuditSnapshot(
    int VisibleText,
    int UntranslatedLauncherText,
    int PreservedExternalText
);
