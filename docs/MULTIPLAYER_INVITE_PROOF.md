# Multiplayer invite proof

Status: **in progress — not release proof**

This document records evidence for `GOAL_MULTIPLAYER_INVITES.md`. A checked build
or a visible button is not sufficient to claim multiplayer invitation support.
Phase 0 distinct-account A→B→A and Phase 1 physical
host/join/disconnect/rejoin proof are green. Their final cumulative artifact
still needs the ordinary release gate. Phase 2 discovery and identity UI are
implemented and physically exercised, but no Steam friend invitation has been
sent because the two controlled accounts are not currently present in each
other's Steam friend list.

## Honest reachability boundary

`sts2lan:v1:<IPv4>:<port>` is a versioned representation of the launcher's
existing ENet direct endpoint. It provides no authentication, anonymity, NAT
traversal, matchmaking, Steam Datagram Relay, or unmodified-PC compatibility.
It is expected to work on the same LAN, a mutually reachable VPN, or a manually
configured reachable public endpoint.

| Layer | Signaling | Game transport | Current proof |
|---|---|---|---|
| Phase 1 copy/share | Android clipboard or user-selected Sharesheet target | Existing ENet direct connection | Parser/build, physical two-device game E2E, discovery, Copy, live Sharesheet, lifecycle, disconnect and reload/rejoin green |
| Phase 2 launcher invite | SteamKit friends-only lobby | Existing ENet direct connection | Handler/lobby/callback, identity status and picker implemented; build/device discovery green; real send/decline/accept blocked by missing friendship between the two controlled accounts |
| Phase 3 native Steam invite | Game/Steamworks lobby and overlay | SteamNetworkingSockets/SDR | Fail-closed blocker proven: bundled library is a stub; no supported Steam Frame/Lepton client/runtime |

## Phase 0 account-boundary evidence

Migration proof used signed production-type `0.4.7-account-invite-qa8`; the
cumulative security build is `0.4.7-account-invite-qa10`. Account names, Steam
IDs, tokens, Guard data, device identifiers, opaque slot values, screenshots,
and raw logs are excluded from this repository.

- Deterministic tests cover strict JWT subject parsing, malformed identity
  rejection, session generation/account/connection guards, atomic vault publish
  failure and retry, first-account legacy copy without overwrite, empty later
  slots, per-account preferences and pending-upload markers, external-backup
  adoption, and diagnostic redaction.
- A failed injected vault publish retained the old encrypted bytes; the retry
  published the new bytes atomically. Source integration rejects credential-file
  deletion and Android Keystore key deletion.
- On an already-migrated physical installation, entering Add account and pressing
  Cancel caused a clean process restart back to the unchanged current account.
- On a second physical installation, a pre-upgrade manual backup contained 211
  files and 8,212,998 bytes; shared mods contained 73 files and 79,698,469 bytes.
  The signed APK was installed with `adb install -r`: signing identity matched,
  the original first-install timestamp remained unchanged, and no uninstall or
  `pm clear` was used.
- Before that upgrade there were zero account-scoped external backup roots. The
  first QA8 cold start created exactly one and copied the one legacy manual set
  before any backup button was pressed. The legacy source and account copy each
  matched the pre-upgrade file count, byte count, relative-path/content digest;
  the source remained present. Shared mod and disabled-mod inventories also
  retained their exact count, bytes, and digest.
- The upgraded process remained alive with no matching fatal exception or ANR.
  A current-process log scan found zero occurrences of the authorized account
  name, password, Steam ID pattern, token pattern, or opaque account-slot path.
- Security review then made vault loading strict and fail-closed: unsupported
  versions, missing/invalid/duplicate accounts, invalid slots, and invalid active
  identity are deterministic errors. An existing encrypted file with an
  unavailable Keystore bridge cannot be overwritten; decryption only accepts an
  existing key and never creates a replacement. The obsolete key-deletion bridge
  was removed. Account-name, SteamID, refresh-token, Guard-data, and slot
  redaction all have negative tests.
- The pinned build passed 133 game-scoped member references and 77 patch/
  reflection rules with zero required misses. QA10 localization audited 1,241
  entries across 63 files; APK v2 signing and the single-signer check passed.
  QA10 installed over both retained devices without changing either first-install
  timestamp, decrypted the existing vault, and reached the account-aware launcher
  with zero fatal/ANR, account-name, token, or slot-path log hits. QA10 SHA-256:
  `0ea897c6d944f685e4ee6373b0bc5391320815e2f1e3988745af6407d7f5a50c`.

