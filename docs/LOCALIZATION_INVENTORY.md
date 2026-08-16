# Launcher KR/EN localization inventory

This inventory is the review contract for Phase 6 of
`GOAL_STABILITY_HARDENING.md`. It covers launcher-authored visible text in C#,
Android Java, and Android XML. It deliberately does not translate content owned
by users, mod authors, Steam Workshop authors, or external error producers.

## Machine-readable inventory

Run:

```sh
dotnet run --project tools/localization-audit/localization-audit.csproj -- --list
```

The default (non-list) audit is part of `docker/build-apk.sh`. Its current
source inventory is:

| Classification | Entries | Contract |
|---|---:|---|
| `ui-explicit-pair` | 126 | `Loc.Tr(ko, en)` has non-empty Hangul-free English |
| `ui-central-overlay` | 168 | legacy launcher copy resolves through `EnglishLocalization` |
| `translation-catalog` | 179 | every catalog key/pattern has an English replacement |
| `ui-adjacent-pair` | 4 | adjacent `*Ko`/`*En` recovery text is paired |
| `android-native-pair` | 9 | Java-visible Hangul is inside `nativeText(ko, en)` |
| `non-ui-log` | 3 | diagnostic-only output, not rendered UI |
| `non-ui-comment` | 209 | source documentation only |
| **Total** | **698** | 44 Hangul-bearing source files |

The committed negative fixture runner first checks the real tree, injects one
untranslated launcher string, and requires the audit to reject it:

```sh
bash tools/localization-audit/tests/run.sh
```

It also verifies the policy boundary where an external string that happens to
equal known launcher copy is preserved rather than translated.

## Runtime provenance

Every shared `StyledLabel`, `StyledButton`, and `StyledLineEdit` is watched with
one of these source categories:

| Provenance | EN behavior | Examples |
|---|---|---|
| `LauncherAuthored` | translate; remaining Hangul is a failure | buttons, headings, hints, placeholders |
| `LauncherTemplateWithExternalContent` | switch only an explicitly registered pair; preserve embedded external text | status/error templates containing a mod id, account name, or external error |
| `ExternalContent` | never translate | mod/Workshop titles and descriptions, tags, authors, paths |

The following runtime owners carry explicit external or mixed provenance:

- local-mod, subscribed-mod, Workshop-download, Workshop-search, dependency,
  conflict, and update-list rows;
- Workshop detail title, description, tags, and author-supplied change notes;
- generic mod detail title/subtitle/body and values;
- branch names/descriptions, backup paths, and mixed error/result messages;
- launcher and Mod Hub status lines that embed account/mod/error data.

In EN mode the registry audits only visible watched properties. After the
visible set is stable it emits a content-free summary:

```text
[LocalizationAudit] visible=N authored_hangul=0 preserved_external_hangul=N
```

`authored_hangul` is never exempt. `preserved_external_hangul` is expected when
an author or user supplied Korean content; neither the text nor an identifying
hash is logged.

## Dynamic and native surfaces

The PLAY transition cloud overlay previously outlived `LanguageToggle` and
assigned Korean after the launcher's refresh timer had been freed. Its initial
copy and every later status assignment now localize at assignment time. Shared
launcher status, busy, download-progress, and action-button setters do the same,
so future text does not wait for the periodic audit refresh.

Android's atlas rebuild overlay and pre-Godot renderer recovery alert select
paired copy from the persisted launcher language preference. The in-game mod
guard alert also uses explicit pairs for its title, explanation, and action.

## Review rules and exclusions

- New launcher-visible Hangul must use `Loc.Tr(ko, en)`, a reviewed central
  mapping, or an adjacent native pair. Unknown text fails the build.
- New runtime fields with user/mod/Workshop content must declare provenance;
  source uncertainty is not permission to globally translate it.
- Mod names, descriptions, authors, Workshop content, usernames, save/profile
  names, filenames/paths, and external error bodies remain original.
- Logs, comments, and test fixtures are enumerated but are not UI exemptions.
- Removing, hiding, transliterating, or replacing Korean with a generic English
  placeholder is not an acceptable audit fix.
