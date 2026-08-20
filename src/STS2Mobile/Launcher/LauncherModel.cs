using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher;

// Orchestrates the launcher flow: credential loading, authentication, ownership
// verification, game file downloads, and update checks. Delegates persistence to
// SteamCredentialStore and ownership to OwnershipVerifier. Events fire from
// background threads; the controller marshals them to the main thread.
public class LauncherModel : IDisposable
{
    private readonly string _dataDir;
    private readonly SteamCredentialStore _credentialStore;

    private SteamConnection _connection;
    private SteamAuth _auth;
    private DepotDownloader _downloader;
    private CancellationTokenSource _downloadCts;
    private TaskCompletionSource<bool> _launchTcs;
    private TaskCompletionSource<string> _codeTcs;
    private SessionState _state = SessionState.Disconnected;
    private string _failReason;
    private int _sessionGeneration;
    private bool _accountSwitchPending;

    public volatile bool OfflineMode;
    public volatile bool ConnectionResolved;
    public volatile bool AwaitingCode;

    // Issue #45: 브랜치 전환으로 PCK 가 in-process 갱신되면 dst dll 과 mismatch
    // — process restart 필요. LauncherUI 가 이걸 보고 Play→Restart 분기.
    internal volatile bool NeedsRestartAfterBranchSwitch;

    // True when launched from GameStartupWrapper (game files present). False in
    // standalone launcher mode where a restart is needed after downloading files.
    // Setting this to true eagerly creates the launch TCS so it exists before the
    // UI is shown (preventing a race between PLAY button and WaitForLaunch).
    private bool _inGameMode;
    public bool InGameMode
    {
        get => _inGameMode;
        set
        {
            _inGameMode = value;
            if (value && _launchTcs == null)
                _launchTcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
        }
    }
    public string AccountName => _credentialStore.AccountName;
    public string SavedAccountName => _credentialStore.AccountName;
    public string SavedRefreshToken => _credentialStore.RefreshToken;
    public ulong ActiveSteamId => _credentialStore.SteamId;
    public IReadOnlyList<SteamAccountSummary> StoredAccounts => _credentialStore.Accounts;
    public bool AccountSwitchPending => _accountSwitchPending;
    public string FailReason => _failReason;
    public SessionState SessionState => _state;

    // issue #59 — computed once per StartSession() call from the saved
    // refresh token's JWT `exp` claim, no network involved. Deliberately does
    // NOT block/redirect the fast path (see StartSession) — these are pure
    // signals for the controller to decide how to warn the user while still
    // letting ReadyToLaunch/PLAY proceed uninterrupted (offline play must
    // keep working even with an expired token — that's the whole point of
    // the fast path).
    public bool SavedTokenExpired { get; private set; }
    public bool SavedTokenExpiringSoon { get; private set; }

    // Issue #58 phase 4b: exposes the launcher's own SteamConnection so the Mod
    // Hub's Workshop tabs can issue PublishedFile RPCs without opening a second
    // connection. Null until EnsureConnectedAsync (or Connect/LoginAsync) has run
    // at least once — callers must check SessionState == LoggedIn alongside this.
    public SteamConnection Connection => _connection;

    public event Action<SessionState> SessionStateChanged;
    public event Action<string> LogReceived;
    public event Action<bool> CodeNeeded;
    public event Action<DownloadProgress> DownloadProgressChanged;
    public event Action<string> DownloadLogReceived;
    public event Action DownloadCompleted;
    public event Action<string> DownloadFailed;
    public event Action DownloadCancelled;
    public event Action<bool> UpdateCheckCompleted;
    public event Action<string> UpdateCheckFailed;

    public LauncherModel(string dataDir)
    {
        _dataDir = dataDir;
        _credentialStore = new SteamCredentialStore(dataDir);
    }

    public Task<bool> WaitForLaunch()
    {
        _launchTcs ??= new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        return _launchTcs.Task;
    }

