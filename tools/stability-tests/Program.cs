using System.Net;
using STS2Mobile;
using STS2Mobile.Launcher;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Modding;
using STS2Mobile.Multiplayer;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

var root = Path.Combine(Path.GetTempPath(), $"sts2-stability-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    Run(
        "two-finger tap emits one right click without leaking a primary click",
        () =>
        {
            var gesture = new TwoFingerTapGesture();
            var firstDown = gesture.Touch(0, pressed: true, 100, 200, 1_000);
            var secondDown = gesture.Touch(1, pressed: true, 140, 200, 1_080);
            Assert(
                !firstDown.ConsumeOriginal
                    && !firstDown.EmitRightClick
                    && !firstDown.BeganTwoFingerSequence
                    && secondDown.ConsumeOriginal
                    && !secondDown.EmitRightClick
                    && secondDown.BeganTwoFingerSequence,
                "the second finger did not take over the gesture"
            );
            Assert(
                gesture.SuppressPrimaryEvent(pressed: false, 1_120),
                "the emulated primary release leaked through the two-finger gesture"
            );

            var firstUp = gesture.Touch(0, pressed: false, 102, 201, 1_150);
            var secondUp = gesture.Touch(1, pressed: false, 138, 201, 1_180);
            Assert(
                firstUp.ConsumeOriginal
                    && !firstUp.EmitRightClick
                    && secondUp.ConsumeOriginal
                    && secondUp.EmitRightClick
                    && secondUp.EndedTwoFingerSequence
                    && Math.Abs(secondUp.X - 120f) < 0.001f
                    && Math.Abs(secondUp.Y - 201f) < 0.001f,
                "a valid two-finger tap did not emit exactly one centered right click"
            );
            Assert(
                !gesture.SuppressPrimaryEvent(pressed: true, 1_190),
                "the completed gesture swallowed the next independent primary press"
            );
        }
    );

    Run(
        "two-finger drag, hold, stagger, third finger, and single tap never right-click",
        () =>
        {
            static bool RunGesture(
                ulong secondDownAt,
                ulong finalUpAt,
                float travel,
                bool addThirdFinger
            )
            {
                var gesture = new TwoFingerTapGesture();
                gesture.Touch(0, pressed: true, 10, 10, 0);
                gesture.Touch(1, pressed: true, 30, 10, secondDownAt);
                if (travel > 0)
                    gesture.Move(1, 30 + travel, 10, secondDownAt + 10);
                if (addThirdFinger)
                    gesture.Touch(2, pressed: true, 50, 10, secondDownAt + 20);
                gesture.Touch(0, pressed: false, 10, 10, finalUpAt - 10);
                var secondUp = gesture.Touch(1, pressed: false, 30 + travel, 10, finalUpAt);
                return addThirdFinger
                    ? gesture.Touch(2, pressed: false, 50, 10, finalUpAt + 10).EmitRightClick
                    : secondUp.EmitRightClick;
            }

            var single = new TwoFingerTapGesture();
            single.Touch(0, pressed: true, 10, 10, 0);
            var singleUp = single.Touch(0, pressed: false, 10, 10, 100);

            Assert(
                !singleUp.ConsumeOriginal
                    && !singleUp.EmitRightClick
                    && !RunGesture(60, 180, travel: 48, addThirdFinger: false)
                    && !RunGesture(60, 500, travel: 0, addThirdFinger: false)
                    && !RunGesture(220, 300, travel: 0, addThirdFinger: false)
                    && !RunGesture(60, 180, travel: 0, addThirdFinger: true),
                "a non-tap multi-touch sequence synthesized a right click"
            );
        }
    );

    Run(
        "two-finger right click dispatches exactly once from the game input boundary",
        () =>
        {
            var repository = FindRepositoryRoot();
            var touchPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "TouchInputPatches.cs")
            );
            var dispatcher = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "TwoFingerRightClickDispatcher.cs"
                )
            );
            var rightClickSources = touchPatches + "\n" + dispatcher;
            var targets = File.ReadAllText(
                Path.Combine(repository, "tools", "patch-target-audit", "sts2-targets.tsv")
            );
            int dispatchStart = dispatcher.IndexOf(
                "private static void DispatchRightClick",
                StringComparison.Ordinal
            );
            int dispatchEnd = dispatcher.IndexOf(
                "private static void CancelCapturedPrimaryPress",
                StringComparison.Ordinal
            );
            string dispatch =
                dispatchStart >= 0 && dispatchEnd > dispatchStart
                    ? dispatcher[dispatchStart..dispatchEnd]
                    : string.Empty;
            int guiDispatch = dispatch.IndexOf(
                "EmitGuiRightClick(target, position);",
                StringComparison.Ordinal
            );
            int guiReturn =
                guiDispatch < 0
                    ? -1
                    : dispatch.IndexOf("return;", guiDispatch, StringComparison.Ordinal);
            int globalDispatch = dispatch.IndexOf(
                "EmitGlobalRightClick(position);",
                StringComparison.Ordinal
            );
            Assert(
                touchPatches.Contains(
                    "typeof(MegaCrit.Sts2.Core.Nodes.NGame)",
                    StringComparison.Ordinal
                )
                    && touchPatches.Contains("nameof(GameInputPrefix)", StringComparison.Ordinal)
                    && touchPatches.Contains("InputEventScreenTouch", StringComparison.Ordinal)
                    && touchPatches.Contains("InputEventScreenDrag", StringComparison.Ordinal)
                    && touchPatches.Contains(
                        "TwoFingerRightClickDispatcher.Capture",
                        StringComparison.Ordinal
                    )
                    && touchPatches.Contains(
                        "TwoFingerRightClickDispatcher.Complete",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains("Input.ParseInputEvent", StringComparison.Ordinal)
                    && touchPatches.Contains(
                        "result.BeganTwoFingerSequence",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains("GuiGetHoveredControl", StringComparison.Ordinal)
                    && rightClickSources.Contains("ActiveHolders", StringComparison.Ordinal)
                    && rightClickSources.Contains("FocusedHolder", StringComparison.Ordinal)
                    && rightClickSources.Contains("hand.InCardPlay", StringComparison.Ordinal)
                    && rightClickSources.Contains(
                        "child is NCardPlay currentCardPlay",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains(
                        "currentCardPlay.Holder",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains("holder.Hitbox", StringComparison.Ordinal)
                    && rightClickSources.Contains("control._HasPoint", StringComparison.Ordinal)
                    && rightClickSources.Contains(
                        "GetSignalConnectionList(Control.SignalName.GuiInput)",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains(
                        "Control.SignalName.GuiInput",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains("target._GuiInput", StringComparison.Ordinal)
                    && rightClickSources.Contains(
                        "CancelCapturedPrimaryPress(target)",
                        StringComparison.Ordinal
                    )
                    && rightClickSources.Contains("outsideGlobal", StringComparison.Ordinal)
                    && rightClickSources.Contains("NPlayerHand.Instance", StringComparison.Ordinal)
                    && rightClickSources.Contains("CancelAllCardPlay", StringComparison.Ordinal)
                    && rightClickSources.Contains("GetInspectCardScreen", StringComparison.Ordinal)
                    && CountOccurrences(dispatch, "EmitGuiRightClick(target, position);") == 1
                    && CountOccurrences(dispatch, "EmitGlobalRightClick(position);") == 1
                    && guiDispatch >= 0
                    && guiReturn > guiDispatch
                    && globalDispatch > guiReturn
                    && targets.Contains(
                        "optional\tmethod\tMegaCrit.Sts2.Core.Nodes.NGame\t_Input\tbare\t-",
                        StringComparison.Ordinal
                    ),
                "the gesture was not connected to an audited, exactly-once input route"
            );
        }
    );

    Run(
        "Steam account switching isolates saves without deleting shared or legacy data",
        () =>
        {
            const ulong firstId = 76561198000000001;
            const ulong secondId = 76561198000000002;
            const string firstSlot = "11111111111111111111111111111111";
            const string secondSlot = "22222222222222222222222222222222";
            var payload = Convert
                .ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{{\"sub\":\"{firstId}\"}}"))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var token = $"e30.{payload}.signature-fixture";
            Assert(
                SteamAccountIdentity.TryGetSteamId(token, out var parsedId) && parsedId == firstId,
                "the encrypted-vault token subject did not resolve to its SteamID"
            );
            Assert(
                !SteamAccountIdentity.TryGetSteamId("not-a-token", out _)
                    && !SteamAccountIdentity.TryGetSteamId("e30.W10.sig", out _),
                "a malformed or subject-less token crossed the identity boundary"
            );
            Assert(
                AccountSessionGuard.CanCommitRenewal(7, 7, firstId, firstId, true)
                    && !AccountSessionGuard.CanCommitRenewal(7, 8, firstId, firstId, true)
                    && !AccountSessionGuard.CanCommitRenewal(7, 7, firstId, secondId, true)
                    && !AccountSessionGuard.CanCommitRenewal(7, 7, firstId, firstId, false),
                "a stale generation, account, or connection could commit a renewed token"
            );

            var validVault = new[]
            {
                new SteamCredentialDescriptor(firstId, true, true, firstSlot),
                new SteamCredentialDescriptor(secondId, true, true, secondSlot),
            };
            Assert(
                SteamCredentialVaultPolicy.Validate(2, 2, firstId, validVault)
                    == SteamCredentialVaultError.None
                    && SteamCredentialVaultPolicy.Validate(3, 2, firstId, validVault)
                        == SteamCredentialVaultError.UnsupportedVersion
                    && SteamCredentialVaultPolicy.Validate(2, 2, firstId, null)
                        == SteamCredentialVaultError.MissingAccounts
                    && SteamCredentialVaultPolicy.Validate(
                        2,
                        2,
                        firstId,
                        validVault.Append(validVault[0]).ToArray()
                    ) == SteamCredentialVaultError.DuplicateAccount
                    && SteamCredentialVaultPolicy.Validate(
                        2,
                        2,
                        firstId,
                        new[] { new SteamCredentialDescriptor(firstId, true, true, "invalid") }
                    ) == SteamCredentialVaultError.InvalidDataSlot
                    && SteamCredentialVaultPolicy.Validate(2, 2, 999, validVault)
                        == SteamCredentialVaultError.InvalidActiveAccount,
                "the encrypted account vault accepted ambiguous or unrecoverable metadata"
            );
            Assert(
                validVault[0].ToString() == nameof(SteamCredentialDescriptor),
                "credential descriptor default ToString can expose identity or slot data"
            );

            var vaultFixture = Path.Combine(root, "vault-transaction", "credentials.enc");
            Directory.CreateDirectory(Path.GetDirectoryName(vaultFixture)!);
            File.WriteAllText(vaultFixture, "old-encrypted-vault");
            Assert(
                !NonDestructiveFileTransaction.TryWriteAtomic(
                    vaultFixture,
                    "new-encrypted-vault",
                    out var publishFailure,
                    beforePublish: () => throw new IOException("injected before publish")
                )
                    && publishFailure == nameof(IOException)
                    && File.ReadAllText(vaultFixture) == "old-encrypted-vault",
                "a failed vault publish replaced or removed the prior encrypted vault"
            );
            Assert(
                NonDestructiveFileTransaction.TryWriteAtomic(
                    vaultFixture,
                    "new-encrypted-vault",
                    out publishFailure
                )
                    && publishFailure == null
                    && File.ReadAllText(vaultFixture) == "new-encrypted-vault",
                "a retry could not atomically publish the new encrypted vault"
            );

            var dataDir = Path.Combine(root, "account-isolation");
            var legacy = Path.Combine(dataDir, "default", "1", "profile1", "saves");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "progress.save"), "legacy-progress");
            File.WriteAllText(Path.Combine(dataDir, "cloud_sync_enabled"), "false");
            File.WriteAllText(Path.Combine(dataDir, "pending_upload_batch"), "12345");
            var firstSave = Path.Combine(
                dataDir,
                "account_profiles",
                firstSlot,
                "data",
                "default",
                "1",
                "profile1",
                "saves",
                "progress.save"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(firstSave)!);
            File.WriteAllText(firstSave, "account-progress");

            Assert(
                AccountDataIsolation.TryActivate(dataDir, firstSlot, out var activationError)
                    && activationError == null,
                "the first account data directory could not be activated"
            );
            Assert(
                File.ReadAllText(firstSave) == "account-progress",
                "legacy adoption overwrote existing account data"
            );
            Assert(
                File.Exists(Path.Combine(legacy, "progress.save")),
                "legacy save data was removed during adoption"
            );
            Assert(
                File.ReadAllText(
                    Path.Combine(dataDir, "account_profiles", firstSlot, "cloud_sync_enabled")
                ) == "false"
                    && File.ReadAllText(
                        Path.Combine(dataDir, "account_profiles", firstSlot, "pending_upload_batch")
                    ) == "12345"
                    && File.Exists(Path.Combine(dataDir, "cloud_sync_enabled"))
                    && File.Exists(Path.Combine(dataDir, "pending_upload_batch")),
                "legacy cloud preference or upload-recovery state was not copied and preserved"
            );
            Assert(
                AccountDataIsolation.RewriteLocalGodotPath(
                    "user://default/1/profile1/saves/progress.save"
                )
                    == $"user://account_profiles/{firstSlot}/data/default/1/profile1/saves/progress.save",
                "local Godot I/O did not resolve to the active opaque account slot"
            );
            var scopedSettings =
                $"user://account_profiles/{firstSlot}/data/default/1/settings.save.tmp";
            Assert(
                AccountDataIsolation.RewriteLocalGodotPath(scopedSettings) == scopedSettings
                    && AccountDataIsolation.RewriteLocalGodotPath(
                        "user://default/1/" + scopedSettings
                    ) == scopedSettings,
                "a resolved account path was nested again during atomic save rename"
            );

            Assert(
                AccountDataIsolation.TryActivate(dataDir, secondSlot, out activationError),
                "a second account data directory could not be activated"
            );
            Assert(
                !File.Exists(
                    Path.Combine(
                        dataDir,
                        "account_profiles",
                        secondSlot,
                        "data",
                        "default",
                        "1",
                        "profile1",
                        "saves",
                        "progress.save"
                    )
                ),
                "legacy data was cloned into more than the first adopted account"
            );
            Assert(
                AccountDataIsolation
                    .GetAccountPreferencePath(dataDir, "cloud_sync_enabled")
                    .Contains(secondSlot, StringComparison.Ordinal),
                "launcher preferences were not account scoped"
            );
            Assert(
                AccountDataIsolation.RewriteLocalGodotPath("user://settings/global.cfg")
                    == "user://settings/global.cfg",
                "account isolation rewrote data outside the game account root"
            );

            Assert(
                AccountDataIsolation.TryActivate(dataDir, firstSlot, out activationError),
                "the first account could not be reactivated for backup adoption"
            );
            var externalSaves = Path.Combine(root, "external-backups");
            var legacyBackupFile = Path.Combine(
                externalSaves,
                "manual",
                "legacy-set",
                "default",
                "1",
                "progress.save"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(legacyBackupFile)!);
            File.WriteAllText(legacyBackupFile, "legacy-backup");
            Assert(
                AccountDataIsolation.TryAdoptExternalBackups(dataDir, externalSaves),
                "the first account did not adopt legacy external backups"
            );
            Assert(
                File.Exists(legacyBackupFile)
                    && File.Exists(
                        Path.Combine(
                            externalSaves,
                            "accounts",
                            firstSlot,
                            "manual",
                            "legacy-set",
                            "default",
                            "1",
                            "progress.save"
                        )
                    ),
                "legacy external backup adoption removed the source or missed the destination"
            );
            Assert(
                AccountDataIsolation.TryActivate(dataDir, secondSlot, out activationError)
                    && !AccountDataIsolation.TryAdoptExternalBackups(dataDir, externalSaves)
                    && !Directory.Exists(
                        Path.Combine(externalSaves, "accounts", secondSlot, "manual", "legacy-set")
                    ),
                "a later account inherited the first account's legacy backup sets"
            );

            const string accountName = "privacy_fixture_user";
            const string secretToken = "privacy_fixture_token";
            const string guardData = "privacy_fixture_guard";
            SensitiveLogRedactor.RegisterAccount(accountName, secondId, secretToken, guardData);
            SensitiveLogRedactor.RegisterOpaqueValue(secondSlot);
            var redacted = SensitiveLogRedactor.Redact(
                $"account={accountName} id={secondId} token={secretToken} guard={guardData} slot={secondSlot}"
            );
            Assert(
                !redacted.Contains(accountName, StringComparison.OrdinalIgnoreCase)
                    && !redacted.Contains(secondId.ToString(), StringComparison.Ordinal)
                    && !redacted.Contains(secretToken, StringComparison.Ordinal)
                    && !redacted.Contains(guardData, StringComparison.Ordinal)
                    && !redacted.Contains(secondSlot, StringComparison.Ordinal),
                "account identity, token, Guard data, or opaque slot survived diagnostic redaction"
            );

            var repository = FindRepositoryRoot();
            var storeSource = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "SteamCredentialStore.cs")
            );
            var modelSource = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherModel.cs")
            );
            var authSource = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "SteamAuth.cs")
            );
            var backupSource = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "LocalBackupService.cs")
            );
            var pendingBatchSource = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "PendingUploadBatch.cs")
            );
            var androidSource = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            var controllerSource = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherController.cs")
            );
            var encryptMethodIndex = androidSource.IndexOf(
                "public String encryptString",
                StringComparison.Ordinal
            );
            var decryptMethodIndex = androidSource.IndexOf(
                "public String decryptString",
                StringComparison.Ordinal
            );
            var encryptKeyIndex =
                encryptMethodIndex < 0
                    ? -1
                    : androidSource.IndexOf(
                        "SecretKey key = getOrCreateKeystoreKey();",
                        encryptMethodIndex,
                        StringComparison.Ordinal
                    );
            var decryptKeyIndex =
                decryptMethodIndex < 0
                    ? -1
                    : androidSource.IndexOf(
                        "SecretKey key = getExistingKeystoreKey();",
                        decryptMethodIndex,
                        StringComparison.Ordinal
                    );
            var sessionStartIndex = controllerSource.IndexOf(
                "var result = _model.StartSession();",
                StringComparison.Ordinal
            );
            var externalDirectoryIndex = controllerSource.IndexOf(
                "AppPaths.EnsureExternalDirectories();",
                StringComparison.Ordinal
            );
            Assert(
                storeSource.Contains("CredentialVault", StringComparison.Ordinal)
                    && storeSource.Contains("TryActivate", StringComparison.Ordinal)
                    && storeSource.Contains("public bool LoadFailed", StringComparison.Ordinal)
                    && storeSource.Contains(
                        "SteamCredentialVaultPolicy.Validate",
                        StringComparison.Ordinal
                    )
                    && !storeSource.Contains("generatedSlot", StringComparison.Ordinal)
                    && !storeSource.Contains(
                        "File.Delete(_credentialsPath)",
                        StringComparison.Ordinal
                    )
                    && !storeSource.Contains("deleteKeystoreKey", StringComparison.Ordinal)
                    && !androidSource.Contains("deleteKeystoreKey", StringComparison.Ordinal)
                    && encryptMethodIndex >= 0
                    && decryptMethodIndex > encryptMethodIndex
                    && encryptKeyIndex > encryptMethodIndex
                    && encryptKeyIndex < decryptMethodIndex
                    && decryptKeyIndex > decryptMethodIndex
                    && modelSource.Contains(
                        "AccountSessionGuard.CanCommitRenewal",
                        StringComparison.Ordinal
                    )
                    && modelSource.Contains(
                        "SteamKit2CloudSaveStore.Instance",
                        StringComparison.Ordinal
                    )
                    && modelSource.Contains(
                        "FastPathResult.AccountDataUnavailable",
                        StringComparison.Ordinal
                    )
                    && modelSource.Contains(
                        "SensitiveLogRedactor.RegisterAccount(\n                result.AccountName",
                        StringComparison.Ordinal
                    )
                    && modelSource.IndexOf(
                        "SensitiveLogRedactor.RegisterAccount(\n                result.AccountName",
                        StringComparison.Ordinal
                    )
                        < modelSource.IndexOf(
                            "new SteamConnection(result.AccountName, result.RefreshToken)",
                            StringComparison.Ordinal
                        )
                    && !authSource.Contains("Authenticating as '", StringComparison.Ordinal)
                    && !authSource.Contains(
                        "Authentication successful for '",
                        StringComparison.Ordinal
                    )
                    && authSource.Contains(
                        "public override string ToString() => nameof(AuthResult);",
                        StringComparison.Ordinal
                    )
                    && storeSource.Contains(
                        "public override string ToString() => nameof(SteamAccountSummary);",
                        StringComparison.Ordinal
                    ),
                "account switching lost its no-delete, stale-session, or no-account-log guard"
            );
            Assert(
                backupSource.Contains(
                    "AccountDataIsolation.RewriteLocalGodotPath",
                    StringComparison.Ordinal
                )
                    && pendingBatchSource.Contains(
                        "AccountDataIsolation.GetAccountPreferencePath",
                        StringComparison.Ordinal
                    ),
                "backup restore or stale cloud-batch recovery bypassed the active account slot"
            );
            Assert(
                sessionStartIndex >= 0 && externalDirectoryIndex > sessionStartIndex,
                "external backup adoption ran before the active account slot was established"
            );
            Assert(
                !File.Exists(
                    Path.Combine(repository, "src", "STS2Mobile", "Patches", "PrivacyLogPatches.cs")
                ),
                "a global hot-path game logger patch was reintroduced despite its native crash regression"
            );
        }
    );

    Run(
        "LAN invite codes are versioned, direct-only, and fail closed",
        () =>
        {
            var local = new LanJoinEndpoint(IPAddress.Parse("192.168.10.8"), 33771);
            var code = LanInviteCode.Format(local);
            Assert(code == "sts2lan:v1:192.168.10.8:33771", "the v1 invite wire format drifted");
            Assert(
                LanInviteCode.TryParseJoinInput(code, out var parsed, out var parseError)
                    && parsed == local
                    && parseError == LanInviteParseError.None,
                "a canonical v1 invite did not round-trip"
            );
            Assert(
                LanInviteCode.TryParseJoinInput("10.0.0.7", out var defaultPort, out parseError)
                    && defaultPort.Port == LanInviteCode.DefaultGamePort
                    && defaultPort.Address.Equals(IPAddress.Parse("10.0.0.7")),
                "plain IPv4 must keep the legacy default-port behavior"
            );
            Assert(
                LanInviteCode.TryParseJoinInput(
                    "100.64.1.2:40123",
                    out var explicitPort,
                    out parseError
                )
                    && explicitPort.Port == 40123,
                "plain IPv4:port must remain accepted"
            );

            var rejected = new Dictionary<string, LanInviteParseError>
            {
                [""] = LanInviteParseError.Empty,
                ["sts2lan:v2:192.168.1.2:33771"] = LanInviteParseError.UnsupportedVersion,
                ["sts2lan:v1:example.com:33771"] = LanInviteParseError.InvalidAddress,
                ["sts2lan:v1:::1:33771"] = LanInviteParseError.InvalidFormat,
                ["192.168.1:33771"] = LanInviteParseError.InvalidAddress,
                ["192.168.001.2:33771"] = LanInviteParseError.InvalidAddress,
                ["0xC0.0xA8.0x01.0x02:33771"] = LanInviteParseError.InvalidAddress,
                ["0300.0250.01.02:33771"] = LanInviteParseError.InvalidAddress,
                ["sts2lan:v1:192.168.1.2:0"] = LanInviteParseError.InvalidPort,
                ["sts2lan:v1:192.168.1.2:65536"] = LanInviteParseError.InvalidPort,
                ["sts2lan:v1:192.168.1.2:+33771"] = LanInviteParseError.InvalidPort,
                ["sts2lan:v1:192.168.1.2: 33771"] = LanInviteParseError.InvalidPort,
                ["127.0.0.1:33771"] = LanInviteParseError.UnsafeAddress,
                ["0.0.0.0:33771"] = LanInviteParseError.UnsafeAddress,
                ["239.1.2.3:33771"] = LanInviteParseError.UnsafeAddress,
            };
            foreach (var (input, expectedError) in rejected)
            {
                Assert(
                    !LanInviteCode.TryParseJoinInput(input, out _, out parseError)
                        && parseError == expectedError,
                    $"unsafe invite fixture crossed the parser boundary: {expectedError}"
                );
            }
            Assert(
                !LanInviteCode.TryParseJoinInput(
                    new string('1', LanInviteCode.MaxInputLength + 1),
                    out _,
                    out parseError
                )
                    && parseError == LanInviteParseError.TooLong,
                "oversized invite input was accepted"
            );

            var candidates = LanInviteCode.SelectShareableEndpoints(
                new[]
                {
                    IPAddress.Loopback,
                    IPAddress.Any,
                    IPAddress.Parse("169.254.4.5"),
                    IPAddress.Parse("224.0.0.1"),
                    IPAddress.Parse("8.8.8.8"),
                    IPAddress.Parse("100.64.2.3"),
                    IPAddress.Parse("192.168.1.20"),
                    IPAddress.Parse("192.168.1.20"),
                }
            );
            Assert(
                candidates
                    .Select(c => c.Address.ToString())
                    .SequenceEqual(new[] { "192.168.1.20", "100.64.2.3", "8.8.8.8" }),
                "share candidates retained an unsafe address or lost deterministic priority"
            );
            var routePreferred = LanInviteCode.SelectShareableEndpoints(
                new[] { IPAddress.Parse("172.30.10.2"), IPAddress.Parse("192.168.1.20") },
                preferredAddress: IPAddress.Parse("192.168.1.20")
            );
            Assert(
                routePreferred[0].Address.Equals(IPAddress.Parse("192.168.1.20"))
                    && routePreferred[1].Address.Equals(IPAddress.Parse("172.30.10.2")),
                "the default-route address did not outrank a virtual private interface"
            );
            var manyCandidates = LanInviteCode.SelectShareableEndpoints(
                Enumerable.Range(1, 20).Select(last => IPAddress.Parse($"10.0.0.{last}"))
            );
            Assert(
                manyCandidates.Count == LanInviteCode.MaxShareChoices
                    && manyCandidates[0].Address.Equals(IPAddress.Parse("10.0.0.1"))
                    && manyCandidates[^1].Address.Equals(IPAddress.Parse("10.0.0.8")),
                "share candidates exceeded the Android chooser budget or lost priority order"
            );

            var repository = FindRepositoryRoot();
            var lanPatcher = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LanMultiplayerPatcher.cs")
            );
            var android = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            Assert(
                lanPatcher.Contains("InviteButtonOnReleasePrefix", StringComparison.Ordinal)
                    && lanPatcher.Contains("showLanInviteChooser", StringComparison.Ordinal)
                    && lanPatcher.Contains(
                        "LanInviteCode.TryParseJoinInput",
                        StringComparison.Ordinal
                    )
                    && android.Contains("Intent.ACTION_SEND", StringComparison.Ordinal)
                    && android.Contains("Intent.createChooser", StringComparison.Ordinal)
                    && android.Contains("ClipboardManager", StringComparison.Ordinal)
                    && android.Contains(
                        "private AlertDialog lanInviteDialog",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "dismissLanInviteDialogOnUiThread();",
                        StringComparison.Ordinal
                    )
                    && android.Contains("debug_force_lan_invite_chooser", StringComparison.Ordinal)
                    && lanPatcher.Contains("DismissLanInviteChooser();", StringComparison.Ordinal)
                    && !lanPatcher.Contains("Joining LAN game at {ip}", StringComparison.Ordinal)
                    && !lanPatcher.Contains(
                        "Discovered LAN host: {hostname}",
                        StringComparison.Ordinal
                    )
                    && !lanPatcher.Contains(
                        "Text override failed for {ip}",
                        StringComparison.Ordinal
                    )
                    && !android.Contains("share.setPackage(", StringComparison.Ordinal),
                "LAN invite UI bypassed the bounded parser, Sharesheet, or private-log boundary"
            );
        }
    );

    Run(
        "Steam lobby direct-invite metadata is closed, bounded, and replay-safe",
        () =>
        {
            const long now = 1_700_000_000;
            const string launcherBuild = "0.4.8";
            const string gameBuild = "public-107.1";
            const string firstNonce = "ABCDEFGHIJKLMNOPQRSTUV";
            const string secondNonce = "ZYXWVUTSRQPONMLKJIHGFE";

            var generatedNonce = SteamLobbyInviteMetadata.CreateNonce();
            var generatedNonceFixture = CreateSteamInviteMetadataFixture(now + 300, generatedNonce);
            Assert(
                generatedNonce.Length == SteamLobbyInviteMetadata.NonceLength
                    && SteamLobbyInviteMetadata.TryValidateAndConsume(
                        generatedNonceFixture,
                        launcherBuild,
                        gameBuild,
                        now,
                        SteamLobbyInviteSenderTrust.Friend,
                        new SteamLobbyInviteReplayGuard(),
                        out _,
                        out _
                    ),
                "the production CSPRNG nonce generator violated the metadata wire contract"
            );

            var createdMetadata = SteamLobbyInviteMetadata.Create(
                launcherBuild,
                gameBuild,
                new[]
                {
                    new LanJoinEndpoint(IPAddress.Parse("192.168.10.8"), 33771),
                    new LanJoinEndpoint(IPAddress.Parse("10.0.0.7"), 33771),
                },
                now
            );
            Assert(
                SteamLobbyInviteMetadata.TryValidateAndConsume(
                    createdMetadata,
                    launcherBuild,
                    gameBuild,
                    now,
                    SteamLobbyInviteSenderTrust.Friend,
                    new SteamLobbyInviteReplayGuard(),
                    out var createdInvite,
                    out _
                )
                    && createdInvite.EndpointCandidates.Count == 2,
                "the production metadata builder did not round-trip its closed schema"
            );
            bool duplicateBuilderRejected = false;
            try
            {
                var duplicate = new LanJoinEndpoint(IPAddress.Parse("10.0.0.7"), 33771);
                SteamLobbyInviteMetadata.Create(
                    launcherBuild,
                    gameBuild,
                    new[] { duplicate, duplicate },
                    now
                );
            }
            catch (ArgumentException)
            {
                duplicateBuilderRejected = true;
            }
            Assert(
                duplicateBuilderRejected,
                "the metadata producer emitted duplicate endpoint candidates"
            );

            var sendLimiter = new SteamLobbyInviteSendRateLimiter();
            Assert(
                sendLimiter.TryAcquire(0, now) == SteamLobbyInviteSendRateResult.InvalidTarget
                    && sendLimiter.TryAcquire(11, now) == SteamLobbyInviteSendRateResult.Accepted
                    && sendLimiter.TryAcquire(11, now + 1)
                        == SteamLobbyInviteSendRateResult.TargetCooldown
                    && sendLimiter.TryAcquire(12, now + 1)
                        == SteamLobbyInviteSendRateResult.Accepted
                    && sendLimiter.TryAcquire(13, now + 2)
                        == SteamLobbyInviteSendRateResult.Accepted
                    && sendLimiter.TryAcquire(14, now + 3)
                        == SteamLobbyInviteSendRateResult.GlobalLimit
                    && sendLimiter.TryAcquire(
                        11,
                        now + SteamLobbyInviteSendRateLimiter.WindowSeconds
                    ) == SteamLobbyInviteSendRateResult.Accepted,
                "Steam friend invite rate limiting lost its target or global bound"
            );
            Assert(
                SteamLobbyInviteSessionGuard.CanHandleCallback(7, 7, 11, 11, true, true)
                    && !SteamLobbyInviteSessionGuard.CanHandleCallback(6, 7, 11, 11, true, true)
                    && !SteamLobbyInviteSessionGuard.CanHandleCallback(7, 7, 11, 12, true, true)
                    && !SteamLobbyInviteSessionGuard.CanHandleCallback(7, 7, 11, 11, false, true)
                    && !SteamLobbyInviteSessionGuard.CanHandleCallback(7, 7, 11, 11, true, false),
                "a stale, cross-account, torn-down, or disconnected invite callback was accepted"
            );
            Assert(
                SteamLobbyInviteSessionGuard.CanAcceptInvite(now + 1, now, true, true)
                    && !SteamLobbyInviteSessionGuard.CanAcceptInvite(now, now, true, true)
                    && !SteamLobbyInviteSessionGuard.CanAcceptInvite(now + 1, now, false, true)
                    && !SteamLobbyInviteSessionGuard.CanAcceptInvite(now + 1, now, true, false),
                "an expired, replaced, or torn-down prompt remained acceptable"
            );
            Assert(
                SteamLobbyInviteMetadata.IsBuildToken("0.4.8-qa.1")
                    && !SteamLobbyInviteMetadata.IsBuildToken("public/private")
                    && !SteamLobbyInviteMetadata.IsBuildToken("public build")
                    && !SteamLobbyInviteMetadata.IsBuildToken(new string('a', 49)),
                "build identities no longer fail closed to the bounded token grammar"
            );

            var valid = CreateSteamInviteMetadataFixture(
                now + 300,
                firstNonce,
                "sts2lan:v1:192.168.10.8:33771,sts2lan:v1:10.0.0.7:33771"
            );
            var replayGuard = new SteamLobbyInviteReplayGuard();
            Assert(
                SteamLobbyInviteMetadata.TryValidateAndConsume(
                    valid,
                    launcherBuild,
                    gameBuild,
                    now,
                    SteamLobbyInviteSenderTrust.Friend,
                    replayGuard,
                    out var invite,
                    out var metadataError
                )
                    && metadataError == SteamLobbyInviteMetadataError.None
                    && invite.EndpointCandidates.Count == 2
                    && invite.LauncherBuild == launcherBuild
                    && invite.GameBuild == gameBuild
                    && invite.ToString() == nameof(SteamLobbyDirectInvite)
                    && !invite.ToString().Contains("192.168", StringComparison.Ordinal)
                    && !invite.ToString().Contains(firstNonce, StringComparison.Ordinal),
                "a valid direct-invite contract failed or exposed private connection metadata"
            );
            Assert(
                !SteamLobbyInviteMetadata.TryValidateAndConsume(
                    valid,
                    launcherBuild,
                    gameBuild,
                    now,
                    SteamLobbyInviteSenderTrust.Friend,
                    replayGuard,
                    out _,
                    out metadataError
                )
                    && metadataError == SteamLobbyInviteMetadataError.Replay,
                "a consumed invite nonce produced a duplicate prompt"
            );

            var concurrentGuard = new SteamLobbyInviteReplayGuard();
            var concurrentMetadata = CreateSteamInviteMetadataFixture(
                now + 300,
                "0123456789abcdefghijkl"
            );
            int concurrentAcceptCount = 0;
            Parallel.For(
                0,
                16,
                iteration =>
                {
                    _ = iteration;
                    if (
                        SteamLobbyInviteMetadata.TryValidateAndConsume(
                            concurrentMetadata,
                            launcherBuild,
                            gameBuild,
                            now,
                            SteamLobbyInviteSenderTrust.Friend,
                            concurrentGuard,
                            out _,
                            out _
                        )
                    )
                        Interlocked.Increment(ref concurrentAcceptCount);
                }
            );
            Assert(
                concurrentAcceptCount == 1,
                "concurrent duplicate callbacks produced more than one invite prompt"
            );

            void AssertRejected(
                Dictionary<string, string> candidate,
                SteamLobbyInviteMetadataError expectedError,
                string context,
                SteamLobbyInviteSenderTrust trust = SteamLobbyInviteSenderTrust.Friend,
                SteamLobbyInviteReplayGuard guard = null
            )
            {
                Assert(
                    !SteamLobbyInviteMetadata.TryValidateAndConsume(
                        candidate,
                        launcherBuild,
                        gameBuild,
                        now,
                        trust,
                        guard ?? new SteamLobbyInviteReplayGuard(),
                        out _,
                        out var actualError
                    )
                        && actualError == expectedError,
                    $"{context}: expected {expectedError}, got {actualError}"
                );
            }

            var missing = new Dictionary<string, string>(valid, StringComparer.Ordinal);
            missing.Remove(SteamLobbyInviteMetadata.NonceKey);
            AssertRejected(missing, SteamLobbyInviteMetadataError.MissingField, "missing nonce");

            var unknown = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                ["account_name"] = "must-not-cross-boundary",
            };
            AssertRejected(
                unknown,
                SteamLobbyInviteMetadataError.UnknownField,
                "unknown/private field"
            );

            var wrongCase = new Dictionary<string, string>(valid, StringComparer.Ordinal);
            wrongCase.Remove(SteamLobbyInviteMetadata.SchemaKey);
            wrongCase["STS2MM_SCHEMA"] = SteamLobbyInviteMetadata.SchemaV1;
            AssertRejected(
                wrongCase,
                SteamLobbyInviteMetadataError.UnknownField,
                "non-canonical key casing"
            );

            var tooLong = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.EndpointsKey] = new string(
                    'a',
                    SteamLobbyInviteMetadata.MaxMetadataCharacters
                ),
            };
            AssertRejected(tooLong, SteamLobbyInviteMetadataError.TooLong, "oversized metadata");

            var unsupportedSchema = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.SchemaKey] = "sts2mm-direct-v2",
            };
            AssertRejected(
                unsupportedSchema,
                SteamLobbyInviteMetadataError.UnsupportedSchema,
                "unknown schema"
            );

            var wrongApp = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.AppIdKey] = "480",
            };
            AssertRejected(wrongApp, SteamLobbyInviteMetadataError.WrongAppId, "wrong App ID");

            var wrongTransport = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.TransportKey] = "steam-relay",
            };
            AssertRejected(
                wrongTransport,
                SteamLobbyInviteMetadataError.UnknownTransport,
                "unsupported transport"
            );

            var invalidBuild = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.GameBuildKey] = "../../private/path",
            };
            AssertRejected(
                invalidBuild,
                SteamLobbyInviteMetadataError.InvalidBuild,
                "non-token build"
            );

            var oldLauncher = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.LauncherBuildKey] = "0.4.7",
            };
            AssertRejected(
                oldLauncher,
                SteamLobbyInviteMetadataError.IncompatibleLauncherBuild,
                "launcher mismatch"
            );

            var oldGame = new Dictionary<string, string>(valid, StringComparer.Ordinal)
            {
                [SteamLobbyInviteMetadata.GameBuildKey] = "public-106.9",
            };
            AssertRejected(
                oldGame,
                SteamLobbyInviteMetadataError.IncompatibleGameBuild,
                "game mismatch"
            );

            foreach (var invalidExpiry in new[] { "", "01700000300", "+1700000300", "x" })
            {
                var candidate = new Dictionary<string, string>(valid, StringComparer.Ordinal)
                {
                    [SteamLobbyInviteMetadata.ExpiresKey] = invalidExpiry,
                };
                AssertRejected(
                    candidate,
                    SteamLobbyInviteMetadataError.InvalidExpiry,
                    "invalid expiry"
                );
            }

            var expired = CreateSteamInviteMetadataFixture(now, secondNonce);
            AssertRejected(expired, SteamLobbyInviteMetadataError.Expired, "expired invite");

            var future = CreateSteamInviteMetadataFixture(
                now + SteamLobbyInviteMetadata.MaxFutureLifetimeSeconds + 1,
                secondNonce
            );
            AssertRejected(
                future,
                SteamLobbyInviteMetadataError.ExpiryTooFarInFuture,
                "unbounded future expiry"
            );

            foreach (
                var invalidNonce in new[]
                {
                    "short",
                    "ABCDEFGHIJKLMNOPQRSTU=",
                    "ABCDEFGHIJKLMNOPQRSTU!",
                }
            )
            {
                var candidate = CreateSteamInviteMetadataFixture(now + 300, invalidNonce);
                AssertRejected(
                    candidate,
                    SteamLobbyInviteMetadataError.InvalidNonce,
                    "invalid nonce"
                );
            }

            var plainEndpoint = CreateSteamInviteMetadataFixture(
                now + 300,
                secondNonce,
                "192.168.10.8:33771"
            );
            AssertRejected(
                plainEndpoint,
                SteamLobbyInviteMetadataError.InvalidEndpoint,
                "non-canonical endpoint"
            );

            var unsafeEndpoint = CreateSteamInviteMetadataFixture(
                now + 300,
                secondNonce,
                "sts2lan:v1:127.0.0.1:33771"
            );
            AssertRejected(
                unsafeEndpoint,
                SteamLobbyInviteMetadataError.InvalidEndpoint,
                "unsafe endpoint"
            );

            var duplicateEndpoint = CreateSteamInviteMetadataFixture(
                now + 300,
                secondNonce,
                "sts2lan:v1:10.0.0.7:33771,sts2lan:v1:10.0.0.7:33771"
            );
            AssertRejected(
                duplicateEndpoint,
                SteamLobbyInviteMetadataError.DuplicateEndpoint,
                "duplicate endpoint"
            );

            var tooManyEndpoints = CreateSteamInviteMetadataFixture(
                now + 300,
                secondNonce,
                string.Join(
                    ',',
                    Enumerable
                        .Range(1, LanInviteCode.MaxShareChoices + 1)
                        .Select(value => $"sts2lan:v1:10.0.0.{value}:33771")
                )
            );
            AssertRejected(
                tooManyEndpoints,
                SteamLobbyInviteMetadataError.TooManyEndpoints,
                "too many endpoints"
            );

            var trustGuard = new SteamLobbyInviteReplayGuard();
            AssertRejected(
                valid,
                SteamLobbyInviteMetadataError.UntrustedSender,
                "non-friend sender",
                SteamLobbyInviteSenderTrust.NonFriend,
                trustGuard
            );
            AssertRejected(
                valid,
                SteamLobbyInviteMetadataError.UntrustedSender,
                "blocked sender",
                SteamLobbyInviteSenderTrust.Blocked,
                trustGuard
            );
            Assert(
                SteamLobbyInviteMetadata.TryValidateAndConsume(
                    valid,
                    launcherBuild,
                    gameBuild,
                    now,
                    SteamLobbyInviteSenderTrust.Friend,
                    trustGuard,
                    out _,
                    out metadataError
                ),
                "an untrusted sender consumed the nonce before a trusted callback"
            );

            Assert(
                !SteamLobbyInviteMetadata.TryValidateAndConsume(
                    valid,
                    launcherBuild,
                    gameBuild,
                    now,
                    SteamLobbyInviteSenderTrust.Friend,
                    replayGuard: null,
                    out _,
                    out metadataError
                )
                    && metadataError == SteamLobbyInviteMetadataError.ReplayStateUnavailable,
                "missing replay state did not fail closed"
            );

            var boundedGuard = new SteamLobbyInviteReplayGuard(capacity: 1);
            var firstBounded = CreateSteamInviteMetadataFixture(now + 100, firstNonce);
            var secondBounded = CreateSteamInviteMetadataFixture(now + 100, secondNonce);
            Assert(
                SteamLobbyInviteMetadata.TryValidateAndConsume(
                    firstBounded,
                    launcherBuild,
                    gameBuild,
                    now,
                    SteamLobbyInviteSenderTrust.Friend,
                    boundedGuard,
                    out _,
                    out _
                ),
                "the bounded replay guard rejected its first invite"
            );
            AssertRejected(
                secondBounded,
                SteamLobbyInviteMetadataError.ReplayCapacityExceeded,
                "live replay capacity",
                SteamLobbyInviteSenderTrust.Friend,
                boundedGuard
            );
            var afterExpiry = CreateSteamInviteMetadataFixture(now + 200, secondNonce);
            Assert(
                SteamLobbyInviteMetadata.TryValidateAndConsume(
                    afterExpiry,
                    launcherBuild,
                    gameBuild,
                    now + 101,
                    SteamLobbyInviteSenderTrust.Friend,
                    boundedGuard,
                    out _,
                    out metadataError
                ),
                "expired replay entries were not reclaimed within the fixed capacity"
            );

            var repository = FindRepositoryRoot();
            var source = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Multiplayer",
                    "SteamLobbyInviteMetadata.cs"
                )
            );
            Assert(
                !source.Contains("PatchHelper.Log", StringComparison.Ordinal)
                    && !source.Contains("SteamID", StringComparison.Ordinal)
                    && !source.Contains("refreshToken", StringComparison.Ordinal)
                    && !source.Contains("accountName", StringComparison.Ordinal),
                "the pure Steam invite boundary acquired a credential, identity, or logging dependency"
            );
        }
    );

    Run(
        "Steam friend picker searches names safely and applies the requested rank",
        () =>
        {
            Assert(
                SteamInviteFriendListPolicy.Matches("Persona", "备注昵称", "sona")
                    && SteamInviteFriendListPolicy.Matches("Persona", "备注昵称", "备注")
                    && !SteamInviteFriendListPolicy.Matches("Persona", "备注昵称", "missing"),
                "friend search did not match both Steam persona names and nicknames"
            );
            Assert(
                SteamInviteFriendListPolicy.IsVisible(isOnline: true, showOffline: false)
                    && !SteamInviteFriendListPolicy.IsVisible(isOnline: false, showOffline: false)
                    && SteamInviteFriendListPolicy.IsVisible(
                        isOnline: false,
                        showOffline: false,
                        query: "目标"
                    )
                    && SteamInviteFriendListPolicy.IsVisible(isOnline: false, showOffline: true),
                "offline friends were not hidden by default or recoverable by search/toggle"
            );
            int nickname = SteamInviteFriendListPolicy.Rank(true, false, false, false);
            int playing = SteamInviteFriendListPolicy.Rank(false, true, false, true);
            int recent = SteamInviteFriendListPolicy.Rank(false, false, true, true);
            int online = SteamInviteFriendListPolicy.Rank(false, false, false, true);
            Assert(
                nickname > playing && playing > recent && recent > online,
                "friend rank is not nickname, playing STS2, recently played STS2, then online"
            );
            Assert(
                SteamInviteFriendListPolicy.PrimaryName("Persona", "Remark") == "Remark"
                    && SteamInviteFriendListPolicy.PrimaryName("Persona", "") == "Persona",
                "nickname did not become the primary visible identity"
            );
            var moreThanOneRenderWindow = Enumerable
                .Range(0, SteamInviteFriendListPolicy.MaxRenderedFriends + 1)
                .Select(index =>
                    index == SteamInviteFriendListPolicy.MaxRenderedFriends
                        ? "目标好友"
                        : $"Friend {index}"
                )
                .Where(name => SteamInviteFriendListPolicy.Matches(name, "", "目标"));
            var lateSearchMatch = SteamInviteFriendListPolicy.RenderWindow(moreThanOneRenderWindow);
            Assert(
                lateSearchMatch.Count == 1 && lateSearchMatch[0] == "目标好友",
                "a friend beyond the first rendered 200 rows disappeared before search"
            );
        }
    );

    Run(
        "Steam invite production flow is explicit, lifecycle-bound, and log-safe",
        () =>
        {
            var repository = FindRepositoryRoot();
            string Read(params string[] parts) =>
                File.ReadAllText(Path.Combine(new[] { repository }.Concat(parts).ToArray()));

            var coordinator = Read("src", "STS2Mobile", "Multiplayer", "SteamInviteCoordinator.cs");
            var bridge = Read("src", "STS2Mobile", "Multiplayer", "SteamLobbyInviteBridge.cs");
            var dialogs = Read("src", "STS2Mobile", "Multiplayer", "SteamInviteDialogs.cs");
            var connection = Read("src", "STS2Mobile", "Steam", "SteamConnection.cs");
            var patcher = Read("src", "STS2Mobile", "Patches", "LanMultiplayerPatcher.cs");
            var lifecycle = Read("src", "STS2Mobile", "Patches", "AppLifecyclePatches.cs");

            Assert(
                coordinator.Contains("SteamInviteMethodDialog", StringComparison.Ordinal)
                    && coordinator.Contains(
                        "SteamInviteFriendPickerDialog",
                        StringComparison.Ordinal
                    )
                    && coordinator.Contains("dialog.Confirmed +=", StringComparison.Ordinal)
                    && coordinator.IndexOf("dialog.Confirmed +=", StringComparison.Ordinal)
                        < coordinator.IndexOf("BeginSendInvite(", StringComparison.Ordinal)
                    && coordinator.Contains(
                        "The invite request was submitted to Steam; delivery is not guaranteed.",
                        StringComparison.Ordinal
                    ),
                "Steam sending bypassed method choice, friend choice, explicit confirmation, or truthful delivery copy"
            );
            Assert(
                coordinator.Contains(
                    "LauncherOverlay.Show(GetOverlayContext(owner), modal);",
                    StringComparison.Ordinal
                )
                    && coordinator.Contains(
                        "GetOverlayContext(owner),\n            message",
                        StringComparison.Ordinal
                    ),
                "host invite overlays can be clipped to the game Invite button instead of the viewport root"
            );
            Assert(
                coordinator.Contains(
                    "result != SteamInviteBridgeResult.Success",
                    StringComparison.Ordinal
                )
                    && coordinator.Contains(
                        "join(screen, endpoint.Address",
                        StringComparison.Ordinal
                    )
                    && coordinator.IndexOf(
                        "result != SteamInviteBridgeResult.Success",
                        StringComparison.Ordinal
                    )
                        < coordinator.IndexOf(
                            "join(screen, endpoint.Address",
                            StringComparison.Ordinal
                        ),
                "ENet join can start before Steam lobby acceptance succeeds"
            );
            Assert(
                coordinator.Contains("BackgroundGraceSeconds = 30", StringComparison.Ordinal)
                    && coordinator.Contains("++_generation", StringComparison.Ordinal)
                    && coordinator.Contains("OnAppBackgrounded", StringComparison.Ordinal)
                    && coordinator.Contains("OnAppForegrounded", StringComparison.Ordinal)
                    && lifecycle.Contains(
                        "SteamInviteCoordinator.OnAppBackgrounded();",
                        StringComparison.Ordinal
                    )
                    && lifecycle.Contains(
                        "SteamInviteCoordinator.OnAppForegrounded();",
                        StringComparison.Ordinal
                    ),
                "HOME/resume no longer expires the Steam connection and stale callback generation"
            );
            Assert(
                coordinator.Contains(
                    "internal static void OnHostDisconnected()",
                    StringComparison.Ordinal
                )
                    && coordinator.Contains(
                        "if (_mode != SurfaceMode.Host)\n                return;",
                        StringComparison.Ordinal
                    ),
                "a client disconnect can tear down a newly opened Join listener"
            );
            Assert(
                CountOccurrences(patcher, "SteamInviteCoordinator.") == 5
                    && patcher.Contains(
                        "SteamInviteCoordinator.OnJoinScreenOpened((Node)__instance, JoinViaIp);",
                        StringComparison.Ordinal
                    )
                    && patcher.Contains(
                        "SteamInviteCoordinator.ShowInviteMethod(owner, endpoints, ShowLanInviteChooser);",
                        StringComparison.Ordinal
                    ),
                "the upstream-sensitive LAN patcher absorbed Steam session ownership instead of narrow lifecycle hooks"
            );
            Assert(
                bridge.Contains("ELobbyType.FriendsOnly", StringComparison.Ordinal)
                    && bridge.Contains(
                        "SteamLobbyInviteSessionGuard.CanAcceptInvite",
                        StringComparison.Ordinal
                    )
                    && CountOccurrences(bridge, "SteamLobbyInviteSessionGuard.CanAcceptInvite") == 2
                    && bridge.Contains("SteamLobbyInviteSendRateLimiter", StringComparison.Ordinal)
                    && bridge.Contains(
                        "TryLeaveLobby(callback.Lobby?.SteamID ?? attemptedLobby);",
                        StringComparison.Ordinal
                    )
                    && CountOccurrences(bridge, "TryLeaveLobby(attemptedLobby);") == 2
                    && !bridge.Contains("PatchHelper.Log", StringComparison.Ordinal),
                "the bridge lost friends-only, expiry recheck, cleanup, rate-limit, or log-free guarantees"
            );
            Assert(
                connection.Contains(
                    "Subscribe<SteamFriends.FriendsListCallback>",
                    StringComparison.Ordinal
                )
                    && connection.Contains("if (!cb.Incremental)", StringComparison.Ordinal)
                    && connection.Contains(
                        "Subscribe<SteamFriends.PersonaStateCallback>",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "EClientPersonaStateFlag.Presence",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "EClientPersonaStateFlag.GameDataBlob",
                        StringComparison.Ordinal
                    )
                    && connection.Contains("Player.GetNicknameList", StringComparison.Ordinal)
                    && connection.Contains(
                        "Player.GetFriendsGameplayInfo",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "SetPersonaState(EPersonaState.Online)",
                        StringComparison.Ordinal
                    )
                    && bridge.Contains("SetInvitePresenceOnline();", StringComparison.Ordinal)
                    && connection.Contains(
                        "GetAuthenticatedPersonaName(CancellationToken cancellationToken)",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "_steamFriends.GetPersonaName()",
                        StringComparison.Ordinal
                    )
                    && bridge.Contains(
                        "GetAuthenticatedPersonaNameAsync()",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "SteamInviteFriendListPolicy.IdentityKey",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "_friendsReadyGate.Wait(TimeSpan.FromSeconds(5), cancellationToken)",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "SteamInviteFriendListPolicy.MaxSearchableFriends",
                        StringComparison.Ordinal
                    )
                    && connection.Contains(
                        "for (int index = 0; index < count; index++)",
                        StringComparison.Ordinal
                    )
                    && !connection.Contains(
                        "friendIds.Count < maxFriends",
                        StringComparison.Ordinal
                    )
                    && connection.Contains("UnicodeCategory.Format", StringComparison.Ordinal)
                    && !bridge.Contains("Task.Delay(500", StringComparison.Ordinal)
                    && connection.IndexOf("_connectedGate.Set();", StringComparison.Ordinal)
                        < connection.IndexOf("lock (_stateLock)", StringComparison.Ordinal),
                "friend readiness or connection disposal regressed to guessed delays or a connect-timeout race"
            );
            Assert(
                coordinator.Contains("SteamInviteListenerStatus", StringComparison.Ordinal)
                    && coordinator.Contains(
                        "ShowListenerStatus(generation, bridge, personaName)",
                        StringComparison.Ordinal
                    )
                    && coordinator.Contains("listenerStatus.QueueFree();", StringComparison.Ordinal)
                    && dialogs.Contains(
                        "Steam invites active as {personaName}",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "TextProvenance.LauncherTemplateWithExternalContent",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "MouseFilter = MouseFilterEnum.Ignore",
                        StringComparison.Ordinal
                    )
                    && !coordinator.Contains("SavedAccountName}", StringComparison.Ordinal)
                    && !bridge.Contains("AuthenticatedSteamId.ToString", StringComparison.Ordinal),
                "join-listener identity UI can expose the wrong identity, survive teardown, or block input"
            );
            Assert(
                dialogs.Contains("tree.ProcessFrame += Drain;", StringComparison.Ordinal)
                    && dialogs.Contains(
                        "GetTree().ProcessFrame -= Drain;",
                        StringComparison.Ordinal
                    )
                    && !dialogs.Contains("public override void _Process", StringComparison.Ordinal),
                "Steam invite async results rely on an embedded Node virtual that is not device-reliable"
            );
            Assert(
                dialogs.Contains("Search Steam name or nickname", StringComparison.Ordinal)
                    && dialogs.Contains("Show offline friends", StringComparison.Ordinal)
                    && dialogs.Contains("ButtonPressed = false", StringComparison.Ordinal)
                    && dialogs.Contains(
                        "SteamInviteFriendListPolicy.Matches",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "SteamInviteFriendListPolicy.IsVisible",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "_showOffline.ButtonPressed,\n                    _search.Text",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "SteamInviteFriendListPolicy.Rank",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "SteamInviteFriendListPolicy.RenderWindow(matching)",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains(
                        "Showing the first {visible.Count} matches",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains("friend.Nickname", StringComparison.Ordinal)
                    && dialogs.Contains("friend.PersonaName", StringComparison.Ordinal)
                    && dialogs.Contains(
                        "RECENTLY PLAYED SLAY THE SPIRE 2",
                        StringComparison.Ordinal
                    )
                    && !dialogs.Contains("RECENTLY PLAYED TOGETHER", StringComparison.Ordinal),
                "friend picker lost search, offline-default, nickname display, rank, or truthful recent-play copy"
            );
            Assert(
                dialogs.Contains("This is not Steam Relay", StringComparison.Ordinal)
                    && dialogs.Contains("这不是 Steam Relay", StringComparison.Ordinal)
                    && dialogs.Contains("Steam Relay가 아니며", StringComparison.Ordinal)
                    && dialogs.Contains(
                        "TextProvenance.LauncherTemplateWithExternalContent",
                        StringComparison.Ordinal
                    )
                    && dialogs.Contains("Math.Min(", StringComparison.Ordinal)
                    && dialogs.Contains(
                        "TouchScroll.Attach(endpointScroll);",
                        StringComparison.Ordinal
                    )
                    && !coordinator.Contains("ConfigFile", StringComparison.Ordinal)
                    && !coordinator.Contains("File.Write", StringComparison.Ordinal)
                    && !coordinator.Contains("{friendSteamId}", StringComparison.Ordinal)
                    && !coordinator.Contains("{endpoint}", StringComparison.Ordinal),
                "invite UI mislabeled relay behavior, lost trilingual/external-content handling, or persisted/logged private values"
            );
        }
    );

    Run(
        "completed Workshop installs clear stale update badges",
        () =>
        {
            Assert(
                WorkshopUpdateStatus.ShouldShowUpdateAvailable(
                    plannedAsUpdate: true,
                    installedTimeUpdated: 100,
                    downloadCompleted: false,
                    downloadedTimeUpdated: 200
                ),
                "an unfinished planned update disappeared early"
            );
            Assert(
                !WorkshopUpdateStatus.ShouldShowUpdateAvailable(
                    plannedAsUpdate: true,
                    installedTimeUpdated: 200,
                    downloadCompleted: true,
                    downloadedTimeUpdated: 200
                ),
                "a completed, persisted update kept the stale Update available badge"
            );
            Assert(
                WorkshopUpdateStatus.ShouldShowUpdateAvailable(
                    plannedAsUpdate: true,
                    installedTimeUpdated: 100,
                    downloadCompleted: true,
                    downloadedTimeUpdated: 200
                ),
                "a queue completion without a persisted registry revision hid a real update"
            );

            var repository = FindRepositoryRoot();
            var manager = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Sections",
                    "ModManagerSection.cs"
                )
            );
            var subscribed = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Sections",
                    "WorkshopSubscribedPane.cs"
                )
            );
            var snapshotPublish = subscribed.IndexOf(
                "_updateAvailablePfids = new HashSet<ulong>",
                StringComparison.Ordinal
            );
            var firstEnqueue = subscribed.IndexOf("_queue.Enqueue(item)", StringComparison.Ordinal);
            Assert(
                manager.Contains(
                    "_subscribedPane.NotifyQueueChanged(queueIdle: !busy)",
                    StringComparison.Ordinal
                )
                    && subscribed.Contains(
                        "private void ReconcileCompletedUpdates()",
                        StringComparison.Ordinal
                    )
                    && subscribed.Contains(
                        "persisted.TimeUpdated < queueEntry.Item.TimeUpdated",
                        StringComparison.Ordinal
                    )
                    && snapshotPublish >= 0
                    && firstEnqueue >= 0
                    && snapshotPublish < firstEnqueue,
                "hidden SUBSCRIBED state was not reconciled when DOWNLOADS became idle"
            );
        }
    );

    Run(
        "launcher self-update uses this fork and only its exact signed APK name",
        () =>
        {
            Assert(
                LauncherReleaseChannel.LatestReleaseApiUrl.Contains(
                    "xingfanxia/StS2-Launcher_Mod_Manager",
                    StringComparison.Ordinal
                ),
                "automatic update checks still target upstream instead of this fork"
            );
            var v047Asset = LauncherReleaseChannel.GetExpectedApkAssetName("0.4.7");
            var v048Asset = LauncherReleaseChannel.GetExpectedApkAssetName("0.4.8");
            Assert(
                v047Asset == "StS2Launcher-v0.4.7.apk"
                    && v048Asset == "StS2Launcher-v0.4.8.apk"
                    && !string.Equals(v047Asset, v048Asset, StringComparison.Ordinal),
                "different launcher versions reused the same installer cache URI"
            );
            Assert(
                LauncherReleaseChannel.GetExpectedApkAssetName("") == null
                    && LauncherReleaseChannel.GetExpectedApkAssetName("v0.4.8") == null
                    && LauncherReleaseChannel.GetExpectedApkAssetName("0.4.8-debug") == null
                    && LauncherReleaseChannel.GetExpectedApkAssetName("０.４.８") == null
                    && LauncherReleaseChannel.GetExpectedApkAssetName("0.4.8/other") == null,
                "an untrusted version crossed the installer filename boundary"
            );
            Assert(
                LauncherReleaseChannel.IsExpectedApkAsset("StS2Launcher-v0.4.6.apk", "0.4.6"),
                "the canonical release APK was rejected"
            );
            Assert(
                !LauncherReleaseChannel.IsExpectedApkAsset("StS2Launcher-v0.4.6-debug.apk", "0.4.6")
                    && !LauncherReleaseChannel.IsExpectedApkAsset("another-launcher.apk", "0.4.6")
                    && !LauncherReleaseChannel.IsExpectedApkAsset(
                        "StS2Launcher-v0.4.6.apk.sha256",
                        "0.4.6"
                    ),
                "an unrelated/debug/checksum asset crossed the APK install boundary"
            );
            Assert(
                LauncherReleaseChannel.IsExpectedDownloadUrl(
                    "https://github.com/xingfanxia/StS2-Launcher_Mod_Manager/releases/download/v0.4.6/StS2Launcher-v0.4.6.apk",
                    "0.4.6"
                )
                    && !LauncherReleaseChannel.IsExpectedDownloadUrl(
                        "https://github.com/xingfanxia/StS2-Launcher_Mod_Manager/releases/download/v0.4.6/StS2Launcher-v0.4.6.apk",
                        "0.4.7"
                    )
                    && !LauncherReleaseChannel.IsExpectedDownloadUrl(
                        "https://github.com/xingfanxia/StS2-Launcher_Mod_Manager/releases/download/v0.4.6/StS2Launcher-v0.4.6.apk.sha256",
                        "0.4.6"
                    )
                    && !LauncherReleaseChannel.IsExpectedDownloadUrl(
                        "https://example.com/StS2Launcher-v0.4.6.apk"
                    )
                    && !LauncherReleaseChannel.IsExpectedDownloadUrl(
                        "http://github.com/xingfanxia/StS2-Launcher_Mod_Manager/releases/download/v0.4.6/StS2Launcher-v0.4.6.apk"
                    ),
                "a non-HTTPS or non-fork download URL crossed the update boundary"
            );

            var repository = FindRepositoryRoot();
            var installer = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "AppUpdateInstaller.cs")
            );
            var controller = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherController.cs")
            );
            Assert(
                installer.Contains("string expectedVersion,", StringComparison.Ordinal)
                    && installer.Contains(
                        "LauncherReleaseChannel.GetExpectedApkAssetName(expectedVersion)",
                        StringComparison.Ordinal
                    )
                    && !installer.Contains(
                        "private const string ApkFileName = \"launcher_update.apk\"",
                        StringComparison.Ordinal
                    )
                    && controller.Contains(
                        "result.DownloadUrl,\n                    result.LatestVersion,",
                        StringComparison.Ordinal
                    ),
                "the updater still exposes every release through one stale FileProvider URI"
            );
        }
    );

    Run(
        "Mono-invalid mod IL is attributed, isolated, and safely auto-disabled",
        () =>
        {
            var repository = FindRepositoryRoot();
            var loaderPatch = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ModLoaderPatches.cs")
            );
            var compatibility = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "ModRuntimeCompatibility.cs"
                )
            );
            var android = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            var autoDisabler = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Modding", "ModAutoDisabler.cs")
            );
            Assert(
                loaderPatch.Contains(
                    "nameof(CallModInitializerTranspiler)",
                    StringComparison.Ordinal
                )
                    && loaderPatch.Contains("nameof(MethodBase.Invoke)", StringComparison.Ordinal)
                    && loaderPatch.Contains(
                        "ModRuntimeCompatibility.IsIncompatible(mod.assembly)",
                        StringComparison.Ordinal
                    )
                    && compatibility.Contains("InvalidProgramException", StringComparison.Ordinal)
                    && compatibility.Contains("return method.Invoke", StringComparison.Ordinal)
                    && compatibility.Contains("throw;", StringComparison.Ordinal)
                    && compatibility.Contains(
                        "ModAssemblyRegistry.IsModAssembly",
                        StringComparison.Ordinal
                    )
                    && loaderPatch.Contains("TryDisableForFutureLaunch", StringComparison.Ordinal)
                    && autoDisabler.Contains("ModStasher.Disable(info)", StringComparison.Ordinal)
                    && autoDisabler.Contains(
                        "The owning enabled mod folder could not be resolved uniquely.",
                        StringComparison.Ordinal
                    )
                    && !compatibility.Contains("LoadFromStream", StringComparison.Ordinal)
                    && android.Contains(
                        "showModRuntimeCompatibilityNotice",
                        StringComparison.Ordinal
                    )
                    && !android.Contains("sts2_mod_compat", StringComparison.Ordinal),
                "Mono-invalid mod handling lost attribution, fail-closed ownership, or byte preservation"
            );

            var candidates = new[]
            {
                new ModAutoDisableCandidate("broken.mod", "/mods/broken", true),
                new ModAutoDisableCandidate("healthy.mod", "/mods/healthy", true),
            };
            Assert(
                ModAutoDisablePolicy.SelectTopLevelDirectory("broken.mod", "", "/mods", candidates)
                    == "/mods/broken",
                "a unique exact manifest id should resolve when an assembly was byte-loaded"
            );
            Assert(
                ModAutoDisablePolicy.SelectTopLevelDirectory(
                    "broken.mod",
                    "/mods/broken/bin/broken.dll",
                    "/mods",
                    candidates
                ) == "/mods/broken",
                "an assembly inside the unique owning folder should resolve"
            );
            Assert(
                ModAutoDisablePolicy.SelectTopLevelDirectory(
                    "broken.mod",
                    "/mods/healthy/broken.dll",
                    "/mods",
                    candidates
                ) == null,
                "a known assembly/manifest folder mismatch must fail closed"
            );
            Assert(
                ModAutoDisablePolicy.SelectTopLevelDirectory(
                    "broken.mod",
                    "",
                    "/mods",
                    candidates.Append(
                        new ModAutoDisableCandidate("broken.mod", "/mods/duplicate", true)
                    )
                ) == null,
                "duplicate ids in different folders must never pick a victim"
            );
            Assert(
                ModAutoDisablePolicy.SelectTopLevelDirectory(
                    "broken.mod",
                    "",
                    "/mods",
                    new[] { new ModAutoDisableCandidate("broken.mod", "/mods/../outside", true) }
                ) == null,
                "a traversal candidate must never cross the enabled-mod root"
            );
            Assert(
                ModAutoDisablePolicy.SelectTopLevelDirectory(
                    "broken.mod",
                    "",
                    "/mods",
                    new[] { new ModAutoDisableCandidate("broken.mod", "/mods/bundle", false) }
                ) == null,
                "a shared top-level folder must remain a manual user decision"
            );
        }
    );

    Run(
        "Steam cloud enumeration requests content hashes",
        () =>
        {
            var repository = FindRepositoryRoot();
            var cache = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "CloudFileCache.cs")
            );

            Assert(
                cache.Contains("extended_details = true", StringComparison.Ordinal),
                "EnumerateUserFiles must request extended metadata or file_sha is omitted"
            );
        }
    );

    Run(
        "Steam manifest hashes skip unchanged whole-file cloud transfers",
        () =>
        {
            Assert(
                CloudContentHash.Matches("A9993E364706816ABA3E25717850C26C9CD0D89D", "abc"),
                "SHA-1 matching must be case-insensitive"
            );
            Assert(
                CloudContentHash.Matches("sha1:a9993e364706816aba3e25717850c26c9cd0d89d", "abc"),
                "Steam SHA-1 prefixes must be accepted"
            );
            Assert(
                CloudContentHash.Matches("qZk+NkcGgWq6PiVxeFDCbJzQ2J0=", "abc"),
                "20-byte Base64 manifest hashes must be accepted"
            );
            Assert(
                CloudContentHash.Matches("da39a3ee5e6b4b0d3255bfef95601890afd80709", ""),
                "empty saves must use the canonical raw-content SHA-1"
            );
            Assert(
                !CloudContentHash.Matches("a9993e364706816aba3e25717850c26c9cd0d89d", "changed"),
                "changed content must not be skipped"
            );
            Assert(
                !CloudContentHash.Matches("not-a-steam-sha", "abc"),
                "unknown manifest hash formats must fail open to transfer"
            );
        }
    );

    Run(
        "manual cloud push does not resurrect history removed by the cloud cap",
        () =>
        {
            var repository = FindRepositoryRoot();
            var coordinator = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "CloudSyncCoordinator.cs")
            );
            var pushStart = coordinator.IndexOf(
                "public static async Task<CloudBatchOutcome> ManualPushAllAsync(",
                StringComparison.Ordinal
            );
            var pullStart = coordinator.IndexOf(
                "public static async Task<CloudBatchOutcome> ManualPullAllAsync(",
                StringComparison.Ordinal
            );
            Assert(
                pushStart >= 0 && pullStart > pushStart,
                "manual cloud push implementation could not be isolated"
            );

            var manualPush = coordinator[pushStart..pullStart];
            var historyGuard = manualPush.IndexOf(
                "if (IsHistoryRunFile(path))",
                StringComparison.Ordinal
            );
            var contentRead = manualPush.IndexOf(
                "string content = localStore.ReadFile(path);",
                StringComparison.Ordinal
            );
            Assert(
                historyGuard >= 0
                    && historyGuard < contentRead
                    && manualPush.Contains(
                        "Push: skipping local-only history run",
                        StringComparison.Ordinal
                    )
                    && manualPush.Contains("manifest SHA-1 match", StringComparison.Ordinal),
                "manual push can re-upload capped local-only history or lost mutable-file delta checks"
            );
        }
    );

    Run(
        "bounded cloud downloads overlap without exceeding their connection budget",
        () =>
        {
            const int concurrency = 4;
            var sync = new object();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var saturated = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int active = 0;
            int maximum = 0;
            int completed = 0;

            var run = BoundedAsyncWork.ForEachAsync(
                Enumerable.Range(0, 11).ToArray(),
                concurrency,
                async _ =>
                {
                    lock (sync)
                    {
                        active++;
                        maximum = Math.Max(maximum, active);
                        if (active == concurrency)
                            saturated.TrySetResult(true);
                    }

                    await release.Task.ConfigureAwait(false);

                    lock (sync)
                    {
                        active--;
                        completed++;
                    }
                }
            );

            Assert(
                saturated.Task.Wait(TimeSpan.FromSeconds(2)),
                "the bounded worker never overlapped independent downloads"
            );
            lock (sync)
                Assert(maximum == concurrency, "the worker exceeded its concurrency budget");

            release.TrySetResult(true);
            Assert(run.Wait(TimeSpan.FromSeconds(2)), "the bounded worker did not drain");
            lock (sync)
            {
                Assert(active == 0, "an async worker remained active after drain");
                Assert(completed == 11, "the bounded worker lost an item");
            }
        }
    );

    Run(
        "Android login autofill uses the OS credential boundary without crossing values",
        () =>
        {
            var repository = FindRepositoryRoot();
            var android = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            var bridge = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "AndroidLoginAutofillBridge.cs"
                )
            );
            var login = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Sections",
                    "LoginSection.cs"
                )
            );

            Assert(
                android.Contains(
                    "configureLoginAutofill(String fieldType, String normalizedAnchor)",
                    StringComparison.Ordinal
                )
                    && android.Contains("GodotEditText", StringComparison.Ordinal)
                    && android.Contains("View.AUTOFILL_HINT_USERNAME", StringComparison.Ordinal)
                    && android.Contains("View.AUTOFILL_HINT_PASSWORD", StringComparison.Ordinal)
                    && android.Contains("AutofillManager", StringComparison.Ordinal)
                    && android.Contains("setAutofillHints(hint)", StringComparison.Ordinal)
                    && android.Contains("View.IMPORTANT_FOR_AUTOFILL_YES", StringComparison.Ordinal)
                    && android.Contains("positionLoginAutofillAnchor", StringComparison.Ordinal)
                    && android.Contains("editText.setX(left)", StringComparison.Ordinal)
                    && android.Contains("editText.setY(top)", StringComparison.Ordinal)
                    && android.Contains(
                        "onWindowFocusChanged(boolean hasFocus)",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "restoreGodotEditTextBounds(editText)",
                        StringComparison.Ordinal
                    )
                    && android.Contains("editText.clearFocus()", StringComparison.Ordinal)
                    && android.Contains("requestAutofill(editText)", StringComparison.Ordinal),
                "the Android bridge must annotate Godot's existing native editor"
            );
            Assert(
                android.Contains("clearLoginAutofill()", StringComparison.Ordinal)
                    && android.Contains("autofillManager.cancel()", StringComparison.Ordinal)
                    && android.Contains(
                        "setAutofillHints((String[]) null)",
                        StringComparison.Ordinal
                    ),
                "field changes and login exit must terminate stale autofill sessions"
            );
            Assert(
                bridge.Contains(
                    "Configure(LoginAutofillField field, Control anchor)",
                    StringComparison.Ordinal
                )
                    && bridge.Contains("anchor.GetGlobalRect()", StringComparison.Ordinal)
                    && bridge.Contains("anchor.GetViewportRect()", StringComparison.Ordinal)
                    && bridge.Contains(
                        ".Call(\"configureLoginAutofill\", fieldType, normalizedAnchor)",
                        StringComparison.Ordinal
                    )
                    && bridge.Contains(".Call(\"clearLoginAutofill\")", StringComparison.Ordinal)
                    && !bridge.Contains(".Text", StringComparison.Ordinal),
                "the managed bridge must pass only a fixed field-type token, never a credential"
            );
            Assert(
                login.Contains(
                    "ConfigureAutofill(UsernameField, LoginAutofillField.Username)",
                    StringComparison.Ordinal
                )
                    && login.Contains(
                        "ConfigureAutofill(_passwordField, LoginAutofillField.Password)",
                        StringComparison.Ordinal
                    )
                    && login.Contains("field.FocusEntered", StringComparison.Ordinal)
                    && login.Contains("field.GuiInput", StringComparison.Ordinal)
                    && login.Contains("InputEventScreenTouch", StringComparison.Ordinal)
                    && login.Contains("VisibilityChanged", StringComparison.Ordinal)
                    && !login.Contains(
                        "FocusExited += AndroidLoginAutofillBridge.Clear",
                        StringComparison.Ordinal
                    ),
                "both login fields must request on focus and retry taps without cancelling provider UI"
            );
        }
    );

    Run(
        "Quick Restart compatibility is exact and idle processing is impossible",
        () =>
        {
            Assert(
                QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "the measured Quick Restart v2.0.0 assembly must match"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestartUpdated",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "a same-shape assembly with another name must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 1),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "an updated assembly version must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.NewGuid(),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "an updated MVID must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
                    isExternalModAssembly: true
                ),
                "a different assembly hash must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: false
                ),
                "an assembly outside the mod tree must never be patched"
            );

            Assert(
                QuickRestartPerformancePolicy.AfterInput(isHolding: false, triggered: false)
                    == QuickRestartProcessAction.ResetAndDisable,
                "idle input must reset once and disable processing"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterInput(isHolding: true, triggered: false)
                    == QuickRestartProcessAction.Enable,
                "a real hold must enable processing"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterProcess(isHolding: true, triggered: false)
                    == QuickRestartProcessAction.KeepEnabled,
                "an incomplete hold must continue"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterProcess(isHolding: true, triggered: true)
                    == QuickRestartProcessAction.Disable,
                "a triggered restart must stop processing immediately"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterProcess(isHolding: false, triggered: false)
                    == QuickRestartProcessAction.Disable,
                "release must make idle processing impossible"
            );
        }
    );

    Run(
        "covered startup skips only the hidden logo without leaking session state",
        () =>
        {
            Assert(
                !CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "normal game startup must preserve the logo"
            );
            Assert(
                CoveredStartupLogoPolicy.ShouldSkipLogo(true),
                "the game's existing skip request must remain authoritative"
            );

            using (CoveredStartupLogoPolicy.Enter(launcherSurfaceCoversStartup: false))
                Assert(
                    !CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                    "a failed launcher progress surface must not hide the logo"
                );

            var outer = CoveredStartupLogoPolicy.Enter(launcherSurfaceCoversStartup: true);
            Assert(
                CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "a covered startup must skip the hidden logo"
            );
            using (CoveredStartupLogoPolicy.Enter(launcherSurfaceCoversStartup: true))
                Assert(
                    CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                    "nested startup scopes must remain active"
                );
            Assert(
                CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "disposing a nested scope must preserve its outer scope"
            );
            outer.Dispose();
            outer.Dispose();
            Assert(
                !CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "scope disposal must be idempotent and restore normal startup"
            );
        }
    );

    Run(
        "launcher language codes preserve upgrades and classify Chinese scripts",
        () =>
        {
            Assert(
                LauncherLanguageCodes.TryParsePreference("ko", out var korean)
                    && korean == LauncherLanguage.Korean,
                "legacy ko preference must remain readable"
            );
            Assert(
                LauncherLanguageCodes.TryParsePreference("en", out var english)
                    && english == LauncherLanguage.English,
                "legacy en preference must remain readable"
            );
            foreach (var value in new[] { "zh", "zh-Hans", "zh_CN", "zh-SG" })
                Assert(
                    LauncherLanguageCodes.TryParsePreference(value, out var chinese)
                        && chinese == LauncherLanguage.SimplifiedChinese,
                    $"simplified Chinese preference not recognized: {value}"
                );
            Assert(
                !LauncherLanguageCodes.TryParsePreference("broken", out _),
                "unknown persisted language must be rejected"
            );
            Assert(
                LauncherLanguageCodes.ToPreferenceValue(LauncherLanguage.SimplifiedChinese)
                    == "zh-Hans",
                "simplified Chinese must use the canonical persisted code"
            );

            foreach (var locale in new[] { "zh-Hans", "zh_CN", "zh-SG", "zh" })
                Assert(
                    LauncherLanguageCodes.FromSystemLocale(locale)
                        == LauncherLanguage.SimplifiedChinese,
                    $"simplified Chinese locale not selected: {locale}"
                );
            foreach (var locale in new[] { "zh-Hant", "zh_TW", "zh-HK", "zh_MO" })
                Assert(
                    LauncherLanguageCodes.FromSystemLocale(locale) == LauncherLanguage.English,
                    $"traditional Chinese locale must not silently select Simplified: {locale}"
                );
            Assert(
                LauncherLanguageCodes.FromSystemLocale("ko-KR") == LauncherLanguage.Korean,
                "Korean system locale must select Korean"
            );
            Assert(
                LauncherLanguageCodes.FromSystemLocale("fr-FR") == LauncherLanguage.English,
                "other locales must retain the existing English default"
            );
        }
    );

    Run(
        "language selector is prominent, three-valued, and touch-sized",
        () =>
        {
            var repository = FindRepositoryRoot();
            var components = Path.Combine(
                repository,
                "src",
                "STS2Mobile",
                "Launcher",
                "Components"
            );
            var selector = File.ReadAllText(Path.Combine(components, "LanguageToggle.cs"));
            var view = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherView.cs")
            );
            var registry = File.ReadAllText(Path.Combine(components, "LocalizedTextRegistry.cs"));

            Assert(
                selector.Contains("class LanguageSelector", StringComparison.Ordinal)
                    && selector.Contains("new OptionButton()", StringComparison.Ordinal)
                    && selector.Contains("\"한국어\"", StringComparison.Ordinal)
                    && selector.Contains("\"English\"", StringComparison.Ordinal)
                    && selector.Contains("\"简体中文\"", StringComparison.Ordinal)
                    && selector.Contains("\"LANG\"", StringComparison.Ordinal)
                    && selector.Contains("Ui.TouchHeight", StringComparison.Ordinal)
                    && selector.Contains("_lastReportedAudit = null", StringComparison.Ordinal)
                    && !selector.Contains("EN · ON", StringComparison.Ordinal)
                    && !selector.Contains("EN · OFF", StringComparison.Ordinal),
                "legacy binary toggle or undersized selector remains"
            );
            int selectorMount = view.IndexOf("new LanguageSelector", StringComparison.Ordinal);
            int loginMount = view.IndexOf("Login = new LoginSection", StringComparison.Ordinal);
            int consoleMount = view.IndexOf("var logHeader", StringComparison.Ordinal);
            Assert(
                selectorMount >= 0
                    && selectorMount < loginMount
                    && selectorMount < consoleMount
                    && view.Split("new LanguageSelector", StringSplitOptions.None).Length == 2,
                "language selector must have one mount beside the primary title before login"
            );
            Assert(
                registry.Contains("WatchedProperty.OptionItem", StringComparison.Ordinal)
                    && registry.Contains("SetItemText", StringComparison.Ordinal),
                "localized dropdown items must update immediately with the selected language"
            );
        }
    );

    Run(
        "startup performance catalog is closed, localized, and watchdog-owned",
        () =>
        {
            var definitions = StartupStageCatalog.All;
            Assert(
                definitions.Select(item => item.Id).Distinct().Count() == definitions.Count,
                "stage ids must be unique"
            );
            Assert(definitions.Count == 16, "the closed startup catalog must cover all owners");

            foreach (var definition in definitions)
            {
                Assert(
                    !string.IsNullOrWhiteSpace(definition.TitleKo)
                        && !string.IsNullOrWhiteSpace(definition.TitleEn)
                        && !string.IsNullOrWhiteSpace(definition.TitleZh)
                        && !string.IsNullOrWhiteSpace(definition.WatchdogKo)
                        && !string.IsNullOrWhiteSpace(definition.WatchdogEn)
                        && !string.IsNullOrWhiteSpace(definition.WatchdogZh),
                    $"localized stage copy missing for {definition.Id}"
                );
                Assert(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        definition.TitleKo,
                        "[\\uAC00-\\uD7AF]"
                    ),
                    $"Korean title missing for {definition.Id}"
                );
                Assert(
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        definition.TitleEn,
                        "[\\uAC00-\\uD7AF]"
                    ),
                    $"English title contains Hangul for {definition.Id}"
                );
                Assert(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        definition.TitleZh,
                        "[\\u3400-\\u9FFF]"
                    )
                        && !System.Text.RegularExpressions.Regex.IsMatch(
                            definition.TitleZh,
                            "[\\uAC00-\\uD7AF]"
                        ),
                    $"Simplified Chinese title missing or contains Hangul for {definition.Id}"
                );

                if (definition.WatchdogPolicy == StartupWatchdogPolicy.NoneForUserWait)
                    Assert(
                        definition.WatchdogAfterUsec == 0,
                        $"user-owned wait must not time out: {definition.Id}"
                    );
                else
                    Assert(
                        definition.WatchdogAfterUsec > 0,
                        $"owned work must define a watchdog: {definition.Id}"
                    );

                foreach (var next in definition.AllowedNext)
                    _ = StartupStageCatalog.Get(next);
            }
        }
    );

    Run(
        "startup performance timeline closes normal and optional paths",
        () =>
        {
            var timeline = new StartupPerformanceTimeline();
            long now = 1_000;

            Assert(
                timeline.TryBegin(StartupStageId.LauncherCreation, now++, out var error),
                $"launcher begin: {error}"
            );
            Assert(
                timeline.TryEnd(
                    StartupStageId.LauncherCreation,
                    StartupStageTerminal.Completed,
                    now++,
                    out error
                ),
                $"launcher end: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.LauncherReady, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.LauncherReady,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"launcher ready: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.UserWait, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.UserWait,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"user wait: {error}"
            );
            Assert(
                timeline.TrySkip(StartupStageId.CloudSync, now++, out error),
                $"cloud skip: {error}"
            );
            Assert(
                timeline.TrySkip(StartupStageId.ShaderWarmup, now++, out error),
                $"warmup skip: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.GameSettings, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.GameSettings,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"settings: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.GameStartup, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.GameStartup,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"game startup: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.GameReady, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.GameReady,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"game ready: {error}"
            );

            Assert(timeline.ActiveStage == null, "normal timeline must close");
            Assert(
                timeline.LastTerminalStage == StartupStageId.GameReady,
                "game-ready must be terminal"
            );
            Assert(
                !timeline.TryEnd(
                    StartupStageId.GameReady,
                    StartupStageTerminal.Completed,
                    now++,
                    out error
                )
                    && error == StartupTimelineError.NoActiveStage,
                "duplicate terminal callbacks must be ignored explicitly"
            );
            Assert(
                !timeline.TryBegin(StartupStageId.AndroidProcess, now++, out error)
                    && error == StartupTimelineError.IllegalTransition,
                "a closed timeline must reject backward stage movement"
            );
        }
    );

    Run(
        "startup timeline handles degraded retry, teardown, and bounded watchdogs",
        () =>
        {
            var timeline = new StartupPerformanceTimeline(capacity: 16);
            Assert(
                timeline.TryBegin(StartupStageId.AndroidProcess, 0, out var error)
                    && timeline.TryEnd(
                        StartupStageId.AndroidProcess,
                        StartupStageTerminal.Completed,
                        10,
                        out error
                    ),
                $"android process: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.CacheSync, 20, out error),
                $"cache begin: {error}"
            );
            Assert(
                timeline.CheckWatchdog(59_000_020) == StartupWatchdogPolicy.NoneForUserWait,
                "watchdog must not fire early"
            );
            Assert(
                timeline.CheckWatchdog(60_000_020) == StartupWatchdogPolicy.DegradeAndContinue,
                "cache watchdog policy"
            );
            Assert(
                timeline.CheckWatchdog(61_000_020) == StartupWatchdogPolicy.DegradeAndContinue,
                "watchdog policy remains visible after first marker"
            );
            Assert(
                timeline.Snapshot().Count(item => item.Kind == StartupTimelineEventKind.Watchdog)
                    == 1,
                "one stage may emit only one watchdog marker"
            );
            Assert(
                timeline.TryEnd(
                    StartupStageId.CacheSync,
                    StartupStageTerminal.Degraded,
                    61_000_030,
                    out error
                )
                    && timeline.TryBegin(StartupStageId.CacheSync, 61_000_040, out error)
                    && timeline.TryEnd(
                        StartupStageId.CacheSync,
                        StartupStageTerminal.Recovery,
                        61_000_050,
                        out error
                    ),
                $"retry/recovery teardown: {error}"
            );
            Assert(timeline.ActiveStage == null, "recovery teardown must close active work");
        }
    );

    Run(
        "startup progress is truthful, sparse, bounded, and numeric-only",
        () =>
        {
            var timeline = new StartupPerformanceTimeline(capacity: 8);
            Assert(
                timeline.TryBegin(StartupStageId.AndroidProcess, 0, out var error)
                    && timeline.TryEnd(
                        StartupStageId.AndroidProcess,
                        StartupStageTerminal.Completed,
                        1,
                        out error
                    )
                    && timeline.TryBegin(StartupStageId.CacheSync, 2, out error),
                $"progress setup: {error}"
            );

            for (int done = 0; done <= 100; done++)
            {
                Assert(
                    timeline.TryReportProgress(
                        StartupStageId.CacheSync,
                        done,
                        100,
                        2L + done * 50_000L,
                        out error
                    ),
                    $"progress {done}: {error}"
                );
            }

            Assert(
                timeline.CurrentProgress == new StartupProgress(100, 100),
                "UI progress must keep the exact latest units"
            );
            Assert(timeline.EventCount <= 8, "event ring must remain bounded");
            Assert(
                timeline.Snapshot().Count(item => item.Kind == StartupTimelineEventKind.Progress)
                    < 100,
                "telemetry must not record one event per work item"
            );
            Assert(
                !timeline.TryReportProgress(StartupStageId.CacheSync, 99, 100, 6_000_000, out error)
                    && error == StartupTimelineError.InvalidProgress,
                "progress may not move backward"
            );
            Assert(
                !timeline.TryReportProgress(
                    StartupStageId.CacheSync,
                    100,
                    101,
                    6_000_001,
                    out error
                )
                    && error == StartupTimelineError.InvalidProgress,
                "a displayed determinate total may not change"
            );

            string encoded = timeline.EncodeSanitized();
            Assert(encoded.Length < 1_024, "bounded summary size");
            Assert(encoded.StartsWith("v1\n", StringComparison.Ordinal), "schema version");
            Assert(
                encoded.All(character =>
                    character == 'v'
                    || character == '|'
                    || character == '\n'
                    || char.IsAsciiDigit(character)
                ),
                "sanitized timeline must contain only schema punctuation and numeric fields"
            );
            Assert(
                timeline.EncodeTerminalDurations() == "v2;1|1;",
                "compact stage totals must survive bounded event-ring eviction"
            );
        }
    );

    Run(
        "startup observability stays separate, truthful, and privacy-bounded",
        () =>
        {
            var repository = FindRepositoryRoot();
            string launcherRoot = Path.Combine(repository, "src", "STS2Mobile", "Launcher");
            string patchesRoot = Path.Combine(repository, "src", "STS2Mobile", "Patches");
            var tracker = File.ReadAllText(
                Path.Combine(launcherRoot, "StartupPerformanceTracker.cs")
            );
            var timeline = File.ReadAllText(
                Path.Combine(launcherRoot, "StartupPerformanceTimeline.cs")
            );
            var overlay = File.ReadAllText(Path.Combine(launcherRoot, "StartupProgressOverlay.cs"));
            var recoveryBridge = File.ReadAllText(
                Path.Combine(launcherRoot, "StartupRecoveryBridge.cs")
            );
            var launcherPatches = File.ReadAllText(Path.Combine(patchesRoot, "LauncherPatches.cs"));
            var coveredLogoPatches = File.ReadAllText(
                Path.Combine(patchesRoot, "CoveredStartupLogoPatches.cs")
            );
            var modPatches = File.ReadAllText(Path.Combine(patchesRoot, "ModLoaderPatches.cs"));
            var cloudOverlay = File.ReadAllText(Path.Combine(launcherRoot, "CloudSyncOverlay.cs"));
            var warmup = File.ReadAllText(Path.Combine(launcherRoot, "ShaderWarmupScreen.cs"));
            string androidRoot = Path.Combine(
                repository,
                "android",
                "src",
                "com",
                "game",
                "sts2launcher",
                "modmanager"
            );
            var androidApp = File.ReadAllText(Path.Combine(androidRoot, "GodotApp.java"));
            var nativeTimeline = File.ReadAllText(
                Path.Combine(androidRoot, "StartupPerformanceTimeline.java")
            );

            Assert(
                recoveryBridge.Contains(
                    "StartupPerformanceTracker.BeginManagedStartup();",
                    StringComparison.Ordinal
                )
                    && recoveryBridge.Contains(
                        "Call(\"recordStartupStage\", stage)",
                        StringComparison.Ordinal
                    ),
                "performance startup must begin beside, not replace, crash recovery journaling"
            );
            Assert(
                timeline.Contains("StartupTimelineEvent[] _events", StringComparison.Ordinal)
                    && timeline.Contains("capacity is < 8 or > 256", StringComparison.Ordinal)
                    && timeline.Contains("EncodeSanitized", StringComparison.Ordinal)
                    && !timeline.Contains("System.IO", StringComparison.Ordinal),
                "performance telemetry must remain bounded and free of hot-path file writes"
            );
            Assert(
                tracker.Contains("[StartupPerformance/Summary]", StringComparison.Ordinal)
                    && tracker.Contains("EncodeTerminalDurations()", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "[StartupPerformance/NativeSummary]",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "startupPerformanceTimeline.encode().replace('\\n', ';')",
                        StringComparison.Ordinal
                    ),
                "each completed startup must expose one bounded numeric stage summary"
            );
            foreach (
                var forbidden in new[]
                {
                    "SavedAccountName",
                    "SavedRefreshToken",
                    "ModCandidate",
                    "DeviceId",
                    "FilePath",
                }
            )
            {
                Assert(
                    !tracker.Contains(forbidden, StringComparison.Ordinal)
                        && !timeline.Contains(forbidden, StringComparison.Ordinal)
                        && !nativeTimeline.Contains(forbidden, StringComparison.Ordinal),
                    $"startup performance schema contains private field {forbidden}"
                );
            }

            Assert(
                androidApp.Contains("PROCESS_ENTRY_USEC =", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "splashScreen.setKeepOnScreenCondition(() -> !overlayHandoffReady)",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "showStartupOverlay(wipingAtlas || debugAtlasOverlayPreview)",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "completeNativeStartupPerformance()",
                        StringComparison.Ordinal
                    )
                    && recoveryBridge.Contains(
                        "Call(\"completeNativeStartupPerformance\")",
                        StringComparison.Ordinal
                    ),
                "native monotonic stages must hand off without a splash-to-Godot black gap"
            );
            int previousExitStart = androidApp.IndexOf(
                "startPreviousExitReport();",
                StringComparison.Ordinal
            );
            int godotSuperCreate = androidApp.IndexOf(
                "super.onCreate(savedInstanceState);",
                StringComparison.Ordinal
            );
            Assert(
                previousExitStart >= 0
                    && godotSuperCreate > previousExitStart
                    && androidApp.Contains(
                        "previousExitReportGate.markActivityReady()",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "previousExitReportGate.markQueryComplete()",
                        StringComparison.Ordinal
                    ),
                "previous-exit I/O must overlap bootstrap while prompt finalization waits for Activity readiness"
            );
            Assert(
                nativeTimeline.Contains("private final Event[] events", StringComparison.Ordinal)
                    && nativeTimeline.Contains("String encode()", StringComparison.Ordinal)
                    && !nativeTimeline.Contains("String label", StringComparison.Ordinal),
                "native startup schema must also remain bounded and numeric-only"
            );

            Assert(
                overlay.Contains("RefreshIntervalSeconds = 0.25", StringComparison.Ordinal)
                    && overlay.Contains(
                        "(double)snapshot.Progress.Done / snapshot.Progress.Total * 100",
                        StringComparison.Ordinal
                    )
                    && overlay.Contains("_progressBar.Visible = false", StringComparison.Ordinal)
                    && !overlay.Contains("Value +=", StringComparison.Ordinal),
                "visible startup progress must use real units and never time-driven percentages"
            );
            Assert(
                overlay.Contains("BeginMainThreadHandoff", StringComparison.Ordinal)
                    && overlay.Contains("PublishNativeSnapshot", StringComparison.Ordinal)
                    && overlay.Contains("\"showManagedStartupProgress\"", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "public void showManagedStartupProgress(",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "SystemClock.elapsedRealtime() - managedStageAnchorRealtimeMs",
                        StringComparison.Ordinal
                    )
                    && tracker.Contains("TotalElapsedUsec", StringComparison.Ordinal)
                    && tracker.Contains("_postPlaySinceUsec", StringComparison.Ordinal)
                    && overlay.Contains(
                        "Loc.Tr(\"단계 \", \"Stage \", \"阶段 \")",
                        StringComparison.Ordinal
                    )
                    && overlay.Contains(
                        "Loc.Tr(\" · 전체 \", \" · Total \", \" · 总计 \")",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "SystemClock.elapsedRealtime() - managedStartupAnchorRealtimeMs",
                        StringComparison.Ordinal
                    ),
                "PLAY progress must show stage and total elapsed on the Android UI thread while Godot startup blocks"
            );
            Assert(
                launcherPatches.IndexOf(
                    "StartupPerformanceTracker.AdvanceTo(StartupStageId.UserWait)",
                    StringComparison.Ordinal
                )
                    < launcherPatches.IndexOf(
                        "await launcher.WaitForLaunch()",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupPerformanceTracker.AdvanceTo(StartupStageId.CloudSync)",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupStageId.GameSettings",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupPerformanceTracker.AdvanceTo(StartupStageId.GameStartup)",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupPerformanceTracker.MarkGameReady()",
                        StringComparison.Ordinal
                    ),
                "PLAY wait and owned startup work must have distinct real boundaries"
            );
            Assert(
                coveredLogoPatches.Contains("AccessTools.DeclaredMethod(", StringComparison.Ordinal)
                    && coveredLogoPatches.Contains("\"LaunchMainMenu\"", StringComparison.Ordinal)
                    && coveredLogoPatches.Contains(
                        "new[] { typeof(bool) }",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "CoveredStartupLogoPolicy.Enter(startupProgressCoversGameStartup)",
                        StringComparison.Ordinal
                    )
                    && !coveredLogoPatches.Contains("SkipIntroLogo", StringComparison.Ordinal),
                "covered startup may skip only the hidden logo without mutating its saved preference"
            );
            int gameSettingsStage = launcherPatches.IndexOf(
                "StartupPerformanceTracker.AdvanceTo(StartupStageId.GameSettings",
                StringComparison.Ordinal
            );
            int nativeHandoff = launcherPatches.IndexOf(
                "startupProgressOverlay.BeginMainThreadHandoff()",
                StringComparison.Ordinal
            );
            int settingsLoad = launcherPatches.IndexOf(
                "SaveManager.Instance.InitSettingsData()",
                StringComparison.Ordinal
            );
            Assert(
                gameSettingsStage >= 0
                    && gameSettingsStage < nativeHandoff
                    && nativeHandoff < settingsLoad,
                "the first post-PLAY stage must be published before synchronous game settings work"
            );
            Assert(
                androidApp.Contains("debug_startup_stage_delay_seconds", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "consumeDebugStartupStageDelaySeconds",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "debugStartupStageDelaySeconds > 20",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "ApplyDebugStartupStageDelayAsync",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "await Task.Delay(TimeSpan.FromSeconds(seconds))",
                        StringComparison.Ordinal
                    ),
                "watchdog UI proof must use a bounded debug-only startup-stage hold"
            );
            Assert(
                androidApp.Contains("debug_preview_atlas_overlay", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "showStartupOverlay(wipingAtlas || debugAtlasOverlayPreview)",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains("no cache mutation", StringComparison.Ordinal),
                "atlas overlay proof must reuse production copy without deleting cache"
            );
            int gameReady = launcherPatches.IndexOf(
                "StartupPerformanceTracker.MarkGameReady()",
                StringComparison.Ordinal
            );
            int hideManagedSurface = launcherPatches.IndexOf(
                "startupProgressOverlay.Visible = false",
                StringComparison.Ordinal
            );
            int renderedGameFrame =
                hideManagedSurface >= 0
                    ? launcherPatches.IndexOf(
                        "SceneTree.SignalName.ProcessFrame",
                        hideManagedSurface,
                        StringComparison.Ordinal
                    )
                    : -1;
            int endNativeHandoff = launcherPatches.IndexOf(
                "startupProgressOverlay.EndMainThreadHandoff()",
                StringComparison.Ordinal
            );
            Assert(
                gameReady < hideManagedSurface
                    && hideManagedSurface < renderedGameFrame
                    && renderedGameFrame < endNativeHandoff,
                "the native surface must remain until a Godot game frame has replaced stale PLAY UI"
            );
            Assert(
                modPatches.Contains(
                    "StartupPerformanceTracker.AdvanceTo(StartupStageId.ModDiscovery)",
                    StringComparison.Ordinal
                )
                    && modPatches.Contains(
                        "StartupPerformanceTracker.AdvanceTo(StartupStageId.ModLoad)",
                        StringComparison.Ordinal
                    )
                    && !modPatches.Contains(
                        "ReportProgress(StartupStageId.ModLoad, modId",
                        StringComparison.Ordinal
                    ),
                "mod spans must be stage-only and never include mod ids"
            );
            Assert(
                cloudOverlay.Contains(
                    "ReportProgress(StartupStageId.CloudSync, done, total)",
                    StringComparison.Ordinal
                ) && warmup.Contains("StartupStageId.ShaderWarmup", StringComparison.Ordinal),
                "determinate UI values must originate in real cloud/warmup work units"
            );
        }
    );

    Run(
        "frame-time summary uses nearest-rank percentiles and long-frame thresholds",
        () =>
        {
            var samples = Enumerable.Range(1, 100).Select(value => (long)value * 1_000).ToArray();
            var summary = FrameTimeSummary.Create(samples, frameBudgetUsec: 10_000);

            Assert(summary.Count == 100, "sample count");
            Assert(summary.P50Usec == 50_000, "p50 nearest rank");
            Assert(summary.P95Usec == 95_000, "p95 nearest rank");
            Assert(summary.P99Usec == 99_000, "p99 nearest rank");
            Assert(summary.MaxUsec == 100_000, "max interval");
            Assert(summary.FrameBudgetUsec == 10_000, "frame budget");
            Assert(summary.Over1XBudget == 90, ">1x budget count");
            Assert(summary.Over2XBudget == 80, ">2x budget count");
            Assert(summary.Over3XBudget == 70, ">3x budget count");
            Assert(summary.MaxConsecutiveOver2X == 80, "consecutive >2x frames");
            Assert(summary.Over50Ms == 50, ">50 ms count");
            Assert(summary.Over100Ms == 0, ">100 ms is strict");
            Assert(summary.Over250Ms == 0, ">250 ms count");
        }
    );

    Run(
        "frame metric validation probe is debug-only and bounded",
        () =>
        {
            var repository = FindRepositoryRoot();
            var android = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            var probe = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "DebugFrameTimeProbe.cs")
            );
            var deckMutationProbe = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "DebugDeckCacheMutationProbe.cs"
                )
            );
            var gameplayWarmupSource = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "GameplayPipelineWarmup.cs"
                )
            );
            var modEntry = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "ModEntry.cs")
            );
            var quickRestartCompat = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "QuickRestartPerformanceCompatPatches.cs"
                )
            );
            var modLoadProbe = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "DebugModLoadTimingPatches.cs"
                )
            );
            var recoveryFlow = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "StartupRecoveryFlow.cs")
            );

            Assert(
                android.Contains("consumeDebugFrameProbe(String point)", StringComparison.Ordinal)
                    && android.Contains(
                        "!BuildConfig.VERSION_NAME.contains(\"-debug\")",
                        StringComparison.Ordinal
                    ),
                "production must not expose the frame fault"
            );
            Assert(
                probe.Contains("ValidationTargetIntervals = 180", StringComparison.Ordinal)
                    && probe.Contains("LongCaptureUsec = 120_000_000", StringComparison.Ordinal)
                    && probe.Contains("MenuCaptureUsec = 60_000_000", StringComparison.Ordinal)
                    && probe.Contains("MenuSettleUsec = 5_000_000", StringComparison.Ordinal)
                    && probe.Contains("MaxSpikeMarkers = 64", StringComparison.Ordinal)
                    && probe.Contains("InjectedStallMs = 100", StringComparison.Ordinal)
                    && probe.Contains("Thread.Sleep(InjectedStallMs)", StringComparison.Ordinal)
                    && probe.Contains("BeginGameplayInteractiveSegment()", StringComparison.Ordinal)
                    && probe.Contains(
                        "_segment = \"gameplay-interactive\"",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("segment={probe._segment}", StringComparison.Ordinal)
                    && probe.Contains(
                        "_tree.ProcessFrame -= OnProcessFrame",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("game-menu-idle", StringComparison.Ordinal)
                    && probe.Contains("ShouldAutoContinueRecoverySession", StringComparison.Ordinal)
                    && recoveryFlow.Contains(
                        "debug capture continuing session automatically",
                        StringComparison.Ordinal
                    ),
                "the probe must inject one bounded timing fault and detach"
            );
            Assert(
                android.Contains("\"game-baseline-120\"", StringComparison.Ordinal)
                    && android.Contains("\"game-baseline-safe-120\"", StringComparison.Ordinal)
                    && android.Contains(
                        "STS2_DISABLE_GAMEPLAY_PERFORMANCE_FIXES",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "[FrameProbe] gameplay baseline unavailable",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("game-baseline-120", StringComparison.Ordinal)
                    && probe.Contains("game-baseline-safe-120", StringComparison.Ordinal)
                    && modEntry.Contains(
                        "GameplayPerformanceDisableEnvironmentVariable",
                        StringComparison.Ordinal
                    )
                    && modEntry.Contains(
                        "disableGameplayPerformanceFixes",
                        StringComparison.Ordinal
                    )
                    && modEntry.Contains(
                        "GameLoadFramePacingPatches.Apply(_harmony)",
                        StringComparison.Ordinal
                    )
                    && modEntry.Contains(
                        "DeckViewPerformancePatches.Apply(_harmony)",
                        StringComparison.Ordinal
                    ),
                "the same debug APK must provide a one-variable gameplay-fix baseline"
            );
            Assert(
                modEntry.IndexOf("ModAssemblyRegistry.Install()", StringComparison.Ordinal)
                    < modEntry.IndexOf(
                        "QuickRestartPerformanceCompatPatches.Install(_harmony)",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "ModAssemblyRegistry.IsModAssembly(assembly)",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "assembly.ManifestModule.ModuleVersionId",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "SHA256.HashData(stream)",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "BindingFlags.DeclaredOnly",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains("_attempted", StringComparison.Ordinal)
                    && quickRestartCompat.Contains(
                        "TryRemoveLifecyclePatches",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains("fail-open", StringComparison.Ordinal),
                "Quick Restart compatibility must load late, patch once, and fail open on exact-target mismatch"
            );
            Assert(
                quickRestartCompat.Contains(
                    "GetNestedType(\n                \"PauseMenuButtonPatch\"",
                    StringComparison.Ordinal
                )
                    && quickRestartCompat.Contains(
                        "ExactStaticMethod(\n                pauseMenuButtonPatchType,\n                \"OnPressed\"",
                        StringComparison.Ordinal
                    ),
                "Quick Restart pause-button behavior probe must target the exact nested owner"
            );
            Assert(
                quickRestartCompat.Contains(
                    "STS2_DEBUG_QUICK_RESTART_PROBE",
                    StringComparison.Ordinal
                )
                    && quickRestartCompat.Contains(
                        "[QuickRestartProbe] summary segment=",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains("file_exists_calls=", StringComparison.Ordinal)
                    && android.Contains(
                        "debug_quick_restart_method_probe",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "STS2_DISABLE_QUICK_RESTART_PERFORMANCE_FIX",
                        StringComparison.Ordinal
                    )
                    && probe.Contains(
                        "game-quickrestart-baseline-partition-120",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("game-quickrestart-partition-120", StringComparison.Ordinal),
                "Quick Restart A/B and per-method wrappers must remain explicit debug-only instrumentation"
            );
            Assert(
                modLoadProbe.Contains("STS2_DEBUG_MOD_LOAD_TIMING", StringComparison.Ordinal)
                    && modLoadProbe.Contains(
                        "ModManager.CallModInitializer(Type)",
                        StringComparison.Ordinal
                    )
                    && modLoadProbe.Contains("nameof(Harmony.PatchAll)", StringComparison.Ordinal)
                    && modLoadProbe.Contains("item={_currentOrdinal}", StringComparison.Ordinal)
                    && !modLoadProbe.Contains("manifest.id", StringComparison.Ordinal)
                    && !modLoadProbe.Contains("assembly.Location", StringComparison.Ordinal)
                    && android.Contains("debug_mod_load_probe", StringComparison.Ordinal)
                    && android.Contains("STS2_DEBUG_MOD_LOAD_TIMING", StringComparison.Ordinal),
                "mod-load attribution must be debug-only, anonymous, and span initializer/PatchAll owners"
            );
            var transitionTrace = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "DebugTransitionTimingPatches.cs"
                )
            );
            var modGuardAlert = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ModGuardAlert.cs")
            );
            Assert(
                transitionTrace.Contains(
                    "DebugFrameTimeProbe.IsGameCaptureActive",
                    StringComparison.Ordinal
                )
                    && transitionTrace.Contains(
                        "GetEnvironmentVariable(ArmEnvironmentVariable)",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "Os.setenv(\"STS2_DEBUG_TRANSITION_TIMING\", \"1\", true)",
                        StringComparison.Ordinal
                    )
                    && transitionTrace.Contains(
                        "LogThresholdUsec = 2_000",
                        StringComparison.Ordinal
                    )
                    && transitionTrace.Contains("[FrameTrace]", StringComparison.Ordinal),
                "transition attribution must install only for an explicitly armed debug game capture"
            );
            Assert(
                modGuardAlert.Contains(
                    "GetEnvironmentVariable(QaToolsEnvironmentVariable)",
                    StringComparison.Ordinal
                )
                    && android.Contains(
                        "Os.setenv(\"STS2_DEBUG_QA_TOOLS\", \"1\", true)",
                        StringComparison.Ordinal
                    ),
                "the QA file watcher must not poll external storage in production gameplay"
            );
            var deckViewCache = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "DeckViewPerformancePatches.cs"
                )
            );
            Assert(
                deckViewCache.Contains(
                    "ReferenceEquals(player, cachedPlayer)",
                    StringComparison.Ordinal
                )
                    && deckViewCache.Contains(
                        "WeakReference<GodotObject>",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("WeakReference<object>", StringComparison.Ordinal)
                    && !deckViewCache.Contains(
                        "WeakReference<NDeckViewScreen>",
                        StringComparison.Ordinal
                    )
                    && !deckViewCache.Contains("WeakReference<Player>", StringComparison.Ordinal)
                    && !deckViewCache.Contains("WeakReference<CardModel>", StringComparison.Ordinal)
                    && deckViewCache.Contains(
                        "GodotObject.IsInstanceValid(cached)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "NDebugAudioManager.Instance?.Play(\"map_open.mp3\")",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "NCapstoneContainer.Instance.Open(cached)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "nameof(NDeckViewScreen.AfterCapstoneClosed)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "ReferenceEquals(NCapstoneContainer.Instance.CurrentCapstoneScreen, cached)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("cached.QueueFree()", StringComparison.Ordinal)
                    && deckViewCache.Split("if (TestMode.IsOn)", StringSplitOptions.None).Length
                        >= 3
                    && deckViewCache.Contains(
                        "__instance.Visible = false",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("cached.Visible = true", StringComparison.Ordinal),
                "deck view reuse must preserve behavior without eagerly binding game assemblies in launcher-only mode"
            );
            Assert(
                deckViewCache.Contains(
                    "PileContentsChangedMethod = \"OnPileContentsChanged\"",
                    StringComparison.Ordinal
                )
                    && deckViewCache.Contains(
                        "OnPileContentsChangedPrefix",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("if (__instance.Visible)", StringComparison.Ordinal)
                    && deckViewCache.Contains(
                        "ClearCachedScreen(queueFree: true)",
                        StringComparison.Ordinal
                    ),
                "a hidden cached deck must be invalidated without rebuilding its card grid"
            );
            Assert(
                deckViewCache.Contains(
                    "card.Upgraded += OnCachedCardUpgraded",
                    StringComparison.Ordinal
                )
                    && deckViewCache.Contains(
                        "card.Upgraded -= OnCachedCardUpgraded",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "TreeExiting += OnCachedScreenTreeExiting",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "TreeExiting -= OnCachedScreenTreeExiting",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("ClearCachedScreen", StringComparison.Ordinal)
                    && deckViewCache.Contains("_invalidateAfterClose", StringComparison.Ordinal),
                "card upgrades and run-tree teardown must invalidate the retained deck without leaking subscriptions"
            );
            Assert(
                android.Contains("consumeDebugDeckCacheMutationProbe()", StringComparison.Ordinal)
                    && android.Contains(
                        "intent.removeExtra(\"debug_deck_cache_mutation_probe\")",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmupSource.Contains(
                        "DebugDeckCacheMutationProbe.TryRunAsync(hand, player)",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains(
                        "DebugFrameTimeProbe.IsGameCaptureActive",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains(
                        "player.Deck.AddInternal",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains(
                        "player.Deck.RemoveInternal",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains("UpgradeInternal()", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("DowngradeInternal()", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("finally", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("RestoreMutations", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("pass={Bit(pass)}", StringComparison.Ordinal)
                    && !deckMutationProbe.Contains(".Title", StringComparison.Ordinal)
                    && !deckMutationProbe.Contains(".Id", StringComparison.Ordinal),
                "the debug deck mutation proof must be explicit, reversible, and identity-free"
            );
            var framePacing = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "GameLoadFramePacingPatches.cs"
                )
            );
            Assert(
                framePacing.Contains("LoadRunFramePaced", StringComparison.Ordinal)
                    && framePacing.Contains("StartCombatFramePaced", StringComparison.Ordinal)
                    && framePacing.Contains(
                        "SceneTree.SignalName.ProcessFrame",
                        StringComparison.Ordinal
                    )
                    && framePacing.Contains("MapDrawingsToLoad = null", StringComparison.Ordinal)
                    && framePacing.Contains(
                        "Hook.AfterRoomEntered(runState, room)",
                        StringComparison.Ordinal
                    )
                    && !framePacing.Contains("PrimeFirstCardRender", StringComparison.Ordinal),
                "frame pacing must preserve load/run side effects without speculative fake gameplay work"
            );
            Assert(
                android.Contains("game-safe-120", StringComparison.Ordinal)
                    && android.Contains("game-safe-300", StringComparison.Ordinal)
                    && android.Contains("game-baseline-safe-120", StringComparison.Ordinal)
                    && android.Contains("consumeDebugModSafeMode()", StringComparison.Ordinal)
                    && android.Contains("game-partition-120", StringComparison.Ordinal)
                    && android.Contains("game-menu-safe-60", StringComparison.Ordinal)
                    && android.Contains("game-menu-partition-60", StringComparison.Ordinal)
                    && android.Contains("consumeDebugModPartition()", StringComparison.Ordinal)
                    && android.Contains("isValidDebugModPartition", StringComparison.Ordinal)
                    && probe.Contains("game-partition-120", StringComparison.Ordinal)
                    && probe.Contains("game-menu-partition-60", StringComparison.Ordinal)
                    && probe.Contains("game-safe-300", StringComparison.Ordinal)
                    && probe.Contains("ExtendedCaptureUsec = 300_000_000", StringComparison.Ordinal)
                    && android.Contains("markDebugFrameSpike", StringComparison.Ordinal),
                "mod comparisons and trace markers must stay behind the debug bridge"
            );
            var gameplayWarmup = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "GameplayPipelineWarmup.cs"
                )
            );
            Assert(
                gameplayWarmup.Contains("OS.HasFeature(\"android\")", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "hand.GetViewport() is SubViewport",
                        StringComparison.Ordinal
                    )
                    && !gameplayWarmup.Contains("new SubViewport", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("CoverFirstHandAsync(", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("Player player,", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("Action startFirstTurn", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("startFirstTurn();", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("hand.ActiveHolders.Count", StringComparison.Ordinal)
                    && !gameplayWarmup.Contains("hand.Add(", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "StableFrameWindowMs = 650",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains("MaximumWaitMs = 7_000", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "RenderingServer.SignalName.FramePostDraw",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "NDeckViewScreen.ShowScreen(player)",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "GetSubmenuType<NPauseMenu>()",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "NCapstoneContainer.Instance.Close()",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains("new ScreenBackground", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "MouseFilter = Control.MouseFilterEnum.Ignore",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "Preparing gameplay rendering",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains("cover.QueueFree()", StringComparison.Ordinal),
                "Android must cover the real first hand until its Canvas pipeline set is stable"
            );
            Assert(
                !gameplayWarmup.Contains("MapExposureProbe", StringComparison.Ordinal)
                    && !gameplayWarmup.Contains(
                        "RenderingServer.CanvasItemSetVisible",
                        StringComparison.Ordinal
                    )
                    && !android.Contains("game-map-", StringComparison.Ordinal)
                    && transitionTrace.Contains(
                        "DebugFrameTimeProbe.BeginInteraction(\"map-open\")",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("InteractionSampleCount = 60", StringComparison.Ordinal)
                    && probe.Contains("[InteractionProbe] summary", StringComparison.Ordinal),
                "the rejected first-map exposure candidate must be removed while bounded interaction evidence remains"
            );
            Assert(
                gameplayWarmup.Contains(
                    "DebugFrameTimeProbe.BeginGameplayInteractiveSegment()",
                    StringComparison.Ordinal
                ),
                "the canonical probe must measure 120 seconds after the covered load is revealed"
            );
            Assert(
                framePacing.IndexOf(
                    "CombatManager.Instance.SetUpCombat(room.CombatState)",
                    StringComparison.Ordinal
                )
                    < framePacing.IndexOf(
                        "Hook.AfterRoomEntered(runState, room)",
                        StringComparison.Ordinal
                    )
                    && framePacing.IndexOf(
                        "Hook.AfterRoomEntered(runState, room)",
                        StringComparison.Ordinal
                    )
                        < framePacing.IndexOf(
                            "GameplayPipelineWarmup.CoverFirstHandAsync",
                            StringComparison.Ordinal
                        )
                    && framePacing.Contains(
                        "CombatManager.Instance.AfterCombatRoomLoaded",
                        StringComparison.Ordinal
                    ),
                "the real first turn must start under the cover after room-entry hooks"
            );
        }
    );

    Run(
        "fresh install requires warmup",
        () =>
        {
            var state = new ShaderWarmupState(root, version: 7);
            var result = state.Check();
            Assert(result.NeedsWarmup, result.Reason);
            Assert(!result.RecoveredInterruptedAttempt, "fresh install must not be recovery");
        }
    );

    Run(
        "completed warmup is skipped",
        () =>
        {
            Reset(root);
            var state = new ShaderWarmupState(root, version: 7);
            state.Begin();
            state.Complete();

            var result = new ShaderWarmupState(root, version: 7).Check();
            Assert(!result.NeedsWarmup, result.Reason);
            Assert(!result.RecoveredInterruptedAttempt, "normal completion is not recovery");
            Assert(result.Outcome == ShaderWarmupOutcome.Completed, "completed outcome");
            Assert(!File.Exists(state.AttemptMarkerPath), "attempt marker must be removed");
        }
    );

    Run(
        "interrupted warmup cannot create a crash loop",
        () =>
        {
            Reset(root);
            var firstProcess = new ShaderWarmupState(root, version: 7);
            firstProcess.Begin();

            var nextProcess = new ShaderWarmupState(root, version: 7);
            var result = nextProcess.Check();
            Assert(!result.NeedsWarmup, result.Reason);
            Assert(result.RecoveredInterruptedAttempt, "interrupted attempt must be recovered");
            Assert(result.Outcome == ShaderWarmupOutcome.Interrupted, "interrupted outcome");
            Assert(
                File.ReadAllText(nextProcess.CompletedMarkerPath).Trim() == "7",
                "recovery marker"
            );
            Assert(!File.Exists(nextProcess.AttemptMarkerPath), "recovered attempt marker removed");
        }
    );

    Run(
        "shader warmup persists every bypass outcome without retrying",
        () =>
        {
            foreach (
                var outcome in new[]
                {
                    ShaderWarmupOutcome.DeferredMemoryPressure,
                    ShaderWarmupOutcome.FailedButBypassed,
                }
            )
            {
                Reset(root);
                var state = new ShaderWarmupState(root, version: 7);
                state.Begin();
                state.Complete(outcome, "bounded diagnostic reason");

                var result = new ShaderWarmupState(root, version: 7).Check();
                Assert(!result.NeedsWarmup, $"{outcome} must not repeat");
                Assert(result.Outcome == outcome, $"{outcome} must round-trip");
                Assert(result.Reason == "bounded diagnostic reason", "diagnostic reason");
                Assert(!File.Exists(state.AttemptMarkerPath), "attempt marker must be removed");
            }
        }
    );

    Run(
        "old-version interruption does not suppress current warmup",
        () =>
        {
            Reset(root);
            new ShaderWarmupState(root, version: 6).Begin();

            var result = new ShaderWarmupState(root, version: 7).Check();
            Assert(result.NeedsWarmup, result.Reason);
            Assert(
                !result.RecoveredInterruptedAttempt,
                "old attempt must not count as current recovery"
            );
        }
    );

    Run(
        "completion observed after producer finishes early",
        () =>
        {
            var operation = new ShaderWarmupOperation();
            operation.Complete(restartRequired: false);
            Assert(operation.Completion.IsCompletedSuccessfully, "completion signal was lost");
            Assert(!operation.Completion.Result, "unexpected restart result");
        }
    );

    Run(
        "shader scan keeps the live material set batch-bounded",
        () =>
        {
            var repository = FindRepositoryRoot();
            var warmup = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "ShaderWarmupScreen.cs")
            );
            Assert(
                warmup.Contains("using var packed", StringComparison.Ordinal),
                "each scanned PackedScene must be released"
            );
            Assert(
                warmup.Contains("ResourceLoader.CacheMode.IgnoreDeep", StringComparison.Ordinal),
                "warmup must bypass the reusable resource cache"
            );
            Assert(
                !warmup.Contains("ResourceLoader.CacheMode.Reuse", StringComparison.Ordinal),
                "warmup must not repopulate the reusable resource cache"
            );
            Assert(
                warmup.Contains("CollectWarmupResourcePaths(", StringComparison.Ordinal),
                "warmup must enumerate lightweight paths before loading resources"
            );
            Assert(
                warmup.Contains("if (batch.Count >= BatchSize)", StringComparison.Ordinal)
                    && warmup.Contains("await FlushMaterialBatchAsync", StringComparison.Ordinal),
                "warmup must render and release a bounded batch before loading more resources"
            );
            Assert(
                warmup.Contains("material.Dispose();", StringComparison.Ordinal),
                "warmup must explicitly release each uncached material after rendering"
            );
            Assert(
                !warmup.Contains("ResourceLoader.Load<Material>", StringComparison.Ordinal)
                    && !warmup.Contains("ResourceLoader.Load<Shader>", StringComparison.Ordinal),
                "a typed load of a generic .tres can throw before the mismatched resource is released"
            );
            Assert(
                warmup.Contains("resource.Dispose();", StringComparison.Ordinal),
                "every loose non-material resource probe must be explicitly released"
            );
            Assert(
                !warmup.Contains("CollectMaterialsAsync()", StringComparison.Ordinal)
                    && !warmup.Contains(
                        "new Dictionary<string, Material>()",
                        StringComparison.Ordinal
                    ),
                "warmup must never retain the complete material graph before rendering"
            );
        }
    );

    Run(
        "shader warmup defers before Android memory pressure becomes LMK",
        () =>
        {
            const long mib = 1024L * 1024L;
            var healthy = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    trimLevel: 0,
                    systemLowMemory: false,
                    availableBytes: 3_000 * mib,
                    lowMemoryThresholdBytes: 512 * mib,
                    totalBytes: 12_000 * mib,
                    processPssBytes: 1_900 * mib
                )
            );
            Assert(!healthy.ShouldDefer, healthy.Reason);

            var trimPressure = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    10,
                    false,
                    3_000 * mib,
                    512 * mib,
                    12_000 * mib,
                    900 * mib
                )
            );
            Assert(trimPressure.ShouldDefer, "TRIM_MEMORY_RUNNING_LOW must stop optional warmup");

            var systemLow = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    0,
                    true,
                    900 * mib,
                    512 * mib,
                    6_000 * mib,
                    900 * mib
                )
            );
            Assert(systemLow.ShouldDefer, "ActivityManager low-memory state must stop warmup");

            var lowHeadroom = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    0,
                    false,
                    900 * mib,
                    512 * mib,
                    4_000 * mib,
                    900 * mib
                )
            );
            Assert(lowHeadroom.ShouldDefer, "system reserve must remain above the LMK threshold");

            var processBudget = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    0,
                    false,
                    3_000 * mib,
                    512 * mib,
                    4_000 * mib,
                    1_400 * mib
                )
            );
            Assert(processBudget.ShouldDefer, "low-RAM devices need a bounded process PSS budget");

            var unavailable = ShaderWarmupMemoryPolicy.Evaluate(
                ShaderWarmupMemorySnapshot.Unavailable
            );
            Assert(
                !unavailable.ShouldDefer,
                "missing telemetry must preserve the existing bounded path"
            );

            var repository = FindRepositoryRoot();
            var warmup = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "ShaderWarmupScreen.cs")
            );
            Assert(
                warmup.Contains("BeginWarmupMemoryMonitoring", StringComparison.Ordinal)
                    && warmup.Contains("EndWarmupMemoryMonitoring", StringComparison.Ordinal)
                    && warmup.Contains(
                        "ShaderWarmupMemoryPolicy.Evaluate",
                        StringComparison.Ordinal
                    ),
                "the physical warmup path must own and consume Android memory monitoring"
            );
            Assert(
                warmup.Contains("WarmupDeferredForMemoryException", StringComparison.Ordinal)
                    && warmup.Contains("deferred for memory safety", StringComparison.Ordinal),
                "memory pressure must take an explicit non-failure completion path"
            );
        }
    );

    Run(
        "startup recovery payload is bounded and fails closed",
        () =>
        {
            var request = StartupRecoveryRequest.Parse("1\n2\nmod-loading\nExampleMod\nLOW_MEMORY");
            Assert(request.Pending, "pending request");
            Assert(request.FailureCount == 2, "failure count");
            Assert(request.Stage == "mod-loading", "stage");
            Assert(request.ModCandidate == "ExampleMod", "candidate");
            Assert(request.Reason == "LOW_MEMORY", "reason");

            Assert(!StartupRecoveryRequest.Parse("torn").Pending, "torn payload fails closed");
            Assert(
                !StartupRecoveryRequest.Parse("1\n2\nmod-loading\n../escape\nCRASH").Pending,
                "path-like candidate fails closed"
            );
        }
    );

    Run(
        "Safe Mode and mod isolation are session-only path filters",
        () =>
        {
            var mods = new[]
            {
                new RecoveryModDescriptor("A", "/mods/a", Array.Empty<string>()),
                new RecoveryModDescriptor("B", "/mods/b", new[] { "A" }),
                new RecoveryModDescriptor("C", "/mods/c", Array.Empty<string>()),
                new RecoveryModDescriptor("D", "/mods/d", Array.Empty<string>()),
            };

            var normal = ModRecoveryPolicy.Build(RecoveryAction.ContinueNormally, "C", mods);
            Assert(!normal.FiltersMods, "normal launch cannot filter mods");
            Assert(normal.ShouldExposeFile("/mods", "/mods/c/C.dll"), "normal visibility");

            var safe = ModRecoveryPolicy.Build(RecoveryAction.SafeMode, "C", mods);
            Assert(safe.FiltersMods && safe.SkipOptionalWarmup, "Safe Mode contract");
            Assert(safe.ShouldExposeDirectory("/mods", "/mods"), "scan root remains valid");
            Assert(!safe.ShouldExposeDirectory("/mods", "/mods/a"), "all mod dirs hidden");
            Assert(!safe.ShouldExposeFile("/mods", "/mods/root.json"), "root mod hidden");

            var exclude = ModRecoveryPolicy.Build(RecoveryAction.ExcludeCandidate, "C", mods);
            Assert(exclude.ShouldExposeDirectory("/mods", "/mods/a"), "other mod stays visible");
            Assert(!exclude.ShouldExposeFile("/mods", "/mods/c/C.dll"), "candidate hidden");
            Assert(
                exclude.ShouldExposeFile("/mods", "/mods/unmanaged.json"),
                "candidate exclusion cannot silently hide unrelated root mods"
            );
            Assert(
                ModRecoveryPolicy
                    .Build(RecoveryAction.ExcludeCandidate, "missing", mods)
                    .SkipOptionalWarmup,
                "unknown candidate must fall back to Safe Mode"
            );

            var bisect = ModRecoveryPolicy.Build(RecoveryAction.BisectFirstHalf, "C", mods);
            Assert(bisect.ShouldExposeDirectory("/mods", "/mods/a"), "first half A");
            Assert(bisect.ShouldExposeDirectory("/mods", "/mods/b"), "first half B");
            Assert(!bisect.ShouldExposeDirectory("/mods", "/mods/c"), "second half C hidden");
            Assert(!bisect.ShouldExposeDirectory("/mods", "/mods/d"), "second half D hidden");

            var partition = ModRecoveryPolicy.BuildPartition(1, 2, mods);
            Assert(
                partition.Action == RecoveryAction.DiagnosticPartition,
                "debug partition action"
            );
            Assert(!partition.ShouldExposeDirectory("/mods", "/mods/a"), "partition A hidden");
            Assert(!partition.ShouldExposeDirectory("/mods", "/mods/b"), "partition B hidden");
            Assert(partition.ShouldExposeDirectory("/mods", "/mods/c"), "partition C visible");
            Assert(partition.ShouldExposeDirectory("/mods", "/mods/d"), "partition D visible");
            Assert(
                ModRecoveryPolicy.BuildPartition(9, 2, mods).SkipOptionalWarmup,
                "invalid debug partition must fail closed to Safe Mode"
            );

            var partitionWithCompanion = ModRecoveryPolicy.BuildPartition(
                0,
                4,
                mods,
                new[] { "D" }
            );
            Assert(
                partitionWithCompanion.Action == RecoveryAction.DiagnosticPartition,
                "valid debug companion must preserve partition mode"
            );
            Assert(
                partitionWithCompanion.ShouldExposeDirectory("/mods", "/mods/a"),
                "partition selection remains visible with companion"
            );
            Assert(
                partitionWithCompanion.ShouldExposeDirectory("/mods", "/mods/d"),
                "required companion must be visible outside the selected partition"
            );
            Assert(
                ModRecoveryPolicy
                    .BuildPartition(0, 4, mods, new[] { "missing" })
                    .SkipOptionalWarmup,
                "missing debug companion must fail closed to Safe Mode"
            );

            var companionWithDependency = ModRecoveryPolicy.BuildPartition(
                3,
                4,
                mods,
                new[] { "B" }
            );
            Assert(
                companionWithDependency.ShouldExposeDirectory("/mods", "/mods/a"),
                "debug companion dependency closure must be visible"
            );

            var repository = FindRepositoryRoot();
            var loader = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ModLoaderPatches.cs")
            );
            var fileIo = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ExternalModsFileIo.cs")
            );
            var launch = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            Assert(
                loader.Contains("TryLoadModPrefix", StringComparison.Ordinal)
                    && loader.Contains("RecordModCandidate", StringComparison.Ordinal)
                    && loader.Contains("TryLoadModPostfix", StringComparison.Ordinal)
                    && loader.Contains("RecordModSuccessful", StringComparison.Ordinal),
                "mod execution boundary must journal candidate and success"
            );
            Assert(
                fileIo.Contains(
                    "ModRecoverySession.ShouldExposeDirectory",
                    StringComparison.Ordinal
                )
                    && fileIo.Contains(
                        "ModRecoverySession.ShouldExposeFile",
                        StringComparison.Ordinal
                    ),
                "every game mod scan must consume the session-only filter"
            );
            Assert(
                launch.IndexOf("ResolveRecoveryAsync", StringComparison.Ordinal)
                    < launch.IndexOf("WaitForLaunch", StringComparison.Ordinal)
                    && launch.Contains("SkipOptionalWarmup", StringComparison.Ordinal)
                    && launch.Contains("ShowRecoverySuccessAsync", StringComparison.Ordinal),
                "recovery must resolve before PLAY, skip optional warmup, and expose normal restart"
            );
            Assert(
                !fileIo.Contains("Directory.Move", StringComparison.Ordinal)
                    && !fileIo.Contains("Directory.Delete", StringComparison.Ordinal),
                "session isolation must never mutate real mod directories"
            );
        }
    );

    Run(
        "BaseLib treasure compatibility replaces only the unsafe direct-field postfix",
        () =>
        {
            Assert(
                BaseLibTreasurePatchPolicy.RequiresReplacement(
                    new Version(3, 4, 5),
                    new[] { "_runState", "_chestButton" },
                    inspectionSucceeded: true
                ),
                "the proven BaseLib 3.4.5 direct-field shape must be replaced"
            );
            Assert(
                !BaseLibTreasurePatchPolicy.RequiresReplacement(
                    new Version(3, 4, 6),
                    Array.Empty<string>(),
                    inspectionSucceeded: true
                ),
                "a reflected future postfix must remain installed"
            );
            Assert(
                !BaseLibTreasurePatchPolicy.RequiresReplacement(
                    new Version(3, 4, 5),
                    new[] { "_runState" },
                    inspectionSucceeded: true
                ),
                "a partial or unrelated field shape cannot be guessed unsafe"
            );
            Assert(
                BaseLibTreasurePatchPolicy.RequiresReplacement(
                    new Version(3, 4, 4),
                    Array.Empty<string>(),
                    inspectionSucceeded: false
                ),
                "known-bad 3.4.4 must fail safe when IL inspection is unavailable"
            );
            Assert(
                !BaseLibTreasurePatchPolicy.RequiresReplacement(
                    new Version(3, 4, 3),
                    Array.Empty<string>(),
                    inspectionSucceeded: false
                ),
                "known-good 3.4.3 and older builds must not lose custom chests"
            );

            var repository = FindRepositoryRoot();
            var compat = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "BaseLibCompatPatches.cs")
            );
            Assert(
                compat.Contains("PatchProcessor", StringComparison.Ordinal)
                    && compat.Contains(
                        ".GetOriginalInstructions(postfix)",
                        StringComparison.Ordinal
                    )
                    && compat.Contains(
                        "instruction.opcode == OpCodes.Ldfld",
                        StringComparison.Ordinal
                    )
                    && compat.Contains(
                        "_harmony.Unpatch(ready, _unsafeTreasurePostfix)",
                        StringComparison.Ordinal
                    )
                    && compat.Contains("SafeTreasureRoomReadyPostfix", StringComparison.Ordinal)
                    && compat.Contains(
                        "_treasureRunStateField?.GetValue(__instance)",
                        StringComparison.Ordinal
                    )
                    && compat.Contains(
                        "_treasureChestButtonField.GetValue(__instance)",
                        StringComparison.Ordinal
                    )
                    && !compat.Contains("UnpatchAll(\"BaseLib\")", StringComparison.Ordinal),
                "the runtime fix must fingerprint and replace only the unsafe treasure postfix"
            );
        }
    );

    Run(
        "game update interruption exposes only an old or new complete tuple",
        () =>
        {
            var transactionRoot = Path.Combine(root, "game-transaction");
            Reset(transactionRoot);
            WriteInstall(transactionRoot, "old");

            var partial = GameInstallTransaction.Begin(transactionRoot, forceFresh: false);
            ReplaceInstallFile(partial.StagingGameDirectory, "SlayTheSpire2.pck", "GDPCnew");
            GameInstallTransaction.Recover(transactionRoot);
            AssertInstallTuple(transactionRoot, "old", "partial download");

            foreach (
                var faultPoint in new[]
                {
                    GameInstallFaultPoint.AfterPrepared,
                    GameInstallFaultPoint.AfterActiveRetired,
                    GameInstallFaultPoint.AfterStagedActivated,
                }
            )
            {
                GameInstallTransaction.DiscardStaging(transactionRoot);
                var transaction = GameInstallTransaction.Begin(transactionRoot, forceFresh: true);
                WriteInstallFiles(transaction.StagingGameDirectory, "new");
                transaction.Prepare(
                    GameInstallTuple.Capture(
                        transaction.StagingGameDirectory,
                        "public-beta",
                        "new-build",
                        new Dictionary<uint, ulong> { [2868842] = 5045320271505434676 }
                    )
                );

                if (faultPoint == GameInstallFaultPoint.AfterPrepared)
                {
                    var extraAssembly = Path.Combine(
                        transaction.StagingGameDirectory,
                        "data_sts2_windows_x86_64",
                        "unexpected.dll"
                    );
                    File.WriteAllText(extraAssembly, "mixed");
                    bool rejectedMixedAssemblySet = false;
                    try
                    {
                        transaction.Commit();
                    }
                    catch (InvalidDataException)
                    {
                        rejectedMixedAssemblySet = true;
                    }
                    Assert(
                        rejectedMixedAssemblySet,
                        "activation must reject a changed game assembly set"
                    );
                    File.Delete(extraAssembly);
                }

                try
                {
                    transaction.Commit(point =>
                    {
                        if (point == faultPoint)
                            throw new GameInstallInterruptionException(point.ToString());
                    });
                }
                catch (GameInstallInterruptionException) { }

                GameInstallTransaction.Recover(transactionRoot);
                var (pck, assembly) = ReadInstallTuple(transactionRoot);
                Assert(pck == assembly, $"{faultPoint} produced mixed {pck}/{assembly}");
                Assert(pck is "old" or "new", $"{faultPoint} unknown tuple {pck}");
                if (pck == "new")
                {
                    var active = GameInstallTransaction.ReadActiveTuple(transactionRoot);
                    Assert(active?.Branch == "public-beta", $"{faultPoint} active branch");
                    Assert(active?.BuildId == "new-build", $"{faultPoint} active build");
                }

                GameInstallTransaction.CompleteValidation(transactionRoot);
                Assert(
                    !Directory.Exists(GameInstallTransaction.GetRollbackPath(transactionRoot)),
                    $"{faultPoint} rollback cleanup"
                );
                if (pck == "old")
                    continue;

                // Restore a deterministic old baseline for the next injected window.
                Reset(transactionRoot);
                WriteInstall(transactionRoot, "old");
            }

            var repository = FindRepositoryRoot();
            var downloader = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "DepotDownloader.cs")
            );
            var model = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherModel.cs")
            );
            var faultInjector = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "GameInstallFaultInjector.cs"
                )
            );
            var android = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            Assert(
                downloader.Contains("GameInstallTransaction.Begin", StringComparison.Ordinal)
                    && downloader.Contains("transaction.Prepare", StringComparison.Ordinal)
                    && downloader.Contains("transaction.Commit", StringComparison.Ordinal),
                "depot writes must activate only through the directory transaction"
            );
            Assert(
                !model.Contains(
                    "Directory.Delete(gameDir, recursive: true)",
                    StringComparison.Ordinal
                )
                    && model.IndexOf("SaveSelectedBranch", StringComparison.Ordinal)
                        > model.IndexOf("DownloadAsync", StringComparison.Ordinal),
                "branch selection must publish only after a successful active commit"
            );
            Assert(
                android.Contains("GameInstallRecovery.recover", StringComparison.Ordinal)
                    && android.IndexOf("GameInstallRecovery.recover", StringComparison.Ordinal)
                        < android.IndexOf(
                            "setupAssemblies(gameInstallReady);",
                            StringComparison.Ordinal
                        )
                    && android.Contains(
                        "private void setupAssemblies(boolean copyGameAssemblies)",
                        StringComparison.Ordinal
                    )
                    && android.IndexOf(
                        "Copied \" + count + \" BCL assemblies from assets",
                        StringComparison.Ordinal
                    ) < android.IndexOf("if (!copyGameAssemblies)", StringComparison.Ordinal),
                "Android must always extract launcher assemblies after recovery while refusing partial game assemblies"
            );
            foreach (
                var faultPoint in new[]
                {
                    "after-staging-created",
                    "after-file-verified",
                    "after-depot-manifest-committed",
                    "after-all-depots-verified",
                    "after-pck-patched",
                }
            )
            {
                Assert(
                    downloader.Contains($"Hit(\"{faultPoint}\")", StringComparison.Ordinal),
                    $"missing deterministic update fault hook {faultPoint}"
                );
            }
            Assert(
                downloader.Contains(
                    "transaction.Commit(_faultInjector.Hit)",
                    StringComparison.Ordinal
                ),
                "directory activation must expose every rename fault point"
            );
            Assert(
                faultInjector.Contains(
                    "DllImport(\"libc\", EntryPoint = \"kill\"",
                    StringComparison.Ordinal
                )
                    && faultInjector.Contains("SigKill = 9", StringComparison.Ordinal)
                    && faultInjector.IndexOf(
                        "KillProcess(GetProcessId(), SigKill)",
                        StringComparison.Ordinal
                    )
                        < faultInjector.IndexOf(
                            "Environment.FailFast($\"debug game-install fault",
                            StringComparison.Ordinal
                        ),
                "managed update fault injection must kill the Android host process"
            );
            Assert(
                android.Contains(
                    "clearManagedGameInstallFaultOutsideDebug();",
                    StringComparison.Ordinal
                )
                    && android.Contains(
                        "new File(getFilesDir(), GAME_INSTALL_FAULT_MARKER)",
                        StringComparison.Ordinal
                    )
                    && android.IndexOf(
                        "clearManagedGameInstallFaultOutsideDebug();",
                        StringComparison.Ordinal
                    ) < android.IndexOf("GameInstallRecovery.recover", StringComparison.Ordinal),
                "production startup must discard any debug fault marker before recovery"
            );
            foreach (
                var faultPoint in new[]
                {
                    "before-install-recovery",
                    "after-install-recovery",
                    "before-cache-staging",
                    "after-cache-staging",
                    "before-assembly-sync",
                    "after-assembly-sync",
                }
            )
            {
                Assert(
                    android.Contains(
                        $"maybeInjectDebugGameInstallFault(\"{faultPoint}\")",
                        StringComparison.Ordinal
                    ),
                    $"missing deterministic Android fault hook {faultPoint}"
                );
            }
            Assert(
                android.Contains("debug_renderer_override", StringComparison.Ordinal)
                    && android.Contains(
                        "commands.add(\"gl_compatibility\")",
                        StringComparison.Ordinal
                    )
                    && android.Contains("commands.add(\"opengl3\")", StringComparison.Ordinal)
                    && android.Contains(
                        "VERSION_NAME.contains(\"-debug\")",
                        StringComparison.Ordinal
                    ),
                "renderer capability probe must be explicit and unavailable in production builds"
            );
        }
    );

    Run(
        "Android mod configuration resolves to app-private XDG storage",
        () =>
        {
            var repository = FindRepositoryRoot();
            var modEntry = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "ModEntry.cs")
            );
            var godotApp = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            Assert(
                godotApp.Contains(
                    "android.system.Os.setenv(\"XDG_CONFIG_HOME\"",
                    StringComparison.Ordinal
                )
                    && modEntry.Contains(
                        "GetEnvironmentVariable(\"XDG_CONFIG_HOME\")",
                        StringComparison.Ordinal
                    )
                    && !modEntry.Contains(
                        "Path.Combine(OS.GetUserDataDir()",
                        StringComparison.Ordinal
                    ),
                "mods must not inherit Android's unwritable /data/.config fallback"
            );

            if (OperatingSystem.IsLinux())
            {
                var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                var expected = Path.Combine(root, "xdg-config");
                try
                {
                    Directory.CreateDirectory(expected);
                    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", expected);
                    Assert(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                            == expected,
                        ".NET Unix ApplicationData must honor XDG_CONFIG_HOME"
                    );
                }
                finally
                {
                    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
                }
            }
        }
    );

    Run(
        "renderer recovery is repeated-pre-frame, foreground-only, and one-shot",
        () =>
        {
            var repository = FindRepositoryRoot();
            var javaRoot = Path.Combine(
                repository,
                "android",
                "src",
                "com",
                "game",
                "sts2launcher",
                "modmanager"
            );
            var godotApp = File.ReadAllText(Path.Combine(javaRoot, "GodotApp.java"));
            var policy = File.ReadAllText(Path.Combine(javaRoot, "RendererRecoveryPolicy.java"));
            var patches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            var flow = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "StartupRecoveryFlow.cs")
            );

            Assert(
                policy.Contains("RECOVERY_THRESHOLD", StringComparison.Ordinal)
                    && policy.Contains("launcher-awaiting-frame", StringComparison.Ordinal)
                    && !policy.Contains("\"QueuePresentKHR\"", StringComparison.Ordinal),
                "renderer recovery must use durable pre-frame state, never driver log text"
            );
            Assert(
                godotApp.IndexOf("markStartupForeground(false);", StringComparison.Ordinal)
                    < godotApp.IndexOf("super.onPause();", StringComparison.Ordinal),
                "background state must persist before Godot tears down its Surface"
            );
            Assert(
                godotApp.Contains("RENDERER_COMPATIBILITY_ONCE_PREF", StringComparison.Ordinal)
                    && godotApp.Contains(
                        ".remove(RENDERER_COMPATIBILITY_ONCE_PREF)",
                        StringComparison.Ordinal
                    )
                    && godotApp.Contains(
                        "isCompatibilityRendererSession",
                        StringComparison.Ordinal
                    ),
                "compatibility renderer selection must be consumed once and exposed to recovery UI"
            );
            Assert(
                patches.IndexOf("launcher-awaiting-frame", StringComparison.Ordinal)
                    < patches.IndexOf("SceneTree.SignalName.ProcessFrame", StringComparison.Ordinal)
                    && patches.IndexOf(
                        "SceneTree.SignalName.ProcessFrame",
                        StringComparison.Ordinal
                    ) < patches.IndexOf("launcher-ready", StringComparison.Ordinal),
                "launcher-ready must be recorded only after a main-loop frame"
            );
            Assert(
                flow.Contains("Restart with Vulkan", StringComparison.Ordinal)
                    && flow.Contains("Continue in compatibility mode", StringComparison.Ordinal),
                "a compatibility session must offer an explicit Vulkan restore path"
            );
        }
    );

    Run(
        "dialog teardown fallback is one-shot",
        () =>
        {
            var completion = new DialogCompletion<string>("cancelled");
            completion.CompleteFallback();
            completion.Complete("late button result");
            Assert(completion.Task.IsCompletedSuccessfully, "teardown completion was lost");
            Assert(completion.Task.Result == "cancelled", "first completion must win");
        }
    );

    Run(
        "dialog button result wins before teardown",
        () =>
        {
            var completion = new DialogCompletion<string>("cancelled");
            completion.Complete("selected");
            completion.CompleteFallback();
            Assert(completion.Task.Result == "selected", "teardown must not overwrite a choice");
        }
    );

    Run(
        "awaited dialogs complete on every tree teardown",
        () =>
        {
            var repository = FindRepositoryRoot();
            var resultDialogs = new[]
            {
                "ProfilePickerDialog.cs",
                "BackupRestorePickerDialog.cs",
                "ProfileCopyPickerDialog.cs",
                "LocalOnlyMenuDialog.cs",
                "CloudConflictDialog.cs",
            };

            foreach (var fileName in resultDialogs)
            {
                var source = File.ReadAllText(
                    Path.Combine(
                        repository,
                        "src",
                        "STS2Mobile",
                        "Launcher",
                        "Components",
                        fileName
                    )
                );
                Assert(
                    source.Contains("ModalGate.Register(this);", StringComparison.Ordinal),
                    $"{fileName} must register Android Back handling"
                );
                Assert(
                    source.Contains(
                        "TreeExiting += _completion.CompleteFallback;",
                        StringComparison.Ordinal
                    ),
                    $"{fileName} must resolve its awaited result on teardown"
                );
            }

            var simpleResult = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Components",
                    "SimpleResultDialog.cs"
                )
            );
            Assert(
                simpleResult.Contains("TreeExiting += FireClosed;", StringComparison.Ordinal),
                "SimpleResultDialog must raise Closed on teardown"
            );

            var branchPicker = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Components",
                    "BranchPickerDialog.cs"
                )
            );
            Assert(
                branchPicker.Contains("ModalGate.Register(this);", StringComparison.Ordinal),
                "BranchPickerDialog must register Android Back handling"
            );
            Assert(
                branchPicker.Contains("TreeExiting += FireCancelled;", StringComparison.Ordinal),
                "BranchPickerDialog must cancel its awaited picker on teardown"
            );
        }
    );

    Run(
        "launcher initialization failure has an explicit caller path",
        () =>
        {
            var repository = FindRepositoryRoot();
            var launcherUi = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherUI.cs")
            );
            Assert(
                launcherUi.Contains("public bool Initialize()", StringComparison.Ordinal),
                "LauncherUI.Initialize must report failure instead of returning half-initialized"
            );

            var launcherPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            Assert(
                launcherPatches.Contains("if (launcher.Initialize())", StringComparison.Ordinal),
                "game startup must branch on launcher initialization"
            );

            var modEntry = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "ModEntry.cs")
            );
            Assert(
                modEntry.Contains("if (!launcher.Initialize())", StringComparison.Ordinal),
                "standalone startup must surface launcher initialization failure"
            );
            Assert(
                modEntry.Contains("Call(\"hideLoadingOverlay\")", StringComparison.Ordinal),
                "the always-on native overlay must also hand off to a standalone launcher"
            );
            Assert(
                !modEntry.Contains(
                    "Callable.From(() => LauncherModel.GetGodotApp()?.Call",
                    StringComparison.Ordinal
                ),
                "the deferred handoff must be a void callback, not a nullable Variant callback"
            );
        }
    );

    Run(
        "native startup overlay releases launcher input before PLAY wait",
        () =>
        {
            var repository = FindRepositoryRoot();
            var launcherPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            var initialized = launcherPatches.IndexOf(
                "PatchHelper.Log(\"Launcher UI displayed\");",
                StringComparison.Ordinal
            );
            var overlayHidden = launcherPatches.IndexOf(
                "Call(\"hideLoadingOverlay\")",
                StringComparison.Ordinal
            );
            var playWait = launcherPatches.IndexOf(
                "await launcher.WaitForLaunch()",
                StringComparison.Ordinal
            );
            var playReady = launcherPatches.IndexOf(
                "PatchHelper.Log(\"Launcher ready for PLAY\");",
                StringComparison.Ordinal
            );
            var recoveryResolved = launcherPatches.IndexOf(
                "await StartupRecoveryFlow.ResolveRecoveryAsync(launcher)",
                StringComparison.Ordinal
            );

            Assert(initialized >= 0, "game launcher must report successful initialization");
            Assert(overlayHidden >= 0, "game launcher must dismiss the native startup overlay");
            Assert(playWait >= 0, "game launcher must await explicit PLAY");
            Assert(
                initialized < overlayHidden && overlayHidden < playWait,
                "the touch-swallowing native startup overlay must be hidden once launcher UI is ready"
            );
            Assert(
                recoveryResolved >= 0 && playReady > recoveryResolved && playReady < playWait,
                "PLAY readiness must be reported only after recovery resolves and before input wait"
            );
        }
    );

    Run(
        "whole-launcher teardown releases pending launch and auth waits",
        () =>
        {
            var repository = FindRepositoryRoot();
            var model = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherModel.cs")
            );
            Assert(
                model.Contains("public Task<bool> WaitForLaunch()", StringComparison.Ordinal),
                "launch wait must communicate normal PLAY versus teardown"
            );
            Assert(
                model.Contains("_launchTcs?.TrySetResult(false);", StringComparison.Ordinal),
                "disposing a launcher must release the pending launch wait"
            );
            Assert(
                model.Contains("_codeTcs?.TrySetCanceled();", StringComparison.Ordinal),
                "disposing a launcher must release a pending Steam Guard wait"
            );

            var patches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            Assert(
                patches.Contains("if (await launcher.WaitForLaunch())", StringComparison.Ordinal),
                "game handoff must distinguish PLAY from unexpected launcher teardown"
            );
            Assert(
                patches.Contains("if (!userLaunched)", StringComparison.Ordinal)
                    && patches.Contains("CloudSyncEnabled = false;", StringComparison.Ordinal),
                "a teardown fallback must not run cloud sync without PLAY"
            );
        }
    );

    Run(
        "startup transitions keep Back and deferred UI lifecycle-safe",
        () =>
        {
            var repository = FindRepositoryRoot();
            var launcherRoot = Path.Combine(repository, "src", "STS2Mobile", "Launcher");
            var cloudOverlay = File.ReadAllText(Path.Combine(launcherRoot, "CloudSyncOverlay.cs"));
            var warmup = File.ReadAllText(Path.Combine(launcherRoot, "ShaderWarmupScreen.cs"));
            var launcherUi = File.ReadAllText(Path.Combine(launcherRoot, "LauncherUI.cs"));
            var inputPatches = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "GameInputSuppressPatches.cs"
                )
            );
            var launcherPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );

            foreach (var source in new[] { cloudOverlay, warmup })
            {
                Assert(
                    source.Contains("StartupInputGate.Enter(this);", StringComparison.Ordinal)
                        && source.Contains(
                            "StartupInputGate.Exit(this);",
                            StringComparison.Ordinal
                        ),
                    "every full-screen startup transition must hold the shared input gate"
                );
                Assert(
                    source.Contains("NotificationWMGoBackRequest", StringComparison.Ordinal)
                        && source.Contains(
                            "StartupInputGate.HandleBack();",
                            StringComparison.Ordinal
                        ),
                    "startup transitions must route Android Back through the shared gate"
                );
            }

            Assert(
                inputPatches.Contains("!StartupInputGate.Active", StringComparison.Ordinal),
                "raw game input must stay suppressed after LauncherUI is freed"
            );
            Assert(
                launcherPatches.Contains(
                    "using var startupInputLease = StartupInputGate.Hold(gameNode);",
                    StringComparison.Ordinal
                ),
                "the input gate must span every PLAY-to-GameStartup transition"
            );
            Assert(
                launcherUi.Contains(
                    "tree.AutoAcceptQuit = !StartupInputGate.Active;",
                    StringComparison.Ordinal
                ),
                "launcher teardown must not reopen auto-quit under a startup transition"
            );
            Assert(
                cloudOverlay.Contains(
                    "GodotObject.IsInstanceValid(this) || !IsInsideTree()",
                    StringComparison.Ordinal
                )
                    && cloudOverlay.Contains("IsAlive(_statusLabel)", StringComparison.Ordinal)
                    && cloudOverlay.Contains("IsAlive(_progressBar)", StringComparison.Ordinal),
                "deferred cloud UI callbacks must reject freed nodes and controls"
            );
        }
    );

    Run(
        "cloud drain waits never poll on the Godot main thread",
        () =>
        {
            var repository = FindRepositoryRoot();
            var patchesRoot = Path.Combine(repository, "src", "STS2Mobile", "Patches");
            var lifecycle = File.ReadAllText(Path.Combine(patchesRoot, "AppLifecyclePatches.cs"));
            var launcher = File.ReadAllText(Path.Combine(patchesRoot, "LauncherPatches.cs"));

            Assert(
                lifecycle.Contains("_ = Task.Run(() =>", StringComparison.Ordinal)
                    && lifecycle.Contains(
                        "Callable.From(RestartAfterQuitFlush).CallDeferred();",
                        StringComparison.Ordinal
                    ),
                "background/quit cloud drains must run off-main and defer restart"
            );
            Assert(
                lifecycle.Contains(
                    "Interlocked.Exchange(ref _quitRestartInProgress, 1)",
                    StringComparison.Ordinal
                ),
                "repeated Quit calls must not start concurrent drain/restart operations"
            );
            Assert(
                lifecycle.Contains(
                    "Failed to queue deferred quit restart",
                    StringComparison.Ordinal
                )
                    && CountOccurrences(
                        lifecycle,
                        "Interlocked.Exchange(ref _quitRestartInProgress, 0)"
                    ) >= 3,
                "every deferred-restart failure path must release the one-shot latch"
            );
            Assert(
                lifecycle.Contains(
                    "Interlocked.Exchange(ref _backgroundFlushInProgress, 1)",
                    StringComparison.Ordinal
                )
                    && lifecycle.Contains(
                        "Interlocked.Exchange(ref _backgroundFlushInProgress, 0)",
                        StringComparison.Ordinal
                    )
                    && CountOccurrences(
                        lifecycle,
                        "Interlocked.Exchange(ref _backgroundFlushInProgress, 0)"
                    ) >= 2,
                "repeated background callbacks must coalesce concurrent cloud drains"
            );
            Assert(
                !lifecycle.Contains(
                    "SteamKit2CloudSaveStore.Instance?.Flush(300_000)",
                    StringComparison.Ordinal
                ),
                "Quit must not synchronously poll cloud writes for five minutes"
            );

            const string asyncFlush = "await Task.Run(() => cloudStore.Flush(timeoutMs: 300_000))";
            Assert(
                CountOccurrences(launcher, asyncFlush) == 2,
                "both conflict verification flushes must be awaited off-main"
            );
        }
    );

    Run(
        "APK build enforces both compatibility audits",
        () =>
        {
            var repository = FindRepositoryRoot();
            var buildScript = File.ReadAllText(Path.Combine(repository, "scripts", "build.sh"));
            Assert(
                buildScript.Contains(
                    "tools/memberref-audit/audit.csproj",
                    StringComparison.Ordinal
                ),
                "APK build must reject direct/interface contract mismatches"
            );
            Assert(
                buildScript.Contains(
                    "tools/patch-target-audit/audit.csproj",
                    StringComparison.Ordinal
                ),
                "APK build must reject reflection/Harmony target mismatches"
            );
            Assert(
                buildScript.Contains("scripts/make-bootstrap-pck.py", StringComparison.Ordinal),
                "APK build must generate the fresh-install Godot bootstrap"
            );

            var dockerBuild = File.ReadAllText(Path.Combine(repository, "docker", "build-apk.sh"));
            foreach (
                var requiredCheck in new[]
                {
                    "tools/stability-tests/stability-tests.csproj",
                    "tools/stability-tests-java/run.sh",
                    "tools/device-performance/tests/run.sh",
                    "tools/memberref-audit/tests/run.sh",
                    "tools/patch-target-audit/tests/run.sh",
                    "tools/workshop-sync-tests/workshop-sync-tests.csproj",
                }
            )
            {
                Assert(
                    dockerBuild.Contains(requiredCheck, StringComparison.Ordinal),
                    $"container APK build must run {requiredCheck}"
                );
            }
            Assert(
                dockerBuild.Contains("assets/bootstrap.pck", StringComparison.Ordinal)
                    && dockerBuild.Contains("bootstrap_sha256", StringComparison.Ordinal),
                "container APK build must verify the exact bootstrap inside the final APK"
            );
        }
    );

    Run(
        "previous-exit diagnostics never block Activity onCreate",
        () =>
        {
            var repository = FindRepositoryRoot();
            var godotApp = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            Assert(
                godotApp.Contains("startPreviousExitReport();", StringComparison.Ordinal),
                "Activity startup must schedule previous-exit reporting"
            );
            Assert(
                godotApp.Contains(
                    "new Thread(this::reportPreviousProcessExits",
                    StringComparison.Ordinal
                ),
                "exit history and ANR trace reads must run off the UI thread"
            );
        }
    );

    Run(
        "startup recovery journal spans Android and launcher stages without private data",
        () =>
        {
            var repository = FindRepositoryRoot();
            var java = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            var bridge = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "StartupRecoveryBridge.cs"
                )
            );
            var patches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );

            Assert(
                java.Contains("STARTUP_RECOVERY_PREF", StringComparison.Ordinal)
                    && java.Contains(".commit();", StringComparison.Ordinal)
                    && java.Contains("reconcileStartupExit", StringComparison.Ordinal),
                "Android must durably reconcile process exits with startup attempts"
            );
            Assert(
                bridge.Contains("SHA256.HashData", StringComparison.Ordinal)
                    && !bridge.Contains("SavedAccountName", StringComparison.Ordinal)
                    && !bridge.Contains("SavedRefreshToken", StringComparison.Ordinal),
                "the persisted configuration identity must be opaque and credential-free"
            );
            foreach (
                var stage in new[]
                {
                    "launcher-ready",
                    "play-requested",
                    "cloud-sync",
                    "shader-warmup",
                    "game-startup",
                    "game-ready",
                }
            )
            {
                Assert(
                    patches.Contains($"\"{stage}\"", StringComparison.Ordinal),
                    $"launcher startup must journal {stage}"
                );
            }
            Assert(
                patches.IndexOf(
                    "StartupRecoveryBridge.RecordStage(\"game-startup\")",
                    StringComparison.Ordinal
                )
                    < patches.IndexOf(
                        "await (Task)gameStartup.Invoke(game, null);",
                        StringComparison.Ordinal
                    )
                    && patches.IndexOf(
                        "StartupRecoveryBridge.MarkHealthy(\"game-ready\")",
                        StringComparison.Ordinal
                    )
                        > patches.IndexOf(
                            "await (Task)gameStartup.Invoke(game, null);",
                            StringComparison.Ordinal
                        ),
                "game startup may be marked healthy only after the awaited startup succeeds"
            );
        }
    );

    Console.WriteLine("All stability tests passed.");
    return 0;
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void WriteInstall(string transactionRoot, string version)
{
    var game = GameInstallTransaction.GetActivePath(transactionRoot);
    Directory.CreateDirectory(game);
    WriteInstallFiles(game, version);
}

