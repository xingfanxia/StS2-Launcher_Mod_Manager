using Godot;
using STS2MobileIos.Patches;

namespace STS2MobileIos;

public static class MobileUi
{
    private static readonly FrameWindow Frames = new();
    private static Label _fps;
    private static bool _installed;
    private static bool _showFps;
    private static bool _suspended;

    public static void Install(Node game)
    {
        if (_installed)
            return;
        _installed = true;
        Callable.From(() => Create(game)).CallDeferred();
    }

    private static void Create(Node game)
    {
        var config = new ConfigFile();
        config.Load("user://mobile.cfg");
        _showFps = config.GetValue("display", "fps", false).AsBool();
        var layer = new CanvasLayer { Layer = 100, ProcessMode = Node.ProcessModeEnum.Always };
        game.AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _fps = new Label { Visible = _showFps, MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = "FPS --", HorizontalAlignment = HorizontalAlignment.Center };
        _fps.AddThemeFontSizeOverride("font_size", 22);
        _fps.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _fps.AddThemeConstantOverride("shadow_offset_x", 2);
        _fps.AddThemeConstantOverride("shadow_offset_y", 2);
        root.AddChild(_fps);
        _fps.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _fps.OffsetLeft = -140;
        _fps.OffsetRight = 140;
        _fps.OffsetTop = 84;
        _fps.OffsetBottom = 124;

        var menu = new Button { Text = "iOS", CustomMinimumSize = new Vector2(76, 44) };
        root.AddChild(menu);
        menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        menu.OffsetLeft = -92;
        menu.OffsetRight = -16;
        menu.OffsetTop = 96;
        menu.OffsetBottom = 140;

        var panel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(420, 0) };
        root.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -440;
        panel.OffsetRight = -20;
        panel.OffsetTop = 152;
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 20);
        panel.AddChild(margin);
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 16);
        margin.AddChild(list);
        list.AddChild(new Label { Text = "StS2 iOS" });
        var toggle = new CheckButton { Text = "Show FPS", ButtonPressed = _showFps };
        list.AddChild(toggle);
        toggle.Toggled += value =>
        {
            _showFps = value;
            _fps.Visible = value;
            Frames.ClearSample();
            Frames.ResetClock();
            config.SetValue("display", "fps", value);
            config.Save("user://mobile.cfg");
        };
        list.AddChild(new Label { Text = "Hold a card for its upgraded preview.\nMove your finger to cancel the hold.",
            MouseFilter = Control.MouseFilterEnum.Ignore });
        var cloud = new Steam.SteamCloudPanel(root);
        var steam = new Button { Text = "Steam Cloud", CustomMinimumSize = new Vector2(0, 48) };
        list.AddChild(steam);
        steam.Pressed += () => { panel.Hide(); cloud.Show(); };
        var lan = new Lan.LanPanel(root);
        var multiplayer = new Button { Text = "LAN multiplayer", CustomMinimumSize = new Vector2(0, 48) };
        list.AddChild(multiplayer);
        multiplayer.Pressed += () => { panel.Hide(); lan.Show(); };
        list.AddChild(new Label { Text = "Add mod folders in Files > StS2 JIT > mods.\nClose the app before changing mod files.\nEnable JIT again when restarting." });
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 44) };
        list.AddChild(close);
        close.Pressed += () => panel.Hide();
        menu.Pressed += () => panel.Visible = !panel.Visible;

        var tree = game.GetTree();
        tree.ProcessFrame += Tick;
        game.TreeExiting += () => tree.ProcessFrame -= Tick;
    }

    public static void SetSuspended(bool value)
    {
        _suspended = value;
        Frames.ResetClock();
        Frames.ClearSample();
        CardInspectPatches.Cancel();
        Lan.LanSession.SetSuspended(value);
    }

    private static void Tick()
    {
        if (_suspended)
            return;
        CardInspectPatches.Tick();
        Lan.LanSession.Tick();
        if (!_showFps)
            return;
        Frames.Tick(Time.GetTicksUsec());
        if (Frames.Elapsed < 500_000)
            return;
        _fps.Text = FormattableString.Invariant($"{Frames.Intervals.Count * 1_000_000.0 / Frames.Elapsed:F1} FPS  {Frames.Elapsed / (1000.0 * Frames.Intervals.Count):F1} ms");
        Frames.ClearSample();
    }
}