    // Loads saved credentials and determines the launcher path. Sets
    // LauncherPatches statics so cloud push/pull works on all code paths.
    public FastPathResult StartSession()
    {
        OfflineMode = false;
        ConnectionResolved = false;
        _credentialStore.Load();

        if (_credentialStore.LoadFailed)
        {
            LauncherPatches.ResetAccountSession();
            AccountDataIsolation.ClearActive();
            return FastPathResult.AccountDataUnavailable;
        }

        if (_credentialStore.SteamId != 0)
        {
            if (!AccountDataIsolation.TryActivate(_dataDir, _credentialStore.DataSlot, out _))
            {
                LauncherPatches.ResetAccountSession();
                AccountDataIsolation.ClearActive();
                PatchHelper.Log("[AccountSwitch] Account data activation failed; launch blocked");
                return FastPathResult.AccountDataUnavailable;
            }
        }

        if (_credentialStore.HasCredentials)
        {
            LauncherPatches.SavedAccountName = _credentialStore.AccountName;
            LauncherPatches.SavedRefreshToken = _credentialStore.RefreshToken;
        }

        // issue #59 — pre-flight exp check, no network. Deliberately does NOT
        // change any of the FastPathResult branching below — ReadyToLaunch/
        // AutoConnect/ShowLogin are decided exactly as before. Unparseable
        // token (format change, corrupt data) leaves both flags false, same
        // as today (fail-open — see RefreshTokenExpiry's class doc).
        SavedTokenExpired = false;
        SavedTokenExpiringSoon = false;
        if (_credentialStore.HasCredentials)
        {
            SavedTokenExpired = RefreshTokenExpiry.IsExpired(_credentialStore.RefreshToken);
            SavedTokenExpiringSoon =
                !SavedTokenExpired
                && RefreshTokenExpiry.IsExpiringSoon(_credentialStore.RefreshToken, withinDays: 14);
            if (SavedTokenExpired)
                PatchHelper.Log("[Issue59] Saved refresh token appears expired (exp in the past)");
            else if (SavedTokenExpiringSoon)
                PatchHelper.Log("[Issue59] Saved refresh token expiring within 14 days");
        }

        var verifier = CreateOwnershipVerifier();
        var hasMarker = verifier?.HasMarker() ?? false;
        PatchHelper.Log(
            $"[Launcher] Fast path: creds={_credentialStore.HasCredentials}, marker={hasMarker}"
        );

        // Even with a valid marker, refuse the fast path if the PCK isn't on
        // disk — otherwise PLAY would launch into a broken game.
        if (
            _credentialStore.HasCredentials
            && _credentialStore.SteamId != 0
            && hasMarker
            && GameFilesReady()
        )
            return FastPathResult.ReadyToLaunch;

        if (_credentialStore.HasCredentials)
            return FastPathResult.AutoConnect;

        return FastPathResult.ShowLogin;
    }

    // Connects on-demand and verifies ownership. Used when we have saved
    // credentials but no ownership marker.
    public async void Connect()
    {
        var generation = _sessionGeneration;
        SetState(SessionState.Connecting);

        try
        {
            _connection = new SteamConnection(
                _credentialStore.AccountName,
                _credentialStore.RefreshToken
            );
            if (!await VerifyOwnershipAsync(_credentialStore.AccountName))
                return;
            if (
                !FinalizeAuthenticatedIdentity(
                    _credentialStore.AccountName,
                    _credentialStore.RefreshToken,
                    _credentialStore.GuardData,
                    generation
                )
            )
                return;
            SetState(SessionState.LoggedIn);
            // issue #59 — the server accepted this token, so a local "expired"
            // verdict (clock skew, parse quirk) is overruled. ExpiringSoon is
            // NOT cleared here: it's still the same token with the same exp.
            SavedTokenExpired = false;
            _ = MaybeRenewRefreshTokenAsync();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Connection failed: {ex.Message}");
            SetState(
                SessionState.Failed,
                "Could not connect to Steam. Check your internet connection."
            );
        }
    }

