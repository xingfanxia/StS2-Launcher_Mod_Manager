using Godot;
using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer.Connection;

namespace STS2MobileIos.Lan;

internal sealed class LanPanel
{
    internal static Action Open;
    private readonly PanelContainer _panel;
    private readonly Label _status;
    private readonly LineEdit _address, _port;
    private readonly ItemList _hosts;
    private readonly List<LanAddress> _endpoints = new();
    private readonly Button _host, _resume, _join, _browse;
    private bool _browsing;
    private string _snapshot;
    private ulong _nextPoll;

    public LanPanel(Control root)
    {
        Open = Show;
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        root.AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.OffsetLeft = 100; _panel.OffsetRight = -100;
        _panel.OffsetTop = 80; _panel.OffsetBottom = -80;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
            BgColor = new Color(0.055f, 0.065f, 0.085f, 1),
            ContentMarginLeft = 24, ContentMarginRight = 24,
            ContentMarginTop = 24, ContentMarginBottom = 24,
        });
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(list);
        list.AddChild(new Label { Text = "LAN multiplayer (experimental)" });
        list.AddChild(new Label {
            Text = "Use the same Wi-Fi and game version. Keep the app open during play.\nStandard runs, up to four players. Steam invites are not available.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var actions = new HBoxContainer(); list.AddChild(actions);
        _host = Button(actions, "Host new run", () => Host(false));
        _resume = Button(actions, "Resume host save", () => Host(true));
        _browse = Button(actions, "Find nearby games", Browse);
        Button(actions, "Close / cancel", Close);
        var endpoint = new HBoxContainer(); list.AddChild(endpoint);
        _address = new LineEdit { PlaceholderText = "Host IPv4 address", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 64 };
        _port = new LineEdit { Text = LanAddress.DefaultPort.ToString(), CustomMinimumSize = new Vector2(130, 48), MaxLength = 5 };
        endpoint.AddChild(_address); endpoint.AddChild(_port);
        _join = Button(endpoint, "Join", () => _ = Join());
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        list.AddChild(_status);
        _hosts = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 140) };
        list.AddChild(_hosts);
        _hosts.ItemSelected += index => {
            if (index < 0 || index >= _endpoints.Count) return;
            var address = _endpoints[(int)index];
            _address.Text = address.Host; _port.Text = address.Port.ToString();
        };
        var tree = root.GetTree();
        tree.ProcessFrame += Poll;
        LanSession.Suspended += Close;
        root.TreeExiting += () => { Open = null; tree.ProcessFrame -= Poll; LanSession.Suspended -= Close; LanSession.Dispose(); };
    }

    private static Button Button(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 48) };
        parent.AddChild(button); button.Pressed += action; return button;
    }

    public void Show()
    {
        _panel.Show();
        _status.Text = LanSession.Busy ? "A LAN session is active. Use the game's Back button to leave its lobby."
            : "Host a run or find a nearby host. You can also enter its address manually.";
        SetBusy(LanSession.Busy);
    }

    private void SetBusy(bool busy)
    {
        _host.Disabled = _resume.Disabled = _join.Disabled = _browse.Disabled = busy;
        _address.Editable = _port.Editable = !busy;
        _hosts.MouseFilter = busy ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
    }

    private void Close()
    {
        LanSession.CancelJoin();
        StopBrowse();
        _panel.Hide();
    }

    private void StopBrowse()
    {
        if (_browsing) NativeBridge.StopLanBrowse();
        _browsing = false;
    }

    private void Browse()
    {
        try
        {
            NativeBridge.BrowseLan();
            _browsing = true; _snapshot = null; _nextPoll = 0;
            _status.Text = "Searching... Allow Local Network access when iOS asks. Select a host, then Join.";
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void Host(bool resume)
    {
        try
        {
            var endpoint = LanAddress.Parse("127.0.0.1", _port.Text);
            LanSession.Host(endpoint.Port, resume);
            StopBrowse();
            _panel.Hide();
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private async Task Join()
    {
        try
        {
            var endpoint = LanAddress.Parse(_address.Text, _port.Text);
            SetBusy(true); StopBrowse();
            _status.Text = "Connecting... Use Close / cancel to stop.";
            await LanSession.Join(endpoint);
            _panel.Hide();
        }
        catch (OperationCanceledException) { _status.Text = "Connection cancelled."; }
        catch (ClientConnectionFailedException error) { _status.Text = "Could not join: " + error.info.GetReason(); }
        catch (Exception error) { _status.Text = error.Message; }
        finally { SetBusy(LanSession.Busy); }
    }

    private void Poll()
    {
        if (!_panel.Visible || !_browsing || Time.GetTicksMsec() < _nextPoll) return;
        _nextPoll = Time.GetTicksMsec() + 1000;
        try
        {
            string snapshot = NativeBridge.LanSnapshot();
            if (snapshot == null || snapshot == _snapshot) return;
            _snapshot = snapshot;
            using var json = JsonDocument.Parse(snapshot);
            _hosts.Clear(); _endpoints.Clear();
            foreach (var host in json.RootElement.GetProperty("hosts").EnumerateArray().Take(64))
            {
                try
                {
                    var endpoint = LanAddress.Parse(host.GetProperty("address").GetString(), host.GetProperty("port").GetInt32().ToString());
                    string name = new string(host.GetProperty("name").GetString().Where(c => !char.IsControl(c)).Take(64).ToArray());
                    _hosts.AddItem($"{name}  |  {endpoint.Host}:{endpoint.Port}");
                    _endpoints.Add(endpoint);
                }
                catch (ArgumentException) { }
            }
            string error = json.RootElement.GetProperty("error").GetString();
            if (!string.IsNullOrEmpty(error)) _status.Text = error;
            else _status.Text = _endpoints.Count == 0
                ? "No nearby lobby yet. Check Wi-Fi and Local Network access, or enter the host address."
                : "Select a nearby lobby, then Join. The game checks version and mod compatibility.";
        }
        catch { _status.Text = "Discovery is unavailable. You can enter the host address manually."; StopBrowse(); }
    }
}
