using System.Collections.Generic;
using Godot;

namespace STS2Mobile.Launcher.Components;

public class LogView : RichTextLabel
{
    private readonly List<LogEntry> _entries = new();
    private LauncherLanguage _renderedLanguage;

    public LogView(float scale)
    {
        CustomMinimumSize = new Vector2(0, (int)(120 * scale));
        ScrollFollowing = true;
        BbcodeEnabled = true;
        AddThemeFontSizeOverride("normal_font_size", (int)(11 * scale));
        AddThemeColorOverride("default_color", new Color(0.6f, 0.6f, 0.65f));

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.05f, 0.05f, 0.07f);
        bg.SetCornerRadiusAll((int)(4 * scale));
        bg.ContentMarginLeft = (int)(8 * scale);
        bg.ContentMarginRight = (int)(8 * scale);
        bg.ContentMarginTop = (int)(4 * scale);
        bg.ContentMarginBottom = (int)(4 * scale);
        AddThemeStyleboxOverride("normal", bg);

        _renderedLanguage = Loc.CurrentLanguage;
        var languageTimer = new Timer { WaitTime = 0.25, Autostart = true };
        languageTimer.Timeout += RefreshLanguage;
        AddChild(languageTimer);
    }

    public void AppendLog(
        string msg,
        TextProvenance provenance = TextProvenance.LauncherAuthored
    )
    {
        _entries.Add(new LogEntry(msg, provenance, null));
        AddText(Loc.Render(msg, provenance) + "\n");
    }

    public void AppendColoredLog(
        string msg,
        Color color,
        TextProvenance provenance = TextProvenance.LauncherAuthored
    )
    {
        _entries.Add(new LogEntry(msg, provenance, color));
        AppendColoredRendered(Loc.Render(msg, provenance), color);
    }

    private void RefreshLanguage()
    {
        if (_renderedLanguage == Loc.CurrentLanguage)
            return;
        _renderedLanguage = Loc.CurrentLanguage;
        Clear();
        foreach (var entry in _entries)
        {
            var rendered = Loc.Render(entry.Source, entry.Provenance);
            if (entry.Color is Color color)
                AppendColoredRendered(rendered, color);
            else
                AddText(rendered + "\n");
        }
    }

    private void AppendColoredRendered(string msg, Color color)
    {
        PushColor(color);
        AddText(msg + "\n");
        Pop();
    }

    private readonly record struct LogEntry(
        string Source,
        TextProvenance Provenance,
        Color? Color
    );
}
