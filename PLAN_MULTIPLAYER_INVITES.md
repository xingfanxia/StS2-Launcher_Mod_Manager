# Multiplayer invite implementation plan

## Research result

The launcher has three materially different invitation levels. They must not be
presented as one feature because their reachability and trust boundaries differ.

| Level | What users get | Transport | Feasibility on ordinary Android |
|---|---|---|---|
| 1. Share/paste invite | Share a versioned address or paste it into JOIN | Existing ENet direct connection | High; same LAN/VPN immediately, public internet only with a reachable address |
| 2. Steam lobby bridge | Pick a Steam friend, send/receive a Steam lobby invite, then join with launcher ENet metadata | SteamKit lobby signaling + ENet | Plausible for launcher-to-launcher clients; requires two-account proof |
| 3. Original Steam transport | Steam overlay invite and unmodified PC interoperability through SteamNetworkingSockets/SDR | Native Steamworks client + Steam relay | Conditional on an official Steam client environment such as Steam Frame/Lepton; not established on an ordinary phone |

Verified repository facts:

- `LanMultiplayerPatcher` already hosts ENet on UDP `33771`, discovers hosts on
  UDP `33770`, accepts manual `IP[:port]`, and calls the game's existing
  `JoinGameAsync` with `ENetClientConnectionInitializer`.
- Decompiled pinned game code chooses `StartSteamHost` only when
  `SteamInitializer.Initialized`; otherwise it chooses `StartENetHost(33771, 4)`.
  The existing mobile patch operates on that ENet path.
- The game's `NInvitePlayersButton` delegates to
  `PlatformUtil.OpenInviteDialog`, which only supports a native Steam lobby and
  Steam overlay. It is therefore hidden for the current `PlatformType.None`
  mobile host.
- The APK contains an Android ARM64 `libsteam_api.so`, but Valve added Android
  ARM64 Steamworks libraries for Steam Frame's Lepton environment. The connected
  ordinary Android phone has no Steam/Lepton package. Library presence alone is
  not evidence that `SteamAPI.InitEx` can attach to a local Steam client.
- The launcher's SteamKit 3.4.0 package exposes `SteamFriends` and
  `SteamMatchmaking`, including friend enumeration, `CreateLobby`,
  `InviteToLobby`, lobby metadata, `JoinLobby`, and incoming
  `SteamFriends.ChatInviteCallback`. `SteamConnection` already owns the callback
  pump and a reference-counted idle-timeout suspension mechanism.
- SteamKit lobby signaling does not implement the game's native
  SteamNetworkingSockets host/client transport. A SteamKit-created lobby that
  carries an ENet endpoint is therefore launcher-to-launcher unless a real
  native Steamworks transport is separately proven.

Primary references checked on 2026-08-19:

- [Valve Steam Matchmaking & Lobbies](https://partner.steamgames.com/doc/features/multiplayer/matchmaking)
- [Valve ISteamFriends rich presence and invites](https://partner.steamgames.com/doc/api/ISteamFriends)
- [Valve Steam Frame custom-engine Android support](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom)
- [Valve Lepton/ADB architecture](https://partner.steamgames.com/doc/steamhardware/steamframe/adb_lepton)
- [SteamKit SteamMatchmaking source](https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Steam/Handlers/SteamMatchmaking/SteamMatchmaking.cs)
- [SteamKit SteamFriends source](https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Steam/Handlers/SteamFriends/SteamFriends.cs)

## Phase 1 — low-conflict share/paste invitation

1. Add a pure `LanInviteCode` contract with a version, numeric IP endpoint,
   port, and optional short display label. Continue accepting the existing plain
   `IP[:port]` form.
2. Reuse the existing multiplayer lobby's invite button through narrow Harmony
   hooks. For an ENet host, show a mobile-specific Share/Copy action instead of
   changing `PlatformUtil` globally.
3. Add an Android Sharesheet bridge using `ACTION_SEND`. Sharing opens a chooser;
   the launcher never silently selects a recipient or sends a message.
4. Show every viable local IPv4 endpoint when the device has multiple networks,
   clearly labeling this as LAN/VPN/direct-IP. Never claim that an encoded room
   code adds NAT traversal.
5. Parse pasted codes at the existing JOIN field, reject malformed/oversized
   input, and preserve current direct-IP behavior.
6. After share/paste proof passes, optionally add a custom deep link. Treat it as
   untrusted external input: strict scheme/version/host/port parsing, no
   auto-join, and an explicit confirmation showing the destination.
7. Localize all new UI and errors in Korean, English, and Simplified Chinese.

Acceptance for Phase 1:

- Two launcher instances on the same LAN can host, share/copy, paste, confirm,
  join, disconnect, and rejoin.
- A device with multiple interfaces never shares loopback, unspecified, or an
  unrelated malformed endpoint.
- Invalid, traversal-like, oversized, unsupported-version, and replayed deep
  links cannot crash the app or auto-connect.
- Existing UDP discovery and manual `IP[:port]` joining remain unchanged.

## Phase 2 — SteamKit lobby invitation bridge

Start only after Phase 1 is green.

1. Extend `SteamConnection` with owned `SteamFriends` and `SteamMatchmaking`
   handlers and bounded events. Hold the existing connection open only while a
   multiplayer host/join surface needs invite callbacks.
2. Create a friends-only Steam lobby for App ID `2868840` when an ENet host asks
   to use Steam invites. Store a closed, versioned metadata schema containing
   transport kind, compatible launcher/game build, bounded endpoint candidates,
   and an expiry/nonce. Store no account name, refresh token, path, mod list, or
   save data.
3. Build an in-game friend picker from the SteamKit friend cache. Keep Steam IDs
   in memory, rate-limit invitations, and send only after explicit selection.
4. Use `SteamMatchmaking.InviteToLobby`. Handle the matching incoming
   `ChatInviteCallback`, fetch and validate lobby metadata, show inviter and
   endpoint confirmation, then route acceptance through the same ENet join
   function as Phase 1.
5. Leave/delete the bridge lobby and release the connection lease on disconnect,
   screen teardown, logout, app background timeout, or failed host startup.
6. Label compatibility honestly: `Launcher direct invite`, not Steam relay, until
   unmodified desktop interoperability is proven.

Acceptance for Phase 2 requires two designated Steam test accounts:

- Friend A can create a bridge lobby and invite friend B without exposing either
  credential or logging raw Steam IDs.
- Friend B receives exactly one bounded prompt, can decline without state change,
  and can accept into the matching ENet host on a reachable network.
- Wrong App ID, unknown schema, expired nonce, malformed endpoint, duplicate
  invite, blocked/non-friend sender, disconnect, and callback-after-teardown all
  fail closed.
- The ordinary cloud/Workshop connection can still idle, drain, reconnect, and
  logout without invite callbacks leaking across sessions.

## Phase 3 — original Steamworks invite/relay feasibility gate

1. Add a diagnostic-only probe that records the sanitized `SteamAPI.InitEx`
   result and availability of Steam overlay, matchmaking, and networking sockets.
   Do not force `SteamInitializer.Initialized` or call an interface after init
   failure.
2. Run it on an ordinary Android phone. If a Steam Frame/Lepton target becomes
   available, run the same signed build there and compare.
3. If native initialization succeeds, prefer the game's existing
   `StartSteamHost`, `NInvitePlayersButton`, `SteamJoinCallbackHandler`, and
   SteamNetworkingSockets path with only compatibility/lifecycle patches.
4. If it fails on ordinary Android because no supported local Steam client is
   present, record that boundary. Do not emulate Steam client IPC, forge game
   presence, or advertise PC/SDR interoperability.
5. Treat a custom internet relay, Valve partner configuration change, or
   Steam-client emulation as a separate architecture/security decision requiring
   explicit authority and operational ownership.

## Upstream-conflict strategy

- Keep address parsing, Steam lobby metadata, and invitation lifecycle in new
  focused classes with deterministic tests.
- Keep `LanMultiplayerPatcher` changes to narrow hook registration and calls into
  those classes; do not copy or rewrite the game's lobby UI wholesale.
- Reuse `SteamConnection`, its callback pump, idle suspension, and the existing
  ENet join method.
- Add every new reflection target to `patch-target-audit` for both supported game
  branches.
- Preserve LAN discovery, default Vulkan, launcher auth, cloud, Workshop, mod,
  save, and offline behavior.

## Release sequence

1. Land Phase 1 and verify it independently before starting Phase 2.
2. Land Phase 2 only after two-account end-to-end proof; otherwise deliver the
   prototype and exact blocker without enabling it by default.
3. Keep Phase 3 diagnostic/conditional. Never delay a stable direct-invite
   release merely to claim unsupported native Steam relay behavior.
