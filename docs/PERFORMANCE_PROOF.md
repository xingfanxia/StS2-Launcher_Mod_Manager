# Performance and observability proof

Status: complete on the ARM64 reference device. The device-specific limits and
external residuals are recorded below; this is not a claim that every ROM, GPU,
game update, or third-party mod is defect-free.

## Gate ledger

| Gate | Current evidence | Status |
|---|---|---|
| Canonical frame metric | Debug-only 100 ms stall detected by Godot monotonic frame intervals; control did not detect it; `gfxinfo` rejected | Passed for focused metric validation |
| Real game spike ownership | First-hand Canvas pipeline delta, main-thread Perfetto evidence, synchronous deck-grid call path, pause cache, and stateful first-map-open path identified; three deterministic same-device deck-cycle pairs and three supported-mod pairs passed | Passed for launcher-owned work; one anonymous third-party group remains external |
| Standardized mod-jank triage | One command runs session-only full/Safe/anonymous-partition main-menu captures, sanitizes results, and distinguishes valid samples from mod initialization failures | Passed on the reference device; prescreen is not combat proof |
| Standardized startup A/B | One command validates package/full signer set, refuses locked devices, alternates baseline/candidate upgrade installs, and restores candidate on every post-install exit | Host contracts and two independent 30-pair final device series pass |
| First-hand interactive jank | Real first draw is completed behind an explicit localized loading cover; three Safe Mode and three supported-mod 120 s pairs cover the post-reveal path | Passed on the reference device |
| Deck/pause UI jank | Exact real deck screen reuse and cached pause-menu construction implemented; three deterministic 120-second pairs reduced >2x frames by 25% without p95/p99 or RSS regression | Passed on the reference device |
| Returning-user startup | Goal baseline→final interleaved 30-pair A/B: total p50 `38.061 → 28.797 s` (-24.34%), p95 `38.695 → 29.702 s` (-23.24%); PLAY→ready p50 improved 30.68% | Passed, 60/60 same-PID `game-ready` with zero classified errors |
| First-run/warmup | Under injected Android memory pressure, warmup deferred in 3 ms, restarted intentionally, and reached the game through the on-demand path; the following 300 s gameplay sample stayed within the 10% long-frame-density guardrail | Passed on the reference device; broader low-memory-device coverage remains |
| Startup stage catalog | Independent 16-stage catalog plus bounded native/managed monotonic timelines cover process, install/cache/assembly, Godot, launcher/user-wait, cloud/warmup/settings, game, and anonymous mod boundaries | Contracts plus real normal/watchdog/renderer-recovery/managed-recovery paths pass |
| Truthful KR/EN progress | Native splash hands off to an Android UI-thread KR/EN stage surface; PLAY-to-game overlay shows current/recent stages plus separate stage/total elapsed clocks, watchdog copy, and only real work units | KR/EN startup, watchdog, both recovery dialogs, rotation, clipping, liveness, and rendered-frame handoff pass; final state restored to EN |
| Sparse telemetry privacy/overhead | Both schemas are bounded and numeric-only; Java fixtures reject path/account/control/oversized fields; the device frame harness strips raw log/PID/serial/path data | Passed: 3x120 s persistence-on/off A/B stayed within 3% for frame percentiles, CPU, and RSS and created no long-frame cluster |
| Stability and compatibility | Focused tests, compatibility audits, Java/Gradle/D8, FMOD DEX checks, signed build, upgrade install, warmup, Safe Mode, supported-mod, HOME/resume, rotation, offline/reconnect, and both final startup matrices pass | Passed on the reference device with no new Crash/ANR/LMK/black surface classification |
| Repository hygiene | Raw evidence remains outside git and temporary screenshots were deleted; final diff/private-artifact/upstream audits are recorded below | Passed |

## Current runtime changes

- Independent frame boundaries between otherwise synchronous run/map/combat
  setup operations, preserving the original operation order and side effects.
- Explicit first-hand rendering cover that starts the real turn, waits for the
  real visible hand and Canvas pipeline count to stabilize, then reveals combat.
- Reuse of one live, run-tree-owned deck-view screen for the same player. Weak
  references index the retained tree object without adding a static strong
  cross-run owner; player changes clear and retire the old screen. The cache
  also weakly tracks card-upgrade notifications: a hidden upgrade retires the
  stale screen immediately, a visible mutation returns to the upstream free
  path on close, and tree teardown symmetrically detaches every subscription.