static void WriteInstallFiles(string gameDirectory, string version)
{
    Directory.CreateDirectory(gameDirectory);
    Directory.CreateDirectory(Path.Combine(gameDirectory, "data_sts2_windows_x86_64"));
    File.WriteAllText(Path.Combine(gameDirectory, "SlayTheSpire2.pck"), "GDPC" + version);
    File.WriteAllText(Path.Combine(gameDirectory, "data_sts2_windows_x86_64", "sts2.dll"), version);
}

static void ReplaceInstallFile(string gameDirectory, string relativePath, string content)
{
    var path = Path.Combine(gameDirectory, relativePath);
    var temporary = path + ".downloading";
    File.WriteAllText(temporary, content);
    File.Move(temporary, path, overwrite: true);
}

static (string Pck, string Assembly) ReadInstallTuple(string transactionRoot)
{
    var game = GameInstallTransaction.GetActivePath(transactionRoot);
    return (
        File.ReadAllText(Path.Combine(game, "SlayTheSpire2.pck"))[4..],
        File.ReadAllText(Path.Combine(game, "data_sts2_windows_x86_64", "sts2.dll"))
    );
}

static void AssertInstallTuple(string transactionRoot, string expected, string context)
{
    var (pck, assembly) = ReadInstallTuple(transactionRoot);
    Assert(pck == expected && assembly == expected, $"{context}: {pck}/{assembly}");
}