    // Performs interactive login via SteamAuth, saves credentials on success,
    // then verifies ownership.
    public async Task LoginAsync(string username, string password)
    {
        SensitiveLogRedactor.RegisterAccount(username, 0, null);
        var generation = _sessionGeneration;
        SetState(SessionState.Authenticating);

        try
        {
            _auth = new SteamAuth();
            _auth.LogMessage += msg => LogReceived?.Invoke(SensitiveLogRedactor.Redact(msg));
            _auth.CodeProvider = async (wasIncorrect) =>
            {
                AwaitingCode = true;
                CodeNeeded?.Invoke(wasIncorrect);
                _codeTcs = new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                var code = await _codeTcs.Task;

                if (_auth.NeedsReconnectForAuth)
                    await _auth.ReconnectForAuthAsync();

                AwaitingCode = false;
                return code;
            };

            _auth.Connect();
            var result = await _auth.LoginWithCredentialsAsync(
                username,
                password,
                _credentialStore.GetGuardDataForAccount(username)
            );

            // The new token and Guard data exist before ownership verification
            // and vault publication. Register them at the first possible point
            // so even an unexpected exception in that gap cannot echo either
            // value through a Steam/HTTP exception message.
            SensitiveLogRedactor.RegisterAccount(
                result.AccountName,
                0,
                result.RefreshToken,
                result.GuardData
            );

            _auth.Dispose();
            _auth = null;

            _connection = new SteamConnection(result.AccountName, result.RefreshToken);
            if (!await VerifyOwnershipAsync(result.AccountName, persistMarker: false))
                return;
            if (
                !FinalizeAuthenticatedIdentity(
                    result.AccountName,
                    result.RefreshToken,
                    result.GuardData,
                    generation
                )
            )
                return;
            new OwnershipVerifier(_dataDir, result.AccountName).SaveMarker();
            SetState(SessionState.LoggedIn);
            // issue #59 — a fresh interactive login just issued a brand-new
            // refresh token: both expiry signals reset so the auth gates
            // (BlockIfTokenExpired) reopen immediately without a restart.
            SavedTokenExpired = false;
            SavedTokenExpiringSoon = false;
            _ = MaybeRenewRefreshTokenAsync();

            if (_accountSwitchPending)
            {
                PatchHelper.Log("[AccountSwitch] New account activated; restarting app");
                GetGodotApp()?.Call("restartApp");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Login failed: {ex.Message}");
            SetState(SessionState.Failed, ex.Message);
            _auth?.Dispose();
            _auth = null;
        }
    }

    // issue #59 — opportunistic rolling refresh-token renewal, fired
    // fire-and-forget (`_ = ...`) right after a connection has successfully
    // logged on (Connect()/LoginAsync()), so it can never delay or affect
    // the outcome of the login/connect flow itself — "기존 성공 경로는 손대지
    // 않는다". Deliberately NOT wired into EnsureConnectedAsync (download/
    // update-check path): once per app-session connect is enough: repeating
    // this on every download/update check would be redundant RPC traffic for
    // no added benefit (a renewal that already landed this session makes the
    // next IsExpiringSoon check false anyway).
    //
    // Only fires the actual RPC when the CURRENT saved token is within the
    // renewal window — cheap no-op the overwhelming majority of the time.
    // SteamCredentialStore.Save already fails safe internally (catches its
    // own exceptions, logs, never throws) — a persistence failure here still
    // leaves the freshly-renewed token live in LauncherPatches.SavedRefreshToken
    // for the rest of THIS session, it just won't survive an app restart.
    private async Task MaybeRenewRefreshTokenAsync()
    {
        var generation = _sessionGeneration;
        var steamId = _credentialStore.SteamId;
        var currentToken = _credentialStore.RefreshToken;
        var connection = _connection;
        // 45 days, deliberately wider than the 14-day boot warning: renewal
        // only fires while a session is actually connected, so the window has
        // to cover realistic boot gaps — with a 14-day window anyone who
        // plays less often than fortnightly can jump straight from "not soon"
        // to "expired" without ever getting a renewal chance. 45 keeps the
        // "only renew a near-death token" safety property (a botched
        // persist/renewal can only cost a token that was dying anyway) while
        // covering monthly players.
        if (!RefreshTokenExpiry.IsExpiringSoon(currentToken, withinDays: 45))
            return;

        if (connection == null || steamId == 0)
            return;

        var newToken = await connection
            .TryRenewRefreshTokenAsync(currentToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(newToken))
            return;

        if (
            !AccountSessionGuard.CanCommitRenewal(
                generation,
                _sessionGeneration,
                steamId,
                _credentialStore.SteamId,
                ReferenceEquals(connection, _connection)
            )
        )
        {
            PatchHelper.Log("[AccountSwitch] Ignored token renewal from an inactive session");
            return;
        }

        if (!_credentialStore.TryUpdateRefreshToken(steamId, currentToken, newToken))
            return;
        LauncherPatches.SavedRefreshToken = newToken;
        // The renewed token pushed exp ~200 days out — the boot warning no
        // longer applies to what's now saved.
        SavedTokenExpiringSoon = false;
        PatchHelper.Log("[Issue59] Refresh token renewed and persisted");
    }

    public void SubmitCode(string code) => _codeTcs?.TrySetResult(code);

    // Creates or reuses a SteamConnection for depot operations.
    public async Task EnsureConnectedAsync()
    {
        if (_state == SessionState.LoggedIn && _connection != null)
            return;

        if (!_credentialStore.HasCredentials)
        {
            SetState(SessionState.Failed, "No saved credentials");
            return;
        }

        _connection ??= new SteamConnection(
            _credentialStore.AccountName,
            _credentialStore.RefreshToken
        );

        SetState(SessionState.Connecting);
        try
        {
            await _connection.Apps.PICSGetAccessTokens(2868840, null);
            ConnectionResolved = true;
            OfflineMode = false;
            SetState(SessionState.LoggedIn);
        }
        catch (Exception ex)
        {
            SetState(SessionState.Failed, $"Connection failed: {ex.Message}");
        }
    }

    public async Task StartDownloadAsync(string branch = null, bool forceFresh = false)
    {
        await EnsureConnectedAsync();
        if (_state != SessionState.LoggedIn || _connection == null)
        {
            DownloadFailed?.Invoke(null);
            return;
        }

        _downloader?.Dispose();
        _downloader = new DepotDownloader(_connection, _dataDir);
        _downloader.LogMessage += msg => DownloadLogReceived?.Invoke(msg);
        _downloader.ProgressChanged += p => DownloadProgressChanged?.Invoke(p);

        _downloadCts = new CancellationTokenSource();
        var resolvedBranch = branch ?? LoadSelectedBranch();

        try
        {
            await Task.Run(() =>
                _downloader.DownloadAsync(resolvedBranch, _downloadCts.Token, forceFresh)
            );
            WriteCacheStampAfterDownload(resolvedBranch, _downloader.LastDownloadedBuildId);
            DownloadCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            DownloadCancelled?.Invoke();
        }
        catch (Exception ex)
        {
            DownloadFailed?.Invoke(ex.Message);
            PatchHelper.Log($"[Launcher] Download error: {ex}");
        }
    }

    // Reads the just-downloaded release_info.json and writes a CacheStamp.
    // v0.3.18 cleanup: sentinel/.godot rebuild 흐름 제거 후 stamp 는 메타데이터
    // (진단/향후 재활용) 로만 유지. 실제 issue #5 fix 는 GodotApp.setupAssemblies
    // 의 BCL/game-dll size+mtime 비교로 자동 동기화됨.
    private void WriteCacheStampAfterDownload(string branch, string buildId)
    {
        string commit = "";
        string version = "";
        try
        {
            var releaseInfoPath = Path.Combine(_dataDir, "game", "release_info.json");
            if (File.Exists(releaseInfoPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(releaseInfoPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("commit", out var c))
                    commit = c.GetString() ?? "";
                if (root.TryGetProperty("version", out var v))
                    version = v.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Failed to read release_info.json: {ex.Message}");
        }

        var stamp = new CacheStamp
        {
            Branch = branch,
            BuildId = buildId ?? "",
            Commit = commit,
            Version = version,
        };
        stamp.Write();
    }

    public async Task CheckForUpdatesAsync(string branch = null)
    {
        try
        {
            await EnsureConnectedAsync();
            if (_state != SessionState.LoggedIn || _connection == null)
            {
                UpdateCheckFailed?.Invoke("Not connected");
                return;
            }

            var downloader = new DepotDownloader(_connection, _dataDir);
            downloader.LogMessage += msg => DownloadLogReceived?.Invoke(msg);
            var resolvedBranch = branch ?? LoadSelectedBranch();

            bool hasUpdate = await Task.Run(() => downloader.CheckForUpdatesAsync(resolvedBranch));
            downloader.Dispose();

            UpdateCheckCompleted?.Invoke(hasUpdate);
        }
        catch (Exception ex)
        {
            UpdateCheckFailed?.Invoke(ex.Message);
        }
    }

    public async Task<List<SteamBranchInfo>> ListBranchesAsync()
    {
        await EnsureConnectedAsync();
        if (_state != SessionState.LoggedIn || _connection == null)
            throw new Exception("Not connected to Steam");

        var downloader = new DepotDownloader(_connection, _dataDir);
        downloader.LogMessage += msg => DownloadLogReceived?.Invoke(msg);
        try
        {
            return await Task.Run(() => downloader.EnumerateBranchesAsync());
        }
        finally
        {
            downloader.Dispose();
        }
    }

    public FastPathResult Retry()
    {
        _downloadCts?.Cancel();
        _downloader?.Dispose();
        _connection?.Dispose();
        _connection = null;
        _auth?.Dispose();
        _auth = null;
        return StartSession();
    }

    public async Task<bool> BeginAddAccountAsync()
    {
        if (!await PrepareForAccountChangeAsync().ConfigureAwait(false))
            return false;
        _accountSwitchPending = true;
        return true;
    }

    public async Task<bool> ActivateStoredAccountAsync(ulong steamId)
    {
        if (steamId == 0 || steamId == _credentialStore.SteamId)
            return false;
        if (!await PrepareForAccountChangeAsync().ConfigureAwait(false))
            return false;

        var previousId = _credentialStore.SteamId;
        var targetSlot = _credentialStore.GetDataSlot(steamId);
        if (!AccountDataIsolation.TryActivate(_dataDir, targetSlot, out _))
        {
            PatchHelper.Log("[AccountSwitch] Target data directory could not be activated");
            GetGodotApp()?.Call("restartApp");
            return false;
        }
        if (!_credentialStore.TryActivate(steamId))
        {
            if (previousId != 0)
                AccountDataIsolation.TryActivate(
                    _dataDir,
                    _credentialStore.GetDataSlot(previousId),
                    out _
                );
            else
                AccountDataIsolation.ClearActive();
            PatchHelper.Log("[AccountSwitch] Active account vault update failed");
            GetGodotApp()?.Call("restartApp");
            return false;
        }

        LauncherPatches.ResetAccountSession();
        PatchHelper.Log("[AccountSwitch] Stored account activated; restarting app");
        GetGodotApp()?.Call("restartApp");
        return true;
    }

    public void CancelAddAccount()
    {
        if (!_accountSwitchPending)
            return;
        PatchHelper.Log("[AccountSwitch] Add-account flow cancelled; restarting current account");
        GetGodotApp()?.Call("restartApp");
    }

    private async Task<bool> PrepareForAccountChangeAsync()
    {
        Interlocked.Increment(ref _sessionGeneration);
        _downloadCts?.Cancel();
        _codeTcs?.TrySetCanceled();

        var cloudStore = SteamKit2CloudSaveStore.Instance;
        if (cloudStore != null)
        {
            bool drained;
            try
            {
                drained = await Task.Run(() => cloudStore.Flush(300_000)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PatchHelper.Log(
                    $"[AccountSwitch] Cloud drain failed; switch aborted: {ex.GetType().Name}"
                );
                return false;
            }
            if (!drained)
            {
                PatchHelper.Log(
                    "[AccountSwitch] Pending cloud writes did not drain; switch aborted"
                );
                return false;
            }
            try
            {
                cloudStore.Dispose();
            }
            catch (Exception ex)
            {
                PatchHelper.Log(
                    $"[AccountSwitch] Cloud teardown failed; switch aborted: {ex.GetType().Name}"
                );
                GetGodotApp()?.Call("restartApp");
                return false;
            }
        }

        try
        {
            _downloader?.Dispose();
            _downloader = null;
            _connection?.Dispose();
            _connection = null;
            _auth?.Dispose();
            _auth = null;
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[AccountSwitch] Session teardown failed; restarting unchanged account: {ex.GetType().Name}"
            );
            GetGodotApp()?.Call("restartApp");
            return false;
        }
        LauncherPatches.ResetAccountSession();
        return true;
    }

    private bool FinalizeAuthenticatedIdentity(
        string accountName,
        string refreshToken,
        string guardData,
        int generation
    )
    {
        if (generation != _sessionGeneration || _connection == null)
            return false;

        var authenticatedId = _connection.AuthenticatedSteamId;
        SteamAccountIdentity.TryGetSteamId(refreshToken, out var tokenId);
        if (authenticatedId == 0 || (tokenId != 0 && tokenId != authenticatedId))
        {
            SetState(SessionState.Failed, "Steam account identity validation failed.");
            return false;
        }

        var previousId = _credentialStore.SteamId;
        var targetSlot =
            _credentialStore.GetDataSlot(authenticatedId) ?? SteamCredentialStore.CreateDataSlot();
        if (!AccountDataIsolation.TryActivate(_dataDir, targetSlot, out _))
        {
            SetState(SessionState.Failed, "Could not prepare the account data directory.");
            return false;
        }
        if (
            !_credentialStore.Save(
                accountName,
                refreshToken,
                guardData,
                authenticatedId,
                targetSlot
            )
        )
        {
            if (previousId != 0)
                AccountDataIsolation.TryActivate(
                    _dataDir,
                    _credentialStore.GetDataSlot(previousId),
                    out _
                );
            else
                AccountDataIsolation.ClearActive();
            SetState(SessionState.Failed, "Could not save the encrypted account session.");
            return false;
        }

        LauncherPatches.SavedAccountName = accountName;
        LauncherPatches.SavedRefreshToken = refreshToken;
        return true;
    }

    public void Launch()
    {
        if (_credentialStore.HasCredentials)
        {
            LauncherPatches.SavedAccountName = _credentialStore.AccountName;
            LauncherPatches.SavedRefreshToken = _credentialStore.RefreshToken;
        }

        if (_launchTcs != null)
            _launchTcs.TrySetResult(true);
        else
        {
            PatchHelper.Log("[Launcher] Restarting app to load game files");
            GetGodotApp()?.Call("restartApp");
        }
    }

    public bool HasOwnershipMarker() => CreateOwnershipVerifier()?.HasMarker() ?? false;

    public void Dispose()
    {
        Interlocked.Increment(ref _sessionGeneration);
        // An unexpected parent/configuration teardown must release every caller
        // awaiting this UI. A normal PLAY already completed the launch gate true,
        // so the one-shot false fallback cannot overwrite it.
        _launchTcs?.TrySetResult(false);
        _codeTcs?.TrySetCanceled();
        _downloadCts?.Cancel();
        _downloader?.Dispose();
        _auth?.Dispose();
        if (_launchTcs?.Task is not { IsCompletedSuccessfully: true, Result: true })
            _connection?.Dispose();
    }

    private async Task<bool> VerifyOwnershipAsync(string accountName, bool persistMarker = true)
    {
        SetState(SessionState.VerifyingOwnership);

        var verifier = !string.IsNullOrWhiteSpace(accountName)
            ? new OwnershipVerifier(_dataDir, accountName)
            : null;
        if (verifier == null)
        {
            SetState(SessionState.Failed, "Steam account identity is unavailable.");
            return false;
        }
        bool owns = await verifier.VerifyAsync(_connection, persistMarker);

        if (owns)
        {
            PatchHelper.Log("[Launcher] Ownership verified");
            ConnectionResolved = true;
            return true;
        }
        else
        {
            PatchHelper.Log("[Launcher] Ownership denied");
            SetState(
                SessionState.Failed,
                "You don't own Slay the Spire 2. Purchase on Steam to play."
            );
            return false;
        }
    }

    private OwnershipVerifier CreateOwnershipVerifier()
    {
        var account = _credentialStore.AccountName;
        return account != null ? new OwnershipVerifier(_dataDir, account) : null;
    }

    private void SetState(SessionState state, string failReason = null)
    {
        _state = state;
        _failReason = failReason;
        SessionStateChanged?.Invoke(state);
    }

    public static bool GameFilesReady()
    {
        var dataDirectory = OS.GetDataDir();
        var pckPath = Path.Combine(dataDirectory, "game", "SlayTheSpire2.pck");
        try
        {
            if (
                GameInstallTransaction.ActiveHasCompletionMarker(dataDirectory)
                && !GameInstallTransaction.ActiveTupleMatchesFiles(dataDirectory)
            )
            {
                PatchHelper.Log("[Launcher] Active game install marker does not match PCK/DLL");
                return false;
            }
            using var fs = File.OpenRead(pckPath);
            if (fs.Length < 4)
                return false;
            Span<byte> magic = stackalloc byte[4];
            fs.ReadExactly(magic);
            return magic[0] == 0x47 && magic[1] == 0x44 && magic[2] == 0x50 && magic[3] == 0x43;
        }
        catch
        {
            return false;
        }
    }

    // Issue #53 — true when a completed in-session depot download replaced the game
    // assembly (sts2.dll) relative to the copy currently loaded in this process.
    // Mirrors GodotApp.setupAssemblies' size+mtime skip test exactly, so the answer
    // matches what Java would re-copy on the next boot: if that boot would swap in a
    // new sts2.dll, the running process is on a stale assembly and must restart before
    // booting the freshly downloaded PCK (else old assembly + new PCK mix).
    public static bool GameAssemblyReplaced()
    {
        try
        {
            var dataDir = OS.GetDataDir();
            var gameDir = Path.Combine(dataDir, "game");
            if (!Directory.Exists(gameDir))
                return false;

            // The depot writes the managed assemblies into a data_* sibling of the
            // PCK (e.g. data_sts2_windows_x86_64); findAssembliesDir picks the first.
            string srcDll = null;
            foreach (var sub in Directory.GetDirectories(gameDir, "data_*"))
            {
                var candidate = Path.Combine(sub, "sts2.dll");
                if (File.Exists(candidate))
                {
                    srcDll = candidate;
                    break;
                }
            }
            if (srcDll == null)
                return false;

            var destDll = Path.Combine(dataDir, ".godot", "mono", "publish", "arm64", "sts2.dll");
            if (!File.Exists(destDll))
                return true; // nothing loaded on disk to match — treat as replaced

            var src = new FileInfo(srcDll);
            var dest = new FileInfo(destDll);
            // Java skips the copy when dest.length == src.length && dest.mtime >= src.mtime.
            // Assembly replaced = the negation of that up-to-date test.
            bool upToDate =
                dest.Length == src.Length && dest.LastWriteTimeUtc >= src.LastWriteTimeUtc;
            PatchHelper.Log(
                $"[Launcher] GameAssemblyReplaced: srcLen={src.Length} destLen={dest.Length} "
                    + $"srcMtime={src.LastWriteTimeUtc:o} destMtime={dest.LastWriteTimeUtc:o} "
                    + $"→ replaced={!upToDate}"
            );
            return !upToDate;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] GameAssemblyReplaced check failed: {ex.Message}");
            return false;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    // Normalizes a manifest version string to a single "v" prefix so mods whose
    // version already includes it (e.g. BaseLib's "v3.3.5") don't render as
    // "vv3.3.5". Empty for a blank version.
    public static string VersionLabel(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return "";
        v = v.Trim();
        return v.StartsWith("v") || v.StartsWith("V") ? v : "v" + v;
    }

    // Issue #36 Part A: the Local Backup on/off preference was removed. Backup
    // is now a one-shot action (ActionSection's Local Backup button), so there's
    // no persisted enabled/disabled state to load or save.

    private static string CloudSyncPrefPath =>
        AccountDataIsolation.GetAccountPreferencePath(OS.GetDataDir(), "cloud_sync_enabled");

    public static bool LoadCloudSyncPref()
    {
        try
        {
            if (File.Exists(CloudSyncPrefPath))
                return File.ReadAllText(CloudSyncPrefPath).Trim() == "true";
        }
        catch { }
        return true;
    }

    public static void SaveCloudSyncPref(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CloudSyncPrefPath)!);
            File.WriteAllText(CloudSyncPrefPath, enabled ? "true" : "false");
        }
        catch { }
    }

    private static string SelectedBranchPath => Path.Combine(OS.GetDataDir(), "selected_branch");

    public static string LoadSelectedBranch()
    {
        try
        {
            var activeBranch = GameInstallTransaction.ReadActiveTuple(OS.GetDataDir())?.Branch;
            if (!string.IsNullOrWhiteSpace(activeBranch))
                return activeBranch;
            if (File.Exists(SelectedBranchPath))
            {
                var name = File.ReadAllText(SelectedBranchPath).Trim();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
        }
        catch { }
        return "public";
    }

    public static void SaveSelectedBranch(string branch)
    {
        try
        {
            var temporary = SelectedBranchPath + ".tmp";
            File.WriteAllText(temporary, string.IsNullOrEmpty(branch) ? "public" : branch);
            File.Move(temporary, SelectedBranchPath, overwrite: true);
        }
        catch { }
    }

    public static GodotObject GetGodotApp()
    {
        try
        {
            var jcw = Engine.GetSingleton("JavaClassWrapper");
            var wrapper = (GodotObject)
                jcw.Call("wrap", "com.game.sts2launcher.modmanager.GodotApp");
            return (GodotObject)wrapper.Call("getInstance");
        }
        catch
        {
            return null;
        }
    }
}
