using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System.Globalization;
using System.Text.Json;
using MegaCrit.Sts2.Core.Platform;

namespace STS2MobileIos.Lan;

internal static class LanSession
{
    private static INetGameService _service;
    private static JoinFlow _joining;
    private static bool _advertising, _suspended;
    private static ushort _port;
    public static bool Busy => _joining != null || _service?.IsConnected == true;
    public static bool Joining => _joining != null;
    public static event Action Suspended;

    private static NSubmenuStack RequireMenu()
    {
        var menu = NGame.Instance?.MainMenu;
        if (menu == null || RunManager.Instance.IsInProgress || !SaveManager.Instance.IsProfileInitialized
            || SaveManager.Instance.CurrentRunSaveTask is { IsCompleted: false } || Busy)
            throw new InvalidOperationException("Return to the main menu and leave the current lobby before starting LAN play.");
        var stack = menu.SubmenuStack;
        // Character selection and other active submenus may already own a network service.
        if (stack.SubmenusOpen && stack.Peek() is not NMultiplayerSubmenu)
            throw new InvalidOperationException("Close the current game submenu before starting LAN play.");
        return stack;
    }

    public static void Host(ushort port, bool resume)
    {
        var stack = RequireMenu();
        SerializableRun saved = null;
        if (resume)
        {
            saved = ReadHostSave();
        }
        else if (SaveManager.Instance.HasMultiplayerRunSave)
            throw new InvalidOperationException("This profile has a multiplayer save. Resume it, or use another profile for a new run.");

        var service = new NetHostGameService(PeerVersionInfo.LocalDefault());
        try
        {
            var error = service.StartENetHost(port, 4);
            if (error.HasValue)
                throw new InvalidOperationException("Could not open the LAN port: " + error.Value.GetReason());
            _service = service;
            _port = port;
            if (saved != null)
            {
                var screen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
                screen.InitializeAsHost(service, saved);
                stack.Push(screen);
            }
            else
            {
                var screen = stack.GetSubmenuType<NCharacterSelectScreen>();
                screen.InitializeMultiplayerAsHost(service, 4);
                stack.Push(screen);
            }
        }
        catch
        {
            service.Disconnect(NetError.InternalError, now: true);
            _service = null;
            throw;
        }
    }

    public static async Task Join(LanAddress address)
    {
        var stack = RequireMenu();
        var flow = new JoinFlow(new NetClientGameService(PeerVersionInfo.LocalDefault()));
        _joining = flow;
        bool transferred = false;
        try
        {
            // This identity is only for ENet; the local save account remains default/1.
            var result = await flow.Begin(new LanConnectionInitializer(ClientId(), address), stack.GetTree());
            flow.CancelToken.Token.ThrowIfCancellationRequested();
            if (!GodotObject.IsInstanceValid(stack) || NGame.Instance?.MainMenu == null)
                throw new OperationCanceledException();
            if (result.gameMode != GameMode.Standard)
                throw new InvalidOperationException("The LAN menu currently supports standard runs only.");
            if (result.sessionState == RunSessionState.InLobby)
            {
                if (SaveManager.Instance.HasMultiplayerRunSave)
                    throw new InvalidOperationException("This profile already has a multiplayer save. Use another profile to join a new run.");
                var screen = stack.GetSubmenuType<NCharacterSelectScreen>();
                screen.InitializeMultiplayerAsClient(flow.NetService, result.joinResponse.Value);
                stack.Push(screen);
            }
            else if (result.sessionState == RunSessionState.InLoadedLobby)
            {
                if (SaveManager.Instance.HasMultiplayerRunSave)
                    throw new InvalidOperationException("This profile has a hosted multiplayer save. Use a profile without one to join another host.");
                var screen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
                screen.InitializeAsClient(flow.NetService, result.loadJoinResponse.Value);
                stack.Push(screen);
            }
            else
                throw new InvalidOperationException("Join while the host is in a lobby. Rejoining a running game is not supported by this menu yet.");
            _service = flow.NetService;
            transferred = true;
        }
        finally
        {
            if (!transferred)
                flow.NetService.Disconnect(NetError.CancelledJoin, now: true);
            _joining = null;
        }
    }

    private static SerializableRun ReadHostSave()
    {
        string directory = ProjectSettings.GlobalizePath(UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId, "saves"));
        string path = Path.Combine(directory, "current_run_mp.save");
        if (!File.Exists(path)) path += ".backup";
        try
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException();
            var saved = JsonSerializer.Deserialize(File.ReadAllBytes(path), JsonSerializationUtility.GetTypeInfo<SerializableRun>());
            if (saved == null || saved.SchemaVersion != SaveManager.Instance.GetLatestSchemaVersion<SerializableRun>()
                || saved.GameMode != GameMode.Standard || saved.PlatformType != PlatformType.None
                || saved.Players is not { Count: >= 1 and <= 4 } || saved.Players[0].NetId != 1)
                throw new InvalidDataException();
            // Validate in memory; do not use the stock loader that renames rejected saves.
            return RunManager.CanonicalizeSave(saved, 1);
        }
        catch
        {
            throw new InvalidOperationException("This profile has no compatible standard LAN host save for this game version. Its files have been kept.");
        }
    }

    private static ulong ClientId()
    {
        var config = new ConfigFile();
        config.Load("user://lan.cfg");
        string stored = config.GetValue("lan", "client_id", "").AsString();
        if (ulong.TryParse(stored, out var id) && id > 1) return id;
        id = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)) | (1UL << 63);
        config.SetValue("lan", "client_id", id.ToString(CultureInfo.InvariantCulture));
        if (config.Save("user://lan.cfg") != Error.Ok)
            throw new InvalidOperationException("Could not save the LAN identity needed to resume multiplayer runs.");
        return id;
    }

    public static void CancelJoin() => _joining?.CancelToken.Cancel();

    public static void Tick()
    {
        bool advertise = !_suspended && _service is NetHostGameService { IsConnected: true }
            && NGame.Instance?.MainMenu != null && !RunManager.Instance.IsInProgress;
        if (advertise == _advertising) return;
        _advertising = advertise;
        if (advertise) NativeBridge.PublishLan(_port);
        else NativeBridge.StopLanPublish();
    }

    public static void SetSuspended(bool value)
    {
        _suspended = value;
        if (value)
        {
            CancelJoin();
            NativeBridge.StopLanBrowse();
            NativeBridge.StopLanPublish();
            _advertising = false;
            Suspended?.Invoke();
        }
    }

    public static void Dispose()
    {
        CancelJoin();
        NativeBridge.StopLanBrowse();
        NativeBridge.StopLanPublish();
    }
}