- Construction of the upstream-cached pause menu during the covered loading
  stage, without opening it or changing run state.
- Debug-only bounded frame percentile/spike capture and method attribution.
  The production path installs no transition-attribution Harmony patches; the
  method wrappers exist only in an explicitly armed debug game-capture process.
  One current debug APK exposes paired `game-baseline-120` and `game-120`
  modes. Baseline mode skips only the gameplay frame-pacing/covered-first-hand
  and deck-cache changes; both modes retain the same build, instrumentation,
  renderer, data, and process launch path. The composable
  `game-baseline-safe-120`/`game-safe-120` pair holds that same single variable
  while both peers use session-only no-mod Safe Mode.
- The QA-only external-storage trigger watcher is also armed only by an Android
  debug capture intent. Ordinary production gameplay starts no polling watcher;
  real mod-exception attribution remains enabled.
- Independent native and managed startup timelines with stable stage ids,
  explicit owners/terminals/watchdogs, separate `user-wait`, bounded rings, and
  app-private numeric-only summaries.
- Continuous native splash/overlay handoff and a PLAY-to-game-ready KR/EN
  overlay. Unknown work stays indeterminate; determinate values are direct
  cloud item or shader source `done/total` values.
- Once settings/game/mod initialization enters the Godot engine-thread span,
  the same bounded tracker snapshot is mirrored to Android's UI thread. Android
  advances elapsed time from its monotonic clock while Godot cannot draw; the
  surface is released only after a subsequent Godot game frame is available.
- While that full-screen progress surface owns the PLAY→game-ready interval,
  the game's logo animation is completely obscured but still consumes about
  nine seconds. A scoped Harmony prefix now requests the game's existing
  `skipLogo` path only during this covered startup. It still loads the main-menu
  essentials and never reads or mutates the persisted `SkipIntroLogo` setting;
  patch-target drift fails open to the original, slower behavior.
- The Android previous-exit query now begins immediately after startup journaling
  and overlaps assembly/Godot bootstrap. A small process-local gate allows the
  query and `GodotActivity.onCreate` to finish in either order, but grants exactly
  one finalization only after both are ready. This retains previous-exit and
  renderer-recovery behavior without serializing its I/O before PLAY.
- The automation and telemetry boundary is now `Launcher ready for PLAY`, emitted
  only after recovery resolution and immediately before the explicit input wait.
  `play-accepted` supplies the other boundary; initial launcher rendering and the
  host's first tap are no longer mislabeled as readiness or acceptance.
- A bounded one-shot debug intent can hold the real `game-settings` stage for
  at most 20 seconds. It exists only to cross that stage's 15-second watchdog
  threshold deterministically while the same KR/EN Android-thread surface is
  visible. Release builds return zero, and no marker, setting, or persistent
  state is written.

Local decompilation of the matched game assembly was used as a source-parity
check. The patched `NGame.LoadRun` and `CombatRoom.StartCombat` paths preserve
every original call, mutation, conditional, await, and relative side-effect
order; the only additions are frame yields between independent main-thread
steps and the covered real first-turn handoff. Deck-screen reuse preserves the
original open sound and delegates pause/unpause, overlay visibility, tree
ownership, and capstone state to the unchanged `NCapstoneContainer` methods.
Its exact close replacement preserves `Visible = false` and the upstream top-bar
toggle while omitting only the base `QueueFreeSafely` call for the cached screen.
The upstream pile event would otherwise rebuild every card holder while that
screen is hidden. The patch invalidates and queues the cached screen for deletion
before that hidden handler runs; the next open takes the original construction
path with current cards. Card upgrades do not emit that pile event, so the patch
separately observes each current card's existing `Upgraded` notification without
rooting the card. The device gate explicitly covers obtain/remove/upgrade,
next-open node reconstruction, rollback, frame spikes, and RSS.
The first map was deliberately not pre-opened: its real `Open` mutates combat
pause, hotkey, active-screen, signal, audio, and one-time animation state. That
approximately `80 ms` residual stays visible in the device matrix rather than
being hidden by a behavior-changing synthetic warmup.

## Latest verification

- Focused stability test executable: passed.
- Patcher build: passed with zero warnings and zero errors; current MemberRef
  audit covered 132 game-scoped references and the patch-target audit covered
  66 rules with no missing or degraded target, including the private
  `NGame.LaunchMainMenu(bool)` optimization target.