At that stage, the evidence proved only the legacy-to-first-account and cancel
paths; it did not yet replace the required distinct-account A→B→A test.

The distinct-account precondition was then satisfied on the retained tablet and
the production account picker showed exactly two stored rows without exposing
their names or IDs. Starting with account B active, the device completed B→A,
A→B, and B→A; the latter two transitions are the required A→B→A round trip.
Every transition drained/closed the old session, changed the active row only
after a clean process restart, required no password, retained both stored rows,
and returned to the account-aware launcher without an account-data-unavailable
state, fatal exception, or ANR.

Before and after every transition, the shared mod tree remained exactly 70
files and 79,005,161 bytes with aggregate digest
`88fbc7dced8ef153b7fd0ef611741f4833f98e1eb3eb6ecfb269fff29d87b88a`;
three disabled-mod files also remained. The account-scoped external backup tree
kept exactly two opaque roots with a sorted file-count distribution of `0,2695`:
the later account did not inherit the first account's backup set, and the first
account's set was unchanged on return. Per-transition log scans found zero
account-name, Steam-ID, token-like, or raw opaque-slot-path hits. A separate
add-account cancel exercise was stopped before any Steam authentication
connection and cleanly restored the prior account. Temporary captures containing
account UI were deleted outside the repository.

Together with the deterministic local-I/O redirection, non-overwrite migration,
stale-generation, atomic-vault, cloud-drain, and rollback tests above, this is
the retained-device Phase 0 A→B→A proof. It does not claim that a deliberately
failed real-account authentication was submitted; no such external attempt is
needed for the account-switch success invariant.

## Phase 3 native runtime boundary

The signed QA9 artifact's ARM64 `libsteam_api.so` was extracted locally and
inspected by exported symbol and instruction disassembly; the temporary binary
was then deleted. It is the repository's deliberate `src/stubs/steam_stub.c`,
not a Valve Steamworks runtime:

- `SteamAPI_Init` returns constant success;
- `SteamAPI_InitFlat` zeroes the error buffer and returns constant success;
- `SteamAPI_IsSteamRunning` returns constant false; and
- `SteamAPI_GetHSteamUser`/`SteamAPI_GetHSteamPipe` return dummy constant handles.

Therefore an in-app init probe against the current APK would be a false positive
and is explicitly rejected. Neither controlled ordinary Android device exposes a
Steam Frame/Lepton client package. Native overlay/matchmaking/socket calls remain
disabled, the existing ENet path remains active, and `SteamInitializer.Initialized`
is not forced. The minimum external blocker is a verified Valve Android ARM64
runtime plus its supported Steam Frame/Lepton client and matching partner/App ID
environment; no client IPC emulation or forged presence was attempted.

## Phase 2 deterministic boundary evidence

`SteamLobbyInviteMetadata` is a pure, session-independent gate; it does not yet
create a Steam lobby, subscribe to a callback, expose a friend picker, or send an
invitation. The focused workflow passes the following contract before any future
SteamKit callback may show a prompt or reach the existing ENet join path:

- Metadata has an exact eight-key schema and rejects missing, unknown,
  case-variant, oversized, or private-looking fields. It requires App ID
  `2868840`, the explicit `enet-direct` transport, exact compatible launcher and
  game-build tokens, one to eight canonical v1 direct endpoints, an expiry no
  more than ten minutes in the future, and a 128-bit CSPRNG-generated,
  22-character base64url-style nonce.
- Wrong App ID, unknown schema or transport, invalid/incompatible builds,
  malformed/expired/over-future expiry, invalid nonce, plain/non-canonical,
  unsafe, duplicate, or excessive endpoints all fail closed.
- Only a caller-classified friend may cross the boundary. Unknown, non-friend,
  and blocked senders fail before nonce consumption. The parsed object contains
  no sender Steam ID, account name, token, friend-list entry, save path, or mod
  list, and its string representation cannot reveal endpoints or nonce data.
- The in-memory replay guard is fixed-capacity and never evicts an unexpired
  nonce to admit attacker-controlled traffic. Duplicate and capacity-exhausted
  inputs fail closed, expired entries are reclaimed, and sixteen concurrent
  deliveries of the same valid callback deterministically admit exactly one.