static void Reset(string path)
{
    if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    Directory.CreateDirectory(path);
}

static int CountOccurrences(string text, string value)
{
    int count = 0;
    int offset = 0;
    while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static Dictionary<string, string> CreateSteamInviteMetadataFixture(
    long expiresAtUnixSeconds,
    string nonce,
    string endpoints = "sts2lan:v1:192.168.10.8:33771"
)
{
    return new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SteamLobbyInviteMetadata.SchemaKey] = SteamLobbyInviteMetadata.SchemaV1,
        [SteamLobbyInviteMetadata.AppIdKey] = SteamLobbyInviteMetadata.GameAppId,
        [SteamLobbyInviteMetadata.TransportKey] = SteamLobbyInviteMetadata.EnetDirectTransport,
        [SteamLobbyInviteMetadata.LauncherBuildKey] = "0.4.8",
        [SteamLobbyInviteMetadata.GameBuildKey] = "public-107.1",
        [SteamLobbyInviteMetadata.EndpointsKey] = endpoints,
        [SteamLobbyInviteMetadata.ExpiresKey] = expiresAtUnixSeconds.ToString(),
        [SteamLobbyInviteMetadata.NonceKey] = nonce,
    };
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "src", "STS2Mobile", "STS2Mobile.csproj")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find repository root from current directory.");
}
