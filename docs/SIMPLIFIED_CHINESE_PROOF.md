# Simplified Chinese launcher proof

Status: implementation, canonical build, signed upgrade, and physical Android
interaction/layout proof passed on 2026-08-19.

## Contract and implementation

- `LauncherLanguage` is a typed three-value state: Korean, English, and
  Simplified Chinese. Persisted wire values are `ko`, `en`, and `zh-Hans`.
- Existing `ko/en` preferences remain readable. Managed and Java tests cover
  `zh`, `zh-Hans`, `zh_CN`, and `zh_SG`, while `zh-Hant`, `zh_TW`, `zh_HK`, and
  `zh_MO` do not silently select Simplified Chinese.
- The legacy Console `EN · ON/OFF` control is gone. A 48pt, blue-outlined
  `OptionButton` with a stable `LANG` marker and the three self-named choices now
  mounts once beside the `StS2 Launcher` title, before login.
- `LocalizedTextPolicy` and `LocalizedTextRegistry` now render a typed language
  and retain canonical source text across repeated `ko → en → zh-Hans → ko`
  changes. Registered `OptionButton` items update with the same refresh path.
- `SimplifiedChineseLocalization` is a separate low-conflict overlay. Existing
  high-churn KR/EN controllers remain mostly unchanged; exact, pattern, phrase,
  and English-source translations are centralized.
- Android native startup overlays, renderer recovery, and the incompatible-mod
  toast read the same persisted preference and use `nativeText(ko,en,zh)`.
- Launcher-authored console messages are localized and re-render after an
  in-place language change. Raw Cloud diagnostics remain in the debug log while
  the visible Chinese Console presents bounded Chinese summaries. Model,
  download, account, mod, Workshop, and file-path values cross an explicit
  external-content boundary and remain unchanged.
- A display adapter substitutes equivalent ASCII punctuation for the small set
  of CJK punctuation glyphs rendered as tofu by the game-provided Android font
  atlas. Chinese wording remains intact, and the OCR harness checks Hangul,
  common tofu markers, edge clipping, and target-control positions without
  printing recognized text.

## Automated localization evidence

Canonical container audit on 2026-08-19:

```text
android-native-pair: 14
non-ui-comment: 209
non-ui-log: 3
translation-catalog: 450
ui-adjacent-pair: 8
ui-approved-token: 4
ui-central-overlay: 168
ui-english-source: 156
ui-explicit-pair: 142
ui-stage-catalog: 32
Audited 1186 localization source entries across 62 files.
PASS: every launcher-authored visible Hangul literal has English and Simplified Chinese paths.
PASS: Korean, English, missing-zh, Traditional-residue, and Android-native negative fixtures are rejected
```

The policy fixtures additionally prove that:

- Korean and English launcher copy renders reviewed Simplified Chinese;
- dynamic account/mod values survive translation unchanged;
- external Korean and English Workshop content remains byte-for-byte intact;
- unapproved English residue is counted as untranslated in zh-Hans mode.

## Build and regression evidence

The pinned signed-container build completed successfully on 2026-08-19. It ran
the full focused stability/localization/Java/device-harness/member-reference/
patch-target gates, formatted and published `STS2Mobile`, built the release APK,
ran Workshop sync tests, and verified APK Signature Scheme v2 with one signer.

```text
Artifact: StS2Launcher-v0.4.6-zh-hans-qa5.apk
versionName: 0.4.6-zh-hans-qa5
versionCode: 343
SHA-256: 451d6c8e463a841abc538eb60dcaed6a9b76afc8acf4736c8f2e0f6160dddd3b
```

The separate full `STS2Mobile.csproj` compile also passed with zero warnings and
zero errors after dependency restore. No signing credential, account value,
device serial, screenshot, trace, or raw device log is stored in the repository.

## Android interaction and layout evidence

Physical proof passed on an ARM64 OPPO PKH110 running Android 16. The captured
surface was 2480×2248 physical pixels and the Godot launcher viewport reported
1920×1740. Device serials, account values, screenshots, and raw logs remain
outside the repository.

1. The blue-outlined, 48pt title-row selector and blue `LANG` marker were visible
   without scrolling. Its popup contained `한국어`, `English`, and `简体中文` with
   no clipping; content-free OCR located both the closed selector and the lower
   popup item.
2. `zh-Hans → ko → en → zh-Hans` updated the mounted launcher, status, buttons,
   and stored Console entries without restart. Runtime reports were emitted for
   all three states and ended at `language=zh-Hans visible=15 untranslated=0`.
   Newly-created Workshop cards, sort items, Subscribed/Local/Downloads panes,
   update/branch dialogs, Save Manager, backup list, and profile-copy rows also
   rendered in the active language. Workshop statistics, playtime/current-run
   templates, `PLAY`, and technical size-only rows passed the residue policy.
3. Simplified Chinese survived force-stop/relaunch and multiple signed
   `adb install -r` upgrades, including the final QA5 install. Package signature
   and first-install time stayed unchanged; the existing login, cloud-sync-off
   preference, saves, mod registry, and renderer default remained present.
4. The Android-native Godot bootstrap and graphics-recovery dialog displayed
   reviewed Chinese. Two debug-only pre-frame crash injections produced the real
   repeated-crash prompt with both Vulkan/compatibility actions. The managed
   Startup Recovery/Safe Mode selector and game-side ModGuard alert also passed.
   A debug-only atlas-overlay preview reused production text/layout and logged
   `no cache mutation`; no real texture cache, save, or mod file was deleted.
5. Final OCR checks on launcher, popup, Workshop, Subscribed, Local, Save Manager,
   recovery, ModGuard, and native overlays reported visible Chinese with zero
   Hangul residue, zero common tofu markers, and zero edge-clipped lines. External
   mod titles, versions, Steam branch descriptions, account text, paths, and
   error bodies remained unchanged by policy.
6. The sanitized lifecycle matrix passed 3/3 HOME/resume rows and 4/4 alternating
   landscape-rotation rows with PID continuity. Every row recorded zero fatal,
   ANR, LMK, and Vulkan surface errors; the post-rotation Chinese screenshot had
   no overlap, clipping, tofu, black screen, or lost focus.

An ARM64 API 36 Pixel 8 emulator was attempted as supplemental evidence. The
SwiftShader run reached `standalone-ready`, but its Vulkan present path returned
`QueuePresentKHR` errors and produced a black capture. The host/MoltenVK run
terminated inside the emulator/Godot renderer during the initial Android
landscape reconfiguration before launcher UI creation. These emulator-only
renderer failures are excluded from the acceptance result; the physical-device
evidence above is authoritative for this goal.