- Source guards reject logging, Steam-ID, account-name, or refresh-token
  dependencies in this pure boundary. `bash tools/test-workflow.sh focused`
  passed after these tests were added.

This proves parser, sender-policy, resource-bound, and replay behavior only. It
does not prove a SteamKit lobby, callback lifecycle, account-session isolation,
or a real friend invitation; those require the distinct authorized second
account below.

## Phase 1 deterministic evidence

Verified on 2026-08-19 against the pinned game assembly and signed Android
release build `0.4.7-invite-p1-qa5`:

- C# invite parser round-trips the canonical v1 code and preserves plain
  `IPv4` and `IPv4:port` input with the existing default port.
- Empty, oversized, unknown-version, hostname, IPv6, abbreviated IPv4,
  hexadecimal/octal-style IPv4, leading-zero octets, loopback, unspecified,
  multicast, signed/non-ASCII port, zero port, and overflow port fixtures fail
  closed.
- Candidate selection removes loopback, unspecified, link-local, multicast,
  and duplicates, then orders private, CGNAT, and other direct addresses
  deterministically within the shared eight-choice budget.
- The independent Java Sharesheet boundary revalidates payload length, code
  length, schema, numeric endpoint, address safety, port range, uniqueness, and
  an eight-choice maximum.
- Android integration uses `Intent.ACTION_SEND` plus
  `Intent.createChooser`; it does not select a package or recipient. Clipboard
  copy remains an explicit button action.
- The invite dialog has single-owner lifecycle cleanup on replacement,
  Activity pause/destroy, ENet host restart, and host disconnect. An invite-hook
  exception falls through to the game's original Steam behavior.
- Korean, English, and Simplified Chinese localization audit passed 1,201
  classified source entries across 62 files, including all new visible invite
  text and negative fixtures.
- Stability contracts, Java lifecycle contracts, device harnesses, and
  Workshop regression tests passed.
- Compatibility audits passed 133 game-scoped member references and 76
  reflection/Harmony targets with zero required misses and zero optional
  degradations.
- The pinned Docker Android build, lint, DEX packaging, bootstrap asset check,
  APK v2 signing, and single-signer verification passed. APK SHA-256:
  `ad1e5c19da9840ef8b0a82055cb2163ad7dccb2a6a7b986d7338d8c2214e41c6`.

## Current device evidence

- QA5 updated the existing installation in place with the same signing identity
  and version code; the original first-install timestamp remained unchanged.
- A screen-off cold Activity start completed in 455 ms. The process remained
  alive after 20 seconds with no matching Java/.NET fatal exception or ANR, and
  Android remained in Dozing state throughout.

This startup observation proves only installability and background lifecycle
health. It does not prove that the launcher UI was ready in 517 ms or that the
invite flow works.

### Physical two-device LAN E2E

Two retained physical Android installations on the same LAN completed the
production flow on 2026-08-19. Device identifiers, live endpoints, player/account
names, screenshots, raw logs, and invite payloads remain outside this repository.

- A real ENet standard host started with a free slot. The production game-lobby
  Invite button appeared exactly once, while the second device's existing UDP
  discovery independently found the same host.
- The host chooser listed two validated local-interface candidates. The live
  Wi-Fi candidate was selected and the production Copy action completed. The
  exact canonical v1 wire value was entered into the join screen; it passed the
  same strict parser and joined the host. This proves code parsing and transport,
  but is not mislabeled as a cross-device clipboard paste.
- Both screens showed the same host and second player, both readied, and both
  entered the same modded game run with identical mod compatibility hash and
  two live player bars.
- HOME for five seconds and resume retained the same joiner PID and session. An
  explicit disconnect then exercised the game's authoritative recovery contract:
  the joiner was told that the host must reload, and the host displayed the
  disconnected player state. The host saved/exited and recreated from that save;
  the joiner rediscovered it, rejoined, and both devices again entered the same
  run.
- The first live chooser exposed a container/VPN interface before Wi-Fi. The
  implementation now prefers the address selected by the system default route
  while keeping every validated alternative. Signed QA9 physical proof showed
  the reachable Wi-Fi candidate selected first and the virtual interface second.
