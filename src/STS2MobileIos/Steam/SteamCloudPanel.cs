using Godot;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Saves;

namespace STS2MobileIos.Steam;

internal sealed class SteamCloudPanel
{
    private readonly SteamCloudClient _client = new();
    private readonly PanelContainer _panel;
    private readonly Label _status;
    private readonly TextureRect _qr;
    private readonly ItemList _files;
    private readonly Label _preview;
    private readonly Button _connect, _refresh, _download, _disconnect;
    private readonly Button _prepareImport, _confirmImport;
    private readonly CheckButton _includeRun;
    private readonly Button _vanillaTab, _moddedTab;
    private readonly Dictionary<CloudFileKind, Button> _categoryTabs = new();
    private readonly Label _browseHint, _empty;
    private readonly VBoxContainer _importActions;
    private readonly Label _importHint;
    private readonly Label _destinationHint;
    private List<CloudBrowserEntry> _catalog = new(), _visibleFiles = new();
    private bool _showModded;
    private CloudFileKind _category = CloudFileKind.Saves;
    private CloudProfileCopy _importCopy;
    private int _importSlot;
    private CancellationTokenSource _operation;
    private List<CloudFile> _inventory = new();
    private bool _busy;

    public SteamCloudPanel(Control root)
    {
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        root.AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.OffsetLeft = 100;
        _panel.OffsetTop = 80;
        _panel.OffsetRight = -100;
        _panel.OffsetBottom = -80;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
            BgColor = new Color(0.055f, 0.065f, 0.085f, 0.99f),
            BorderColor = new Color(0.27f, 0.32f, 0.40f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16,
        });
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        _panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);
        var title = new Label { Text = "Steam Cloud" };
        title.AddThemeFontSizeOverride("font_size", 30);
        column.AddChild(title);
        _status = new Label { Text = "Connect to browse your Slay the Spire 2 saves.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        column.AddChild(actions);
        _connect = Button(actions, "Connect Steam", () => Run(Connect));
        _refresh = Button(actions, "Refresh saves", () => Run(Refresh));
        _disconnect = Button(actions, "Disconnect", Disconnect);
        Button(actions, "Close", Close);
        _qr = new TextureRect { Visible = false, CustomMinimumSize = new Vector2(280, 280),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        column.AddChild(_qr);
        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 12);
        column.AddChild(modes);
        _vanillaTab = Tab(modes, "Vanilla", () => ChangeView(false, _category));
        _moddedTab = Tab(modes, "Modded", () => ChangeView(true, _category));
        var categories = new HBoxContainer();
        categories.AddThemeConstantOverride("separation", 12);
        column.AddChild(categories);
        foreach (var kind in Enum.GetValues<CloudFileKind>())
            _categoryTabs.Add(kind, Tab(categories, CategoryTitle(kind), () => ChangeView(_showModded, kind)));
        _browseHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_browseHint);
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 24);
        column.AddChild(body);
        var browser = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f };
        body.AddChild(browser);
        var detailScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(420, 0) };
        body.AddChild(detailScroll);
        var details = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 18);
        detailScroll.AddChild(details);
        _empty = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center };
        browser.AddChild(_empty);
        _files = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        _files.AddThemeConstantOverride("v_separation", 14);
        _files.AddThemeFontSizeOverride("font_size", 22);
        browser.AddChild(_files);
        _files.ItemSelected += _ => SelectFile();
        _preview = new Label { Text = "Select a file to preview it.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        details.AddChild(_preview);
        _download = Button(details, "Download a separate copy", () => Run(Download));
        _importActions = new VBoxContainer();
        _importActions.AddThemeConstantOverride("separation", 10);
        details.AddChild(_importActions);
        _importHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _importActions.AddChild(_importHint);
        _destinationHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _importActions.AddChild(_destinationHint);
        _includeRun = new CheckButton { Text = "Include current run when importing", ButtonPressed = true };
        _importActions.AddChild(_includeRun);
        _includeRun.Toggled += _ => { ClearImport(); UpdateButtons(); };
        var imports = new VBoxContainer();
        imports.AddThemeConstantOverride("separation", 12);
        _importActions.AddChild(imports);
        _prepareImport = Button(imports, "Preview profile import", () => Run(PrepareImport));
        _confirmImport = Button(imports, "Import into unused slot", () => Run(ConfirmImport));
        RebuildBrowser();
        root.TreeExiting += () => { _operation?.Cancel(); _client.Dispose(); };
    }

    private static Button Button(Node parent, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 48) };
        parent.AddChild(button);
        button.Pressed += pressed;
        return button;
    }

    private static Button Tab(Node parent, string text, Action pressed)
    {
        var button = Button(parent, text, pressed);
        button.ToggleMode = true;
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat {
            BgColor = new Color(0.16f, 0.30f, 0.43f),
            BorderColor = new Color(0.46f, 0.73f, 0.90f), BorderWidthBottom = 3,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        return button;
    }

    private static string CategoryTitle(CloudFileKind kind) => kind switch {
        CloudFileKind.Saves => "Saves",
        CloudFileKind.History => "Run history (.run)",
        _ => "Other files",
    };

    private CloudBrowserEntry SelectedEntry()
    {
        var indices = _files.GetSelectedItems();
        return indices.Length == 1 && indices[0] < _visibleFiles.Count ? _visibleFiles[indices[0]] : null;
    }

    private bool HasProgress(CloudBrowserEntry entry) => entry?.ImportProfile is string profile
        && _inventory.Any(file => file.Name == profile + "/saves/progress.save");

    private bool HasCurrentRun(CloudBrowserEntry entry) => entry?.ImportProfile is string profile
        && _inventory.Any(file => file.Name == profile + "/saves/current_run.save");

    private void ChangeView(bool modded, CloudFileKind kind)
    {
        if (_busy) return;
        _showModded = modded;
        _category = kind;
        RebuildBrowser();
    }

    private void RebuildBrowser()
    {
        ClearImport();
        _visibleFiles = CloudBrowser.Filter(_catalog, _showModded, _category);
        _files.Clear();
        foreach (var entry in _visibleFiles)
            _files.AddItem(entry.Title + "  |  " + ModifiedTime(entry.File));
        _vanillaTab.Text = $"Vanilla ({_catalog.Count(entry => !entry.IsModded)})";
        _moddedTab.Text = $"Modded ({_catalog.Count(entry => entry.IsModded)})";
        _vanillaTab.SetPressedNoSignal(!_showModded);
        _moddedTab.SetPressedNoSignal(_showModded);
        foreach (var (kind, button) in _categoryTabs)
        {
            button.Text = $"{CategoryTitle(kind)} ({_catalog.Count(entry => entry.IsModded == _showModded && entry.Kind == kind)})";
            button.SetPressedNoSignal(kind == _category);
        }
        _browseHint.Text = _category switch {
            CloudFileKind.Saves when !_showModded => "Select a save to import its profile. Compatibility is checked in the preview; Steam Cloud stays unchanged.",
            CloudFileKind.Saves => "Import into a separate modded profile. Enable the same mods and versions as on desktop before previewing.",
            CloudFileKind.History => "Completed run records, newest first. These .run files are archives and cannot resume a run or import a profile.",
            _ => "Account files, settings, and other files. Download these as backups; they are not supported by profile import.",
        };
        _empty.Text = _client.Login == null ? "Connect Steam to browse your saves."
            : $"No {(_showModded ? "modded" : "vanilla")} {(_category == CloudFileKind.History ? "run history" : CategoryTitle(_category).ToLowerInvariant())} found.";
        _empty.Visible = _visibleFiles.Count == 0;
        _files.Visible = _visibleFiles.Count != 0;
        if (_category == CloudFileKind.Saves && _visibleFiles.Count != 0)
        {
            _files.Select(0);
            SelectFile();
        }
        else
        {
            _preview.Text = _visibleFiles.Count == 0 ? "" : "Select a file to see details and download a copy.";
            UpdateButtons();
        }
    }

    private static string ModifiedTime(CloudFile file) => file.Timestamp is >= -62135596800 and <= 253402300799
        ? $"{DateTimeOffset.FromUnixTimeSeconds(file.Timestamp):yyyy-MM-dd HH:mm} UTC" : "Unknown date";

    private void SelectFile()
    {
        ClearImport();
        var entry = SelectedEntry();
        if (entry == null) return;
        _preview.Text = entry.File.Name + $"\n{entry.File.Size:N0} bytes | {ModifiedTime(entry.File)}";
        bool hasRun = HasCurrentRun(entry);
        _includeRun.SetPressedNoSignal(hasRun);
        _includeRun.Text = hasRun ? "Include current run when importing" : "No current run in this profile";
        _importHint.Text = HasProgress(entry)
            ? $"Import Profile {entry.Profile}: progress and available preferences, with an optional current run. A backup is created first."
            : "This profile has no progress.save. These files can only be downloaded as backups.";
        UpdateDestinationHint();
        UpdateButtons();
    }

    private void UpdateDestinationHint()
    {
        string mismatch = CloudProfileImport.ModeMismatch(_showModded, UserDataPathProvider.IsRunningModded);
        if (mismatch != null)
        {
            _destinationHint.Text = mismatch;
            return;
        }
        if (!SaveManager.Instance.IsProfileInitialized)
        {
            _destinationHint.Text = "Return to the main menu to choose an import destination.";
            return;
        }
        var labels = new List<string>();
        int target = 0;
        for (int slot = 1; slot <= 3; slot++)
        {
            bool active = SaveManager.Instance.CurrentProfileId == slot;
            bool empty = !active && CloudProfileImport.IsEmptySlot(CloudProfileImport.ProfileDirectory(AccountDirectory, slot, _showModded));
            if (empty && target == 0) target = slot;
            labels.Add($"Profile {slot}: {(active ? "active" : empty ? "empty" : "saved files")}");
        }
        _destinationHint.Text = string.Join("  |  ", labels) + (target != 0
            ? $"\nImport destination: iPad {(_showModded ? "modded " : "")}Profile {target}." : "\nAll slots contain saves or are active; no profile will be overwritten.");
    }

    public void Show()
    {
        _panel.Show();
        if (_client.Login == null && !_busy)
            Run(async cancellation =>
            {
                string saved = NativeBridge.ReadCredential();
                if (string.IsNullOrEmpty(saved)) return;
                var value = JsonNode.Parse(saved);
                var login = new SteamLogin(value["account"]!.GetValue<string>(), value["refresh"]!.GetValue<string>(), value["steamid"]!.GetValue<string>());
                _status.Text = "Restoring Steam connection...";
                await _client.Resume(login, cancellation);
                await Refresh(cancellation);
            });
    }

    private async void Run(Func<CancellationToken, Task> operation)
    {
        if (_busy) return;
        _busy = true;
        using var source = new CancellationTokenSource();
        _operation = source;
        UpdateButtons();
        try { await operation(source.Token); }
        catch (OperationCanceledException) { _status.Text = "Connection cancelled or timed out. Try again when ready."; }
        catch (HttpRequestException) { _status.Text = "Cannot reach Steam. Check your connection and try again."; }
        catch (Exception error)
        {
            _status.Text = error is InvalidOperationException or InvalidDataException ? error.Message
                : "Could not complete this operation. No active saves were changed.";
        }
        finally
        {
            _operation = null;
            _busy = false;
            _qr.Hide();
            UpdateButtons();
        }
    }

    private async Task Connect(CancellationToken cancellation)
    {
        _status.Text = "Requesting Steam login...";
        var challenge = await _client.BeginLogin(cancellation);
        DrawQr(challenge.Url);
        _status.Text = "Scan with Steam on your phone, then approve the StS2 iOS login.";
        var login = await _client.WaitForLogin(challenge, DrawQr, cancellation);
        NativeBridge.WriteCredential(new JsonObject { ["account"] = login.AccountName,
            ["refresh"] = login.RefreshToken, ["steamid"] = login.SteamId }.ToJsonString());
        _qr.Hide();
        await Refresh(cancellation);
    }

    private void DrawQr(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || uri.Host != "s.team")
            throw new InvalidOperationException("Steam returned an unexpected login address.");
        using var image = new Image();
        if (image.LoadPngFromBuffer(NativeBridge.QrPng(url)) != Error.Ok)
            throw new InvalidOperationException("Could not display the Steam QR code.");
        _qr.Texture = ImageTexture.CreateFromImage(image);
        _qr.Show();
    }

    private async Task Refresh(CancellationToken cancellation)
    {
        ClearImport();
        _status.Text = "Reading Steam Cloud...";
        _inventory = await _client.ListFiles(cancellation);
        _catalog = _inventory.Select(CloudBrowser.Describe).ToList();
        RebuildBrowser();
        _status.Text = $"Connected as {_client.Login.AccountName}. {_inventory.Count} cloud files.";
    }

    private async Task Download(CancellationToken cancellation)
    {
        ClearImport();
        var file = SelectedEntry()?.File;
        if (file == null) return;
        _status.Text = "Downloading a separate copy...";
        var bytes = await _client.Download(file, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var path = CloudSavePolicy.Archive(Path.Combine(OS.GetUserDataDir(), "cloud-downloads"), file, bytes);
        _preview.Text = CloudSavePolicy.Describe(bytes);
        _status.Text = "Downloaded to Files > StS2 iOS > cloud-downloads/" + Path.GetFileName(Path.GetDirectoryName(path))
            + ". Active saves are unchanged.";
    }

    private string AccountDirectory => ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath(null));

    private async Task PrepareImport(CancellationToken cancellation)
    {
        ClearImport();
        UpdateDestinationHint();
        var selected = SelectedEntry();
        if (selected == null) return;
        string profile = selected.ImportProfile;
        if (profile == null)
            throw new InvalidOperationException("Select progress.save, prefs.save, or current_run.save from a profile.");
        CloudGameCompatibility.RequireMainMenu(selected.IsModded);
        bool includeRun = _includeRun.ButtonPressed;
        int slot = CloudProfileImport.FindUnusedSlot(AccountDirectory, SaveManager.Instance.CurrentProfileId, selected.IsModded);
        _status.Text = "Downloading and checking the profile. Keep the desktop game closed until import finishes...";
        var copy = await CloudProfileImport.Prepare(_client, profile, includeRun, cancellation);
        cancellation.ThrowIfCancellationRequested();
        string details = CloudGameCompatibility.Validate(copy);
        _importCopy = copy;
        _importSlot = slot;
        _preview.Text = $"{profile} -> iPad {(copy.IsModded ? "modded " : "")}Profile {slot}\n{details}\nA local backup is created first. Existing profiles are kept.";
        _status.Text = "Preview ready. Confirm the import to create the new profile.";
        _confirmImport.Text = $"Import into Profile {slot}";
    }

    private async Task ConfirmImport(CancellationToken cancellation)
    {
        var copy = _importCopy;
        int slot = _importSlot;
        if (copy == null) return;
        CloudGameCompatibility.RequireMainMenu(copy.IsModded);
        _status.Text = "Rechecking Steam Cloud before import...";
        await CloudProfileImport.Recheck(_client, copy, copy.Data.ContainsKey("current_run.save"), cancellation);
        cancellation.ThrowIfCancellationRequested();
        CloudGameCompatibility.Validate(copy);
        if (SaveManager.Instance.CurrentProfileId == slot)
            throw new InvalidOperationException("The target profile is now active. Preview the import again.");
        _status.Text = "Backing up local saves and creating the profile...";
        var receipt = CloudProfileImport.Commit(AccountDirectory, Path.Combine(OS.GetUserDataDir(), "save-backups"),
            slot, copy, _client.Login.SteamId);
        ClearImport();
        UpdateDestinationHint();
        _preview.Text = "Backup: Files > StS2 iOS > save-backups/" + Path.GetFileName(receipt.Snapshot);
        _status.Text = $"Imported into {(copy.IsModded ? "modded " : "")}Profile {slot}. Close this panel, tap your profile at the top left, and choose Profile {slot} to play.";
    }

    private void ClearImport()
    {
        _importCopy = null;
        if (_confirmImport != null)
        {
            _confirmImport.Text = "Import into unused slot";
            _confirmImport.Hide();
        }
    }

    private void Disconnect()
    {
        if (_busy) return;
        try
        {
            NativeBridge.DeleteCredential();
            _client.ForgetLogin();
            ClearImport();
            _inventory.Clear();
            _catalog.Clear();
            RebuildBrowser();
            _status.Text = "Steam disconnected. Saved login removed from this iPad.";
        }
        catch (InvalidOperationException error) { _status.Text = error.Message; }
        UpdateButtons();
    }

    private void Close()
    {
        _operation?.Cancel();
        ClearImport();
        _panel.Hide();
    }

    private void UpdateButtons()
    {
        bool connected = _client.Login != null;
        var selected = SelectedEntry();
        bool canImport = HasProgress(selected)
            && CloudProfileImport.ModeMismatch(selected.IsModded, UserDataPathProvider.IsRunningModded) == null;
        _connect.Disabled = _busy || connected;
        _refresh.Disabled = _busy || !connected;
        _disconnect.Disabled = _busy || !connected;
        _download.Visible = selected != null;
        _download.Disabled = _busy || !connected || selected == null;
        _importActions.Visible = _category == CloudFileKind.Saves && selected != null;
        _prepareImport.Disabled = _busy || !connected || !canImport;
        _prepareImport.Text = selected?.Profile is int profile ? $"Preview Profile {profile} import" : "Preview profile import";
        _confirmImport.Visible = _importCopy != null;
        _confirmImport.Disabled = _busy || !connected || _importCopy == null;
        _includeRun.Disabled = _busy || !canImport || !HasCurrentRun(selected);
        _vanillaTab.Disabled = _busy;
        _moddedTab.Disabled = _busy;
        foreach (var button in _categoryTabs.Values) button.Disabled = _busy;
        for (int index = 0; index < _files.ItemCount; index++) _files.SetItemDisabled(index, _busy);
    }
}
