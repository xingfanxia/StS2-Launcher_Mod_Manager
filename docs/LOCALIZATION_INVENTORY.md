# Launcher KR/EN/zh-Hans localization inventory

This inventory is the review contract for `GOAL_SIMPLIFIED_CHINESE.md` and
retains the earlier stability-localization contract. It covers launcher-authored
visible text in C#, Android Java, and Android XML. It deliberately does not
translate content owned by users, mod authors, Steam Workshop authors, or
external error producers.

## Machine-readable inventory

Run:

```sh
dotnet run --project tools/localization-audit/localization-audit.csproj -- --list
```

The default (non-list) audit is part of `docker/build-apk.sh`. Its current
source inventory is:

| Classification | Entries | Contract |
|---|---:|---|
| `ui-explicit-pair` | 142 | `Loc.Tr/Select` has valid KR, EN, and zh-Hans paths |
| `ui-central-overlay` | 168 | legacy launcher copy resolves through both central overlays |
| `ui-english-source` | 156 | English-only launcher UI has a reviewed zh-Hans result |
| `translation-catalog` | 450 | English and zh-Hans exact/pattern/phrase entries are paired |
| `ui-stage-catalog` | 32 | every startup title/watchdog has KR, EN, and zh-Hans copy |
| `ui-adjacent-pair` | 8 | adjacent `*Ko/*En/*Zh` dynamic text is complete |
| `android-native-pair` | 16 | Java-visible copy is inside `nativeText(ko, en, zh)` |
| `ui-approved-token` | 4 | product/legal/language-name tokens intentionally remain unchanged |
| `non-ui-log` | 3 | diagnostic-only output, not rendered UI |
| `non-ui-comment` | 209 | source documentation only |
| **Total** | **1,188** | 62 localization-bearing source files |

The committed negative fixture runner first checks the real tree, injects one
untranslated launcher string, and requires the audit to reject it:

```sh
bash tools/localization-audit/tests/run.sh
```

It rejects five independent negative fixtures: unknown Korean launcher copy,
English-only launcher copy without zh-Hans, a `Loc.Tr` path without Chinese,
Traditional-only residue in a zh-Hans path, and Android `nativeText` without its
third language. It also verifies the policy boundary where external Korean or
English content remains byte-for-byte intact.

## Runtime provenance

Every shared `StyledLabel`, `StyledButton`, `StyledLineEdit`, and registered
`OptionButton` item is watched with one of these source categories:

| Provenance | EN/zh-Hans behavior | Examples |
|---|---|---|
| `LauncherAuthored` | translate; Hangul in EN or Hangul/unapproved English in zh-Hans is a failure | buttons, headings, hints, placeholders |
| `LauncherTemplateWithExternalContent` | switch only an explicitly registered trio; preserve embedded external text | status/error templates containing a mod id, account name, or external error |
| `ExternalContent` | never translate | mod/Workshop titles and descriptions, tags, authors, paths |

The following runtime owners carry explicit external or mixed provenance:

- local-mod, subscribed-mod, Workshop-download, Workshop-search, dependency,
  conflict, and update-list rows;
- Workshop detail title, description, tags, and author-supplied change notes;
- generic mod detail title/subtitle/body and values;
- branch names/descriptions, backup paths, and mixed error/result messages;
- launcher and Mod Hub status lines that embed account/mod/error data.

In EN and zh-Hans modes the registry audits only visible watched properties.
After the visible set is stable it emits a content-free summary:

```text
[LocalizationAudit] language=zh-Hans visible=N untranslated=0 preserved_external_hangul=N
```

`untranslated` is never exempt. `preserved_external_hangul` is expected when
an author or user supplied Korean content; neither the text nor an identifying
hash is logged.

## Dynamic and native surfaces

The PLAY transition cloud overlay previously outlived `LanguageSelector` and
assigned Korean after the launcher's refresh timer had been freed. Its initial
copy and every later status assignment now localize at assignment time. Shared
launcher status, busy, download-progress, and action-button setters do the same,
so future text does not wait for the periodic audit refresh.

Android's atlas rebuild overlay, mod-compatibility toast, and pre-Godot renderer
recovery alert select three-way copy from the persisted launcher language
preference. The in-game mod guard alert also uses an explicit trio for its title,
explanation, and action. The Android and C# locale policies share the same wire
values (`ko`, `en`, `zh-Hans`) and independently test legacy and system-locale
fallbacks.

## Review rules and exclusions

- New launcher-visible Korean must use a three-language `Loc.Tr/Select`, a
  reviewed central mapping, or an adjacent/native trio. Unknown text fails the
  build.
- New launcher-visible English must resolve to reviewed zh-Hans unless it is one
  of the narrow product/legal/language-name tokens. An English-only negative
  fixture proves the gate.
- New runtime fields with user/mod/Workshop content must declare provenance;
  source uncertainty is not permission to globally translate it.
- Mod names, descriptions, authors, Workshop content, usernames, save/profile
  names, filenames/paths, and external error bodies remain original.
- Logs, comments, and test fixtures are enumerated but are not UI exemptions.
- Removing, hiding, transliterating, or replacing Korean with a generic English
  placeholder is not an acceptable audit fix.