- QA9 Share opened the real Android system chooser with the selected live code.
  No recipient or target was selected; Back returned to the host and left no
  stale invite dialog. The temporary chooser capture was deleted because the
  system UI contained private contacts and a live endpoint.
- Both processes stayed alive with no matching fatal exception or ANR throughout
  the first join and disconnect/reload/rejoin sequence. QA9 APK SHA-256:
  `27fd70b92f6416ae605fa9ac51cd45005f8e08fc94e9ac6287feb903693c7be5`.
- The cumulative QA10 source/build removes live endpoint, client net ID, and
  discovered host-name/address values from launcher diagnostics; deterministic
  source guards reject their reintroduction.
- QA10 then exercised the real manual-join field with an unsupported-version
  code containing only a TEST-NET endpoint. The localized unsupported-version
  message appeared, the validated-join call count remained zero, the PID stayed
  unchanged, and no fatal exception or ANR occurred.
- The same retained pair then exercised the legacy plain `IPv4:port` path using
  the host device's live default-route address without recording that address.
  Exactly one validated direct join was initiated, and the joiner reached the
  same two-player character-selection state as the host with PID continuity and
  no fatal exception or ANR. This is physical regression proof that the new
  versioned-code parser did not replace the existing manual endpoint contract.
- With the production host chooser open, physical rotation from one landscape
  orientation to the opposite orientation and back retained the same PID. Each
  hierarchy contained exactly one chooser title and one Share, Copy, and Cancel
  action; no duplicate dialog or action was created.
- Finally, the host process was force-stopped while that chooser was open. The
  old PID disappeared and the observed LAN-port socket row count dropped from
  one to zero. A fresh Activity process reached the account-aware launcher with
  zero stale chooser instances, zero LAN-port socket rows, no account-data
  unavailable state, and no fatal exception or ANR. No uninstall, data clear,
  save choice, recipient selection, or account mutation was performed.

### Android chooser boundary on a controlled AVD

A signed `0.4.7-invite-p1-debug-qa3` release-type build used the repository's
existing `-debug` capability gate to open the production chooser with fixed
private-range fixture endpoints. The trigger is inert in ordinary version
names. The AVD's Vulkan backend repeatedly returned `QueuePresentKHR` error 5,
so this isolated Android UI run explicitly used the existing one-session OpenGL
compatibility override. This does not change the product default and is not
evidence of Vulkan health on that AVD.

- English, Korean, and Simplified Chinese each rendered the localized title,
  Copy, Cancel, and Share actions plus two selectable endpoint rows. UI
  hierarchy bounds confirmed the rows even when a resized screenshot preview
  temporarily omitted them.
- Selecting the second row and pressing Copy closed the chooser and produced
  the localized system toast after `ClipboardManager.setPrimaryClip` returned.
- Pressing Share focused Android's `ChooserActivityLauncher` and displayed a
  text Sharesheet preview. No recipient or target application was selected;
  Back returned to the launcher and the invite dialog did not reappear.
- HOME/resume kept the same launcher PID and left no invite dialog. Force-stop
  plus relaunch without the debug trigger also left no stale dialog.
- A controlled landscape rotation from WindowManager rotation 1 to 3 kept the
  same PID; the post-rotation hierarchy contained exactly one invite title and
  two choices.

The debug APK SHA-256 was
`c21606b341a5bbf411db6ed4ff41725ffc5de8d77f1017d254d480b9b449b184`.
Screenshots and raw hierarchy captures remain outside the repository. Fixture
endpoints are not evidence about a real device's network inventory.

## SteamKit friend-picker and two-device direct-invite proof

### QA15/QA16 Steam friend-picker, identity and account-path evidence

- The friend picker now enriches its in-memory rows from Steam's nickname and
  per-App gameplay-info services. Its pure policy and production source guards
  prove case-insensitive persona/nickname search, offline-hidden-by-default with
  an explicit recovery toggle, nickname-first then currently-playing,
  recently-played, online rank, nickname-primary/persona-secondary display, and
  truthful “recently played” wording. No friend metadata is persisted or logged.
- A physical receiver held the SteamKit invite bridge open while the host opened
  the picker. Publishing Online persona only for the bridge lifetime made real
  online friends visible with the offline toggle still disabled; the search and
  offline-toggle UI both behaved live. The designated receiver could not be
  identified by its login name because Steam persona/nickname is a separate
  identity and no unique matching display name was supplied. No friend was
  selected and no invitation was sent.