- Launcher localization audit: passed across 751 Hangul-bearing source entries,
  including all 32 KR/EN stage-catalog fields and 12 native pairs.
- Native startup timeline tests: passed normal, skipped, degraded, invalid
  transition/timestamp, bounded ring, numeric codec, and private-field rejection
  fixtures.
- The device-performance harness passed on the host and pinned build image. Its
  fixtures prove action gating, pre-mutation battery and thermal rejection,
  release-build rejection, exact summary/spike parsing, numeric RSS/thermal
  context, paired scenario ordering, capture-process cleanup, and that injected
  raw private fields cannot reach the output. A live 1% device
  preflight exited `5` before force-stop, launch, input, or log clearing.
- The final source passed the full pinned Docker APK pipeline: stability,
  localization, compatibility audits, Java/Gradle/D8, FMOD DEX, workshop sync,
  device-stability and device-performance harness contracts, APK signing, and
  signature verification. The final signed `0.4.2` APK SHA-256 is
  `3bd7c050ff15f3e2df7bfa9155695640e7d8bcc4a5fb8d6dd32389147767aa4e`.
  It reports package `com.game.sts2launcher.modmanager`, version code `339`,
  and the upgrade-compatible signer SHA-256
  `9d99de1f064d9ec03fa55ced4e49b7b20991bb68b082bd388ff42d3b4a6f4c94`.
  It upgrade-installed over the prior signer without clearing app data and its
  embedded runtime DLL is byte-identical to the release-configured A/B
  candidate (`03049d08b5d110dccd4d924c50981d3718f299491085dc58ac1f754c7b7f9e17`).
- The interleaved startup runner has deterministic host proof for odd/even arm
  order, common UI→acknowledged-PLAY timing, thermal classification, explicit
  device/install authorization, package and complete signer equality, locked
  device refusal before mutation, and candidate restoration after an injected
  mid-capture failure. It never uninstalls or clears app data and writes only
  sanitized numeric matrices. Its completed-run aggregator emits nearest-rank
  p50/p95/p99/max for common boundaries and every available stage, plus explicit
  10% p50/5% p95 basis-point gates. Missing legacy stage telemetry remains
  `samples=0`; it is never reconstructed from guessed timestamps. Each arm now
  also rechecks thermal, manual unlock, and battery before installation.
  A resumable prefix must contain only the exact alternating sequence of
  `pass/game-ready` rows; an injected failed capture is rejected before any
  install, force-stop, launch, or log clear, so resume cannot erase bad evidence.
- Focused startup-observability reproduction before the fix showed real tracker
  movement through game and anonymous mod stages while two screen samples five
  seconds apart remained frozen at `Waiting for PLAY · 9s`; PLAY-to-ready took
  about 22.5 seconds. With the installed candidate, samples showed `Loading
  mods`, then `Starting game`, with elapsed values increasing through 1, 6, 12,
  15, 16, and 17 seconds. The native surface remained through `game-ready`, was
  removed after the next Godot frame, and handed directly to the rendered game
  without exposing the stale PLAY surface. Raw logs/screenshots remain outside
  the repository.
- A final-APK visual follow-up adds separate current-stage and PLAY-to-now
  elapsed clocks, so repeated anonymous mod sub-stages cannot make the display
  look reset or frozen. EN samples showed `Loading mods · Stage 1s · Total 3s`,
  `Loading mods · Stage 2s · Total 10s`, then `Starting game · Stage 6s · Total
  18s`; total time increased monotonically across the stage change and the same
  process reached `game-ready`. The current APK then passed a `35.828 s`
  cold-start smoke with zero fatal/ANR/LMK/surface classifications. A temporary
  launcher screenshot containing account UI was removed immediately; remaining
  startup-cover samples stay outside git.
- A subsequent complaint-driven reproduction on that exact installed APK
  sampled the same PID at `Starting game · Stage 4s · Total 15s`, then `Stage
  10s · Total 22s`, then `Stage 19s · Total 30s`, before the rendered game
  replaced the overlay. This directly verifies that Android's UI thread keeps
  the visible clock moving while Godot's engine thread is occupied. All
  temporary samples were deleted after inspection because the rendered game
  could expose user or third-party content.