- The second account's first game start exposed an independent atomic-write bug:
  `GodotFileIo.RenameFile` fed an already account-scoped temporary path through
  `GetFullPath` a second time, nesting the logical root and throwing “source does
  not exist.” The background task logged and swallowed that exception, leaving
  the native progress overlay apparently hung. Account path rewriting is now
  idempotent for direct and doubled resolved paths. A deterministic regression
  covers the exact `settings.save.tmp` shape, and QA14/QA15 physically completed
  first-settings publication and reached the modded main menu without deleting
  the retained temporary file or any user data.
- QA15 passed the pinned Docker release pipeline, 133 game-scoped member
  references, 77 patch/reflection rules with zero failures/degradations, all
  stability/localization/Java/device harnesses, Workshop regressions, APK v2
  signing, and the one-signer check. APK SHA-256:
  `d08be7f036872b7c82259bca852d6875e96f030dbb46a1f2d93e30bc256f9476`.
- Both retained installations were upgraded in place and shared mod count/size
  remained unchanged. Temporary friend-list, account, endpoint, and log captures
  were deleted after inspection. The original screen-off timeouts and the
  receiver's original auto-sync preference were restored.
- QA16 added a join-listener identity chip backed only by Steam's authenticated
  persona cache. It waits on the real persona callback, sanitizes and bounds the
  name, never exposes the login name or numeric ID, never logs or persists the
  value, ignores input, and is owned by the same generation/surface teardown as
  the listener. Deterministic source guards, the focused workflow, and an ARM64
  compile passed with zero warnings or errors.
- A signed production-type QA16 APK was built in the pinned ARM64 toolchain from
  the already verified Godot/FMOD dependency bytes, passed 133 game-scoped
  member references, all 77 required patch/reflection rules, Workshop sync,
  deterministic bootstrap, FMOD DEX, APK v2 and one-signer verification, then
  upgraded both retained installations without clearing data. APK SHA-256:
  `50691499e5616c709e5cb92e5d822404ba9a3771dc11d6e12fb74a91b9298671`.
- On the physical receiver, the join page displayed its current sanitized
  persona while the listener was active. On the physical sender, the picker
  defaulted to online-only; enabling the explicit offline toggle and scanning
  the full bounded 200-friend capacity produced no exact match for that persona.
  No row was selected and no invitation was sent. This isolates the remaining
  external condition to an absent or not-yet-effective Steam friendship, rather
  than login-name ambiguity, picker filtering, or guessed recipient selection.
- All identity/list/endpoint screenshots and local OCR data were deleted after
  the check. The receiver's temporarily disabled auto-sync was restored to on,
  both app sessions were ended, USB stay-awake was disabled, the original
  60/120-second screen timeouts were restored, and both devices were left
  asleep.

### QA17–QA20 search and lifecycle regressions

- QA17 bound the listener status and incoming modal to the same surface
  generation and teardown. Its signed APK SHA-256 was
  `631ec2534c383ab779bfe5aa70820cf138e5d89f6e579308f82b2c3294851f74`.
- QA18 removed the 200-row pre-search truncation. Steam relationships are now
  enumerated to a bounded 5,000-entry searchable model, search/ranking runs on
  that complete model, and only the first 200 matches become Godot controls.
  A deterministic fixture places the only match after row 200. Its signed APK
  SHA-256 was
  `8cdab54ce4780822126ed4e43a0655c3ea30f5908cdec7005fab9b09dc120310`.
- Live QA18 proved that the designated controlled receiver was discoverable in
  the complete list when the explicit offline toggle was enabled. QA19 then
  made a non-empty search query temporarily include offline matches while an
  empty query still hides them by default. Deterministic tests cover all three
  states: empty/default-hidden, active-search-visible, and explicit-toggle-
  visible.
- QA19 passed the focused workflow, 133 game-scoped member references, all 77
  required patch/reflection rules, Workshop regressions, APK v2 signing, and the
  one-signer check. It upgraded both retained installations without clearing
  data. APK SHA-256:
  `89654f811ae027415566507348fd014c53b3c6b0ebe0cdcc93bb88940f7caf13`.
- Post-QA19 review found a client lifecycle race: the shared game-service
  disconnect postfix called the host cleanup entry point for both host and
  client disconnects. A late client disconnect callback could therefore reset a
  freshly reopened Join listener. The cleanup entry point now resets only while
  the invite surface is actually in host mode, with a deterministic source
  regression. The same review registered newly returned authentication secrets
  with the redactor before ownership verification and made sensitive result and
  credential records fail-closed under implicit `ToString()`.
- QA20 passed the focused workflow, 133 game-scoped member references, all 77
  required patch/reflection rules, Workshop regressions, deterministic
  bootstrap, FMOD DEX checks, APK v2 signing, and the one-signer check. It
  upgraded both retained installations without clearing data. APK SHA-256:
  `beea9ae8894008944ae9ef02eea24625db713caa19fe3a660cf751320d764246`.
- After the metadata-only release bump to v0.4.8 (versionCode 345), the exact
  final source tree repeated that complete pinned build successfully. The signed
  one-signer/v2 APK upgraded both retained installations without clearing data,
  started cleanly on both with no current-process fatal signal, and was then
  force-stopped with both devices returned asleep. APK SHA-256:
  `909ffe942f9837a3b308aaed9cdab80febab9369b1fa816fe1b6e91ae2b75c76`.

### Real launcher-to-launcher invite E2E

Verified on 2026-08-19 using the signed QA19 production-type build, two distinct
authorized accounts, and two retained physical Android installations on the same
reachable LAN. No account name, persona, Steam ID, endpoint, friend-list row, or
invite payload is recorded here.

1. The receiver opened `Multiplayer → Join`; the listener status showed only
   its sanitized authenticated persona and remained input-transparent.
2. On the host, the offline toggle remained disabled. Entering a Simplified
   Chinese prefix matched exactly the designated offline receiver, proving both
   the QA18 search-before-render fix and QA19 active-search override on the real
   list. No other friend was selected.
3. The first explicit send produced exactly one launcher-direct prompt on the
   receiver with two bounded reachable endpoint choices. Decline removed the
   prompt, left the receiver on Join, and left the host at one player.
4. The second explicit send produced one prompt. Accept first validated the
   Steam lobby metadata/build/expiry and then joined the selected ENet endpoint.
   Both screens showed the same lobby with two players.
5. The receiver disconnected, explicitly reopened Join, accepted a fresh invite,
   and returned to the same two-player lobby. This proves disconnect plus
   re-invite/rejoin rather than relying on a retained socket.
6. After returning to Join, a 10-second HOME interval preserved the listener; a
   newly sent invite arrived and could be declined. For the expiry path, the
   receiver went HOME, the host sent while it was backgrounded, and the receiver
   stayed away for 38 seconds. Foregrounding showed a newly active listener and
   no old prompt, so the pre-expiry callback could not be accepted after the
30-second generation boundary.

The signed QA20 build then repeated the lifecycle path that exposed the review
race. The receiver accepted a launcher-direct invite and joined the two-player
lobby, disconnected back to the already-open Join surface, and did not leave or
reopen Join. The host immediately selected the same controlled receiver and sent
a fresh invitation. The receiver received the new prompt on that existing Join
surface and declined it successfully. This is physical proof that a late client
disconnect callback no longer tears down the new listener.

Both package-PID and system log scans reported zero launcher fatal exceptions,
ANRs, native fatal signals, or process crashes. The same scan reported zero
matches for either controlled identity, a raw Steam ID, a live ENet endpoint, or
token/authorization material. The receiver's original auto-sync-on preference,
the original 60/120-second screen timeouts, and USB stay-awake-off state were
restored; both apps were force-stopped and both devices left asleep. All 64
temporary screenshots, UI hierarchies, and raw log captures were deleted.
The QA20 current-process scan likewise reported zero fatal/ANR/native events and
zero token, authorization, raw Steam-ID, or endpoint patterns. Auto-sync,
timeouts, stay-awake, process, and screen state were restored again, and all 33
temporary QA20 captures were deleted.

## Native Steamworks boundary

Phase 3 remains fail-closed rather than pretending that the bundled stub is a
Valve runtime. Ordinary Android has no supported Steam Frame/Lepton client or
verified Valve ARM64 runtime on either test device. A native overlay/SDR experiment
therefore stops at the reproducible missing-runtime boundary; the completed
SteamKit feature is labeled launcher direct invite and continues to use ENet.
Native Steamworks can only be re-opened when a supported target, matched real
runtime, and partner configuration are supplied.