- Before overlapping previous-exit diagnostics, the first compact 30-run series
  was 30/30 stable but exposed a managed `user-wait` stage p50 of `3.284 s` and
  automatic total p50 of `38.929 s`, only 5.638% better than the historical
  baseline. The query started after `GodotActivity.onCreate`, so its bounded
  wait did not overlap the approximately 2.3-second Godot bootstrap. A no-mod
  smoke also established a `20.695 s` total lower bound and showed that anonymous
  third-party mod work accounts for most of the remaining full-start cost.
- With the exact-once overlap gate and corrected PLAY boundary, a fresh 30-run
  final series reached `game-ready` 30/30 with same-process continuity and zero
  fatal, ANR, LMK, or surface-error classifications. Nearest-rank results were:
  process→PLAY-ready p50/p95/p99/max `4.153/4.208/4.277/4.277 s`, real user-wait
  `0.158/0.166/0.169/0.169 s`, PLAY-accepted→game-ready
  `31.657/32.557/32.901/32.901 s`, and automatic total excluding user-wait
  `35.821/36.741/37.082/37.082 s`. Relative to the historical automatic p50
  `41.255 s` and p95 `43.657 s`, this is a 13.172% p50 improvement and a 15.842%
  p95 improvement. The thermal pairs were one `0→0`, one `0→1`, one `1→1`, one
  `1→2`, and 26 `2→2`; no sample exceeded the admitted 0–2 band.
  This series used the immediately preceding signed APK
  (`be3113fca1eb08da7df0e04baa0eb9916c6c07f63873b166a3819ed9d391fc9c`);
  it is retained as historical evidence and is superseded by the two
  interleaved final matrices below.
- The same final series' stage p50 values identify the remaining critical path:
  Android `0.013 s`, assembly sync `0.134 s`, Godot bootstrap `2.562 s`, launcher
  creation `0.777 s`, tracker user-wait `0.283 s`, game settings `0.062 s`, game
  startup `19.569 s`, anonymous mod discovery `0.060 s`, and anonymous mod load
  `11.913 s`. The previous-exit overlap reduced tracker user-wait by about 91%
  without bypassing the crash-loop or renderer-recovery checks. Sanitized TSV
  evidence remains outside git.
- The exact-current signed APK
  (`3184492583a407260e2b19437c4e86736d5c4ac433c02bcffd15dc3fedc4445a`)
  then produced 30 thermal-valid samples by retaining 29 valid rows from the
  primary matrix and adding one cooled replacement. One additional launch that
  reached `game-ready` at Android thermal status 3 remains explicitly excluded.
  All 31 launches reached `game-ready` with same-PID continuity and zero fatal,
  ANR, LMK, or surface-error classifications. Across the 30 valid rows,
  process→PLAY-ready p50/p95/p99/max was `4.527/5.098/5.265/5.265 s`, external
  user-wait `0.175/0.210/0.214/0.214 s`, PLAY→game-ready
  `32.169/32.893/32.903/32.903 s`, and automatic total excluding user-wait
  `36.526/37.546/38.158/38.158 s`. Relative to the retained baseline, automatic
  p50 improved `11.463%` and p95 improved `13.998%`.
- Exact-current stage p50 values were Android `0.012 s`, install recovery
  `0 s`, cache sync `0 s`, assembly sync `0.137 s`, Godot bootstrap `2.876 s`,
  launcher creation `0.785 s`, launcher-ready transition `0.007 s`, tracker
  user-wait `0.326 s`, cloud/warmup `0 s`, game settings `0.063 s`, game startup
  `19.732 s`, anonymous mod discovery `0.056 s`, anonymous mod load `12.170 s`,
  and game-ready `0 s`. The corresponding p95 values for the two dominant
  spans were `20.010 s` and `12.738 s`.
- A subsequent interleaved 30-pair A/B correctly falsified that earlier
  non-interleaved startup conclusion: immediately before the logo fix, total
  p50 changed only `36.699 → 36.382 s` (-0.86%), below the 10% gate. Decompiling
  the matched private game assembly then showed that the progress surface
  completely covered a timed logo path with fades, fixed waits, and an
  animation loop. The retained main-menu essential-load path was identical
  when the game's existing `skipLogo` argument was true.
- With only the covered-logo scope added, the release-configured 30-pair series
  reached `game-ready` 60/60 with same-PID continuity, zero classified errors,
  and no thermal-invalid sample. Baseline→candidate total p50/p95/p99/max was
  `37.177/37.916/38.776/38.776 → 28.193/29.477/29.478/29.478 s`; p50 improved
  24.17% and p95 improved 22.26%. PLAY→ready p50 improved 28.72%. The causal
  stage moved `game-startup` p50 `19.032 → 10.022 s`, while anonymous mod-load
  p50 stayed `11.468 → 11.527 s`.
- The Goal's original stability baseline APK was then compared directly with
  the exact final no-suffix APK in a second 30-pair alternating series. All
  60 arms again reached `game-ready` in one PID with zero fatal, ANR, LMK, or
  surface-error classification; thermal transitions were eight `1→1` and 52
  `1→2`. Process→launcher p50/p95/p99/max was
  `6.056/6.634/6.734/6.734 → 6.060/6.701/6.958/6.958 s`; automated activation
  wait was `0.421/1.059/1.068/1.068 → 1.062/1.079/1.086/1.086 s`;
  PLAY→ready was `31.359/31.727/31.753/31.753 →
  21.739/22.397/22.685/22.685 s`; and complete automated launch was
  `38.061/38.695/38.712/38.712 → 28.797/29.702/30.388/30.388 s`. The final
  p50 improvement is 24.34% and p95 improvement is 23.24%.
- The standardized device triage completed full, Safe Mode, partition `0/2`,
  and partition `1/2` without manual game-menu navigation. Safe Mode and `0/2`
  were valid 60-second samples with p99 `18.988 ms` and `19.074 ms`, and zero
  frames over 2x budget. Full and `1/2` were retained as invalid samples with
  an anonymous `mod-load-error` classification rather than being misreported as
  performance passes. No raw logs, mod names, paths, account, save, or device
  identifier entered the repository.
- Nested real-combat isolation reproduced the sustained moderate frame-pacing
  regression in anonymous partition `5/8` (`p99 31.906 ms`), then separated its
  two single-group children: `10/16` remained slow (`p95 22.990 ms`, `p99
  32.107 ms`, 61 frames over 2x budget) while `11/16` was clean despite a hotter
  thermal sequence (`p95 17.587 ms`, `p99 18.810 ms`, 6 frames over 2x budget).
  This identifies a single third-party mod group as the sustained-jank owner;
  launcher protection remains session-only isolation/Safe Mode rather than
  changing or masking that mod's behavior.
- Three alternating 120-second pairs for the anonymous supported-mod group all
  passed at thermal status 0–2 with no load error or frame over 100 ms. Median
  baseline→optimized p95 was `17.643 → 17.607 ms`, p99
  `18.767 → 18.741 ms`, >2x frames `5 → 5`, and >50 ms frames `3 → 3`.
  Private evidence confirms that this group exercises both the
  base-library dependency path and one ordinary supported-mod path. This closes
  both 10% regression guardrails without recording either identity.
- The first unattended full-mod gameplay baseline was deliberately excluded:
  it reported an anonymous initializer failure (`p99 63.721 ms`, 139 frames
  over 2x budget), then ended at Android thermal status 3. The paired optimized
  run was rejected before launch. This attempt proved that full-mod A/B was
  contaminated and exposed two harness defects; the current runner now offers
  the composable Safe Mode pair, reads thermal status through a device-compatible
  fallback, and always stops the capture-owned process during teardown.
- Three 120-second no-mod idle-combat pairs held the renderer, APK,
  instrumentation, fixture, and thermal band constant. Median p95 was `17.731
  → 17.840 ms`, p99 `18.965 → 19.021 ms`, >2x frames `5 → 5`, and median RSS
  changed by about `+0.4%`. This is a clean regression guardrail, not a claim
  that an idle scene exercises the deck optimization.
- The standardized `deck-cycle` then waited for the real stable-hand boundary
  and executed exactly five deck open/close cycles in each of three paired
  Safe Mode runs. Every row passed with thermal status 0–2 and no mod-load
  error. Median baseline→optimized results were: p50 `16.686 → 16.687 ms`, p95
  `18.069 → 18.050 ms`, p99 `18.919 → 18.931 ms`, >2x frames `12 → 9`
  (`-25.0%`), >3x `6 → 4`, >50 ms `6 → 4`, >100 ms `3 → 0`, maximum frame
  `417.755 → 68.783 ms`, and RSS `1,560,732 → 1,535,620 KiB`. This satisfies
  the launcher-owned 25% long-frame-density gate while p95/p99 and memory stay
  inside their regression guardrails. Sanitized raw TSV remains outside git.
- The exact-current debug APK then ran the explicitly armed, Safe-Mode-only
  hidden-deck mutation proof against the sacrificial fixture. Its sanitized
  result was `obtain=1 remove=1 upgrade=1 restore=1 cleanup=1 error=0 pass=1`:
  obtain/remove each invalidated and rebuilt the next screen with the current
  count, upgrade/downgrade each produced a new card node from current state,
  and the `finally` path restored both deck count and upgrade state. The same
  run stayed at Android thermal status `0→0`; its 120-second interactive segment
  had p95/p99 `17.948/19.096 ms`, four frames over 50 ms, and zero over 100 ms.
  The harness force-stopped the app and deleted its temporary OCR screenshot;
  only bounded numeric TSV remains outside git.
- A cache-invalidated warmup run with injected Android memory pressure deferred
  optional work in `3 ms`, performed one planned restart, and reached
  `game-ready` without a crash loop. The following on-demand 300-second real
  gameplay capture had 17,935 frames, p95/p99 `17.778/18.957 ms`, no frame over
  `100 ms`, and long-frame density `0.000780597`; the comparable current
  warm-cache path was `0.000743080`, a `+5.05%` change inside the 10% guardrail.
- Three 120-second Safe Mode runs with bounded startup-summary persistence on
  and three with it off showed on-versus-off median deltas of p50 `+0.042%`,
  p95 `-0.235%`, p99 `+0.058%`, CPU `-0.144%`, and RSS `+1.584%`; neither side
  had a frame over `100 ms`. Release startup ignores the debug-only switch.
- Lifecycle checks passed 10/10 HOME/resume and 10/10 rotation with the same
  PID. Rotated EN startup UI had zero launcher-authored Hangul and no
  edge-clipped text. An offline cold start reached `game-ready` in `35.370 s`,
  and the harness restored the original network state afterward. A real KR
  startup displayed the localized mod-loading stage; the user language was
  restored to EN after the audit.
- The final no-suffix APK additionally passed a `29.591 s` cold start, 3/3
  HOME/resume, and 3/3 rotation checks. Its EN launcher screenshot audit found
  16 English OCR lines, zero Hangul, and zero edge-clipped lines; the temporary
  screenshot was deleted. The current language remains EN, Auto Sync remains
  off, and Vulkan remains the default.
- Real KR and EN watchdog paths passed with moving stage/total clocks. Repeated
  pre-first-frame native exits produced the localized renderer recovery dialog;
  repeated managed `mod-loading` crashes produced localized candidate-only
  Safe Mode dialogs in both languages. Choosing the session-only actions closed
  recovery normally and the next launch had no pending recovery. Every audit
  screenshot was deleted after inspection.
- Device attempt was invalidated when Android entered critical-battery mode and
  stopped the app with a system-kill exit reason. It was not counted as a crash,
  ANR, or performance sample.
- The direct GitHub parent was verified as
  `iunius612/StS2-Launcher_Mod_Manager`; local `upstream/main` at `59a5b87` is
  still the merge base and an ancestor after a final fetch. The merge-tree
  has no upstream conflict. Most runtime/performance ownership remains in new
  files; unavoidable integration edits stay at Activity bootstrap and existing
  launcher/mod lifecycle boundaries.
- Final hygiene checks found zero changed private-artifact extensions, zero
  changed file over 5 MiB, zero repository match for the device/evidence-path
  literals, zero match for the authorized Steam credential values, and zero
  remaining final-audit screenshot. `git diff --check` and the standardized
  focused workflow both pass.

## Residual scope

- One anonymous third-party mod group owns a reproducible sustained-jank
  cluster. The launcher can isolate and diagnose it through session-only
  partitions/Safe Mode, but cannot safely repair that mod's implementation.
- The reference device does not prove behavior on every ROM, GPU, driver,
  refresh rate, or future game build. Vulkan remains the measured default;
  OpenGL was worse on this device and stays a recovery-only option.
- The historical baseline predates stage telemetry, so its per-stage cells are
  explicitly unavailable. The separate pre-logo/final 30-pair series supplies
  the causal per-stage comparison without fabricating baseline spans.
