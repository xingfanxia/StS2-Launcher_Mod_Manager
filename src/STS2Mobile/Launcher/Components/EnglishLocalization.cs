using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace STS2Mobile.Launcher.Components;

// English overlay for legacy Korean-only launcher copy. It intentionally lives
// in one new file: upstream can keep adding/changing screens without turning a
// localization change into merge conflicts across every controller and dialog.
internal static class EnglishLocalization
{
    private static readonly ConcurrentDictionary<string, string> RuntimeEnglish = new(
        StringComparer.Ordinal
    );
    private static readonly ConcurrentDictionary<string, string> RuntimeKorean = new(
        StringComparer.Ordinal
    );
    private static readonly ConcurrentDictionary<string, string> TranslationCache = new(
        StringComparer.Ordinal
    );

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["클라우드 상태 확인 중..."] = "Checking cloud status...",
        ["잠시만 기다려 주세요"] = "Please wait",
        ["클라우드 백업 중"] = "Backing up to cloud",
        ["클라우드 동기화 중"] = "Syncing cloud saves",
        ["클라우드 정리 중"] = "Cleaning up cloud saves",
        ["클라우드 반영 중"] = "Updating cloud saves",
        ["클라우드 반영 중..."] = "Updating cloud saves...",
        ["클라우드 받는 중"] = "Downloading cloud saves",
        ["동기화 적용 중..."] = "Applying sync...",
        ["한국어로 전환했습니다."] = "Switched to Korean.",
        ["영어로 전환하려면 누르세요."] = "Tap to switch to English.",

        ["프로필별로 로컬과 Steam Cloud의 진행도를 확인하고 개별적으로 동기화할 수 있습니다."] =
            "Review local and Steam Cloud progress for each profile and sync them individually.",
        ["프로필 복제"] = "COPY PROFILE",
        ["백업 복원"] = "RESTORE BACKUP",
        ["닫기"] = "CLOSE",
        ["일치"] = "MATCH",
        ["충돌"] = "CONFLICT",
        ["로컬만"] = "LOCAL ONLY",
        ["클라우드만"] = "CLOUD ONLY",
        ["확인 불가"] = "UNAVAILABLE",
        ["일시적으로 클라우드를 확인하지 못함 · 다시 시도해 주세요"] =
            "Cloud status is temporarily unavailable · Please try again",
        ["복원할 로컬 백업 시점을 선택하세요. 현재 상태는 복원 직전에 자동 백업됩니다."] =
            "Choose a local backup to restore. Your current state will be backed up first.",
        ["수동"] = "MANUAL",
        ["자동 · 일치"] = "AUTO · MATCH",
        ["자동 · 충돌(유지)"] = "AUTO · CONFLICT KEPT",
        ["자동 · 충돌(폐기)"] = "AUTO · CONFLICT DISCARDED",
        ["클라우드 동기화가 꺼져 있어 로컬 기능만 사용할 수 있습니다."] =
            "Cloud sync is off. Only local tools are available.",

        ["이미지 캐시 정리"] = "CLEAR IMAGE CACHE",
        ["포션 / 카드 / 유물 이미지가 잘못 표시될 때 사용"] =
            "Use this if potion, card, or relic images display incorrectly",
        [
            "이미지 인덱스 캐시 정리\n\n포션 / 카드 / 유물 등 이미지가 잘못 표시될 때 사용하세요.\n게임 텍스처 캐시(약 660개) 를 삭제하고 앱을 재시작합니다.\n\n* 다음 실행이 30~60초 더 걸립니다 (재import)\n* 게임을 다시 다운로드하지 않습니다\n* 세이브 / 진행도 / 로그인 정보는 보존됩니다"
        ] =
            "Clear image index cache\n\nUse this if potion, card, or relic images display incorrectly.\nThis deletes about 660 cached game textures and restarts the app.\n\n* The next launch will take 30–60 seconds longer while textures are reimported\n* The game will not be downloaded again\n* Saves, progress, and login information are preserved",
        ["완료"] = "SUCCESS",
        ["실패"] = "FAILED",
        ["확인"] = "OK",

        ["세이브 동기화 상태"] = "SAVE SYNC STATUS",
        ["로컬과 Steam Cloud의 진행도가 일치합니다.\n별도 작업이 필요하지 않습니다."] =
            "Local and Steam Cloud progress match.\nNo action is needed.",
        ["로컬과 Steam Cloud 모두 진행도 데이터가 없습니다."] =
            "No progress data exists locally or in Steam Cloud.",
        ["세이브 데이터 동기화"] = "SYNC SAVE DATA",
        ["Steam Cloud에 진행도가 없습니다.\n이 디바이스 진행도를 클라우드로 업로드할까요?"] =
            "Steam Cloud has no progress data.\nUpload this device's progress?",
        ["이 디바이스에 진행도가 없습니다.\nSteam Cloud의 진행도를 가져올까요?"] =
            "This device has no progress data.\nDownload progress from Steam Cloud?",
        ["세이브 상태 확인 불가"] = "SAVE STATUS UNAVAILABLE",
        ["일시적으로 Steam Cloud 상태를 확인하지 못했습니다.\n잠시 후 다시 시도해 주세요."] =
            "Steam Cloud status is temporarily unavailable.\nPlease try again shortly.",
        ["세이브 데이터 충돌"] = "SAVE DATA CONFLICT",
        ["이 디바이스와 Steam Cloud의 진행도가 다릅니다.\n어느 쪽을 유지할지 선택하세요."] =
            "This device and Steam Cloud have different progress.\nChoose which one to keep.",
        ["📱  이 디바이스 (로컬)"] = "📱  THIS DEVICE (LOCAL)",
        ["취소"] = "CANCEL",
        ["로컬 유지"] = "KEEP LOCAL",
        ["클라우드 유지"] = "KEEP CLOUD",
        ["최근"] = "NEWER",
        ["진행도 데이터 없음"] = "NO PROGRESS DATA",
        ["파일 생성 시간"] = "FILE CREATED",
        ["파일 크기"] = "FILE SIZE",
        ["총 플레이타임"] = "TOTAL PLAYTIME",
        ["현재 진행"] = "CURRENT RUN",
        ["전적"] = "RECORD",
        ["최고 승천"] = "MAX ASCENSION",
        ["올라간 층"] = "FLOORS CLIMBED",
        ["발견 유물"] = "RELICS DISCOVERED",
        ["(상세 통계를 읽지 못함 — 파일은 존재함)"] =
            "(Detailed stats unavailable — the file exists)",

        ["백업 완료"] = "BACKUP COMPLETE",
        ["백업 실패"] = "BACKUP FAILED",
        ["백업된 파일"] = "FILES BACKED UP",
        ["총 크기"] = "TOTAL SIZE",
        ["저장 위치"] = "SAVED TO",
        ["백업 중 오류가 발생했습니다."] = "An error occurred while creating the backup.",
        ["프로필"] = "Profile",
        ["비어 있음"] = "EMPTY",
        ["데이터 있음"] = "HAS DATA",

        ["백업하려면 저장공간 접근 권한이 필요합니다.\n권한을 허용한 뒤 다시 시도하세요."] =
            "Storage access is required for backups.\nGrant access, then try again.",
        ["현재 세이브 데이터를 로컬에 백업할까요?"] = "Back up the current save data locally?",
        [
            "Steam 로그인이 만료되었습니다.\n다시 로그인하거나, 클라우드 동기화·창작마당 없이 오프라인으로 계속할 수 있습니다."
        ] =
            "Your Steam login has expired.\nSign in again, or continue offline without cloud sync or Workshop access.",
        ["다시 로그인"] = "SIGN IN AGAIN",
        ["오프라인으로 계속"] = "CONTINUE OFFLINE",
        ["Steam 로그인이 만료되었습니다. 다시 로그인해 주세요."] =
            "Your Steam login has expired. Please sign in again.",
        ["오프라인 모드 — 클라우드 동기화·창작마당은 재로그인 필요"] =
            "Offline mode — sign in again to use cloud sync and Workshop",
        [
            "Steam 로그인이 만료되어 이 기능을 쓸 수 없습니다.\n앱을 재실행한 뒤 다시 로그인해 주세요."
        ] =
            "This feature is unavailable because your Steam login expired.\nRestart the app and sign in again.",
        ["업데이트 적용을 위해 재시작합니다..."] = "Restarting to apply the update...",
        ["앱 재시작 필요"] = "RESTART REQUIRED",

        ["슬롯 정보 확인 중..."] = "Checking profile slots...",
        ["슬롯 정보를 확인하지 못했습니다."] = "Could not read profile slots.",
        ["복제할 데이터가 있는 슬롯이 없습니다."] = "No profile contains data to copy.",
        ["복제할 원본 슬롯"] = "SOURCE PROFILE",
        ["복제할 프로필을 선택하세요."] = "Choose the profile to copy.",
        ["덮어쓸 대상 슬롯"] = "DESTINATION PROFILE",
        ["복제본을 덮어쓸 대상 프로필을 선택하세요."] =
            "Choose the profile that will be overwritten.",
        ["대상 슬롯의 현재 데이터가 덮어써집니다. 진행 전 로컬 백업이 자동 생성됩니다."] =
            "The destination profile will be overwritten. A local backup will be created first.",
        ["진행 중이던 런(current_run)은 복사되지 않습니다."] =
            "The active run (current_run) will not be copied.",
        ["프로필 복제 중..."] = "Copying profile...",
        ["복제 중 오류가 발생했습니다."] = "An error occurred while copying the profile.",
        ["클라우드에도 반영할까요?"] = "Apply this change to Steam Cloud too?",
        ["예"] = "YES",
        ["아니오"] = "NO",
        ["클라우드 반영에 실패했습니다. 이번 세션은 로컬 전용으로 전환됩니다."] =
            "Could not update Steam Cloud. This session will continue in local-only mode.",
        [
            "복제는 완료됐지만 클라우드에는 반영하지 않았습니다.\n다음 동기화에서 클라우드 진행도가 더 높으면 복사본이 되돌려질 수 있습니다."
        ] =
            "The profile was copied but Steam Cloud was not updated.\nA newer cloud save may overwrite the copy during the next sync.",
        ["백업 목록 확인 중..."] = "Loading backups...",
        ["백업 목록을 확인하지 못했습니다."] = "Could not load the backup list.",
        ["백업이 없습니다."] = "No backups are available.",
        ["이 백업 시점으로 전체 세이브를 되돌립니다. 현재 상태는 복원 직전에 자동 백업됩니다."] =
            "Restore all saves to this backup. Your current state will be backed up first.",
        ["복원 중..."] = "Restoring backup...",
        ["복원 중 오류가 발생했습니다."] = "An error occurred while restoring the backup.",
        ["클라우드 반영이 시간 초과되었습니다. 일부 파일이 반영되지 않았을 수 있습니다."] =
            "Updating Steam Cloud timed out. Some files may not have been uploaded.",
        ["클라우드 반영 중 오류가 발생했습니다. 로그를 확인하세요."] =
            "An error occurred while updating Steam Cloud. Check the log.",
        [
            "복원은 완료됐지만 클라우드에는 반영하지 않았습니다.\n다음 동기화에서 클라우드 진행도가 더 높으면 복사본이 되돌려질 수 있습니다."
        ] =
            "The backup was restored but Steam Cloud was not updated.\nA newer cloud save may overwrite it during the next sync.",

        ["모드를 관리하려면 저장소 권한이 필요합니다."] =
            "Storage access is required to manage mods.",
        ["\"Import Mod (.zip)\"를 누르거나 WORKSHOP 탭에서 구독하세요."] =
            "Tap \"Import Mod (.zip)\" or subscribe from the WORKSHOP tab.",
        ["권한을 허용한 뒤 여기로 돌아와 Refresh 를 누르세요."] =
            "Grant access, return here, and tap Refresh.",
        ["창작마당 기능을 쓰려면 Steam 로그인이 필요합니다."] =
            "Sign in to Steam to use Workshop features.",
        ["아직 구독한 창작마당 모드가 없습니다."] =
            "You have not subscribed to any Workshop mods yet.",
        ["WORKSHOP 탭에서 둘러보고 구독하면 자동으로 다운로드됩니다."] =
            "Browse the WORKSHOP tab and subscribe to download mods automatically.",
        ["수동 설치본 존재 — 창작마당 버전 미적용"] =
            "Manual install detected — Workshop version not applied",
        ["비활성 · 업데이트 있음 — 활성화 후 다운로드"] =
            "Disabled · Update available — enable to download",
        ["조회 시간 초과 — 다시 시도해 주세요."] = "Lookup timed out — please try again.",
        ["Steam 구독은 해제됨; 로컬 정리 건너뜀."] =
            "Steam subscription removed; local cleanup skipped.",

        [
            "Steam 로그인이 만료되어 클라우드 세이브 기능을 쓸 수 없습니다.\n앱을 재실행한 뒤 다시 로그인해 주세요.\n(로컬 기능은 계속 사용할 수 있습니다)"
        ] =
            "Cloud saves are unavailable because your Steam login expired.\nRestart the app and sign in again.\n(Local features remain available.)",
        ["모드 오류 감지"] = "MOD ERROR DETECTED",
        ["이 알림을 보고 싶지 않으면\n런처 화면 우측 상단의 Debug 토글을 OFF 하세요."] =
            "To hide this alert, turn off the Debug toggle in the top-right of the launcher.",
        ["저장소 권한이 없습니다."] = "Storage permission is missing.",
        ["백업할 세이브가 없습니다."] = "There are no saves to back up.",
        ["백업을 찾을 수 없습니다."] = "The backup could not be found.",
        ["원본과 대상 슬롯이 동일합니다."] = "The source and destination profiles are the same.",
        ["Steam 로그인이 만료되었거나 취소되었습니다. 다시 로그인해 주세요."] =
            "Your Steam login expired or was cancelled. Please sign in again.",
        ["세이브 파일이 불완전(손상)해 클라우드 업로드를 보류했습니다."] =
            "Cloud upload was paused because the save file is incomplete or corrupt.",
        ["세이브 보호 — 클라우드 덮어쓰기 차단"] = "SAVE PROTECTION — CLOUD OVERWRITE BLOCKED",
        ["이미 수동 설치된 모드와 충돌(덮어쓰지 않음)"] =
            "Conflicts with a manually installed mod (not overwritten)",
        ["모드 매니페스트 없음"] = "Mod manifest is missing",
        ["다운로드 실패"] = "Download failed",
        ["취소됨"] = "Cancelled",
        [
            "이번 세션은 세이브를 복구 모드로 열었습니다(게임 버전 차이). 로컬을 클라우드로 덮으시겠습니까?"
        ] =
            "This session opened saves in recovery mode due to a game-version difference. Overwrite the cloud copy with local saves?",
        ["덮어쓰기"] = "OVERWRITE",
    };

    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    {
        Make(@"^프로필 (\d+)( · 모드)?$", "Profile $1$2"),
        Make(@"^로컬에만 있음 · (.+)$", "Local only · $1"),
        Make(@"^Cloud에만 있음 · (.+)$", "Cloud only · $1"),
        Make(@"^동기화됨 · (.+)$", "Synced · $1"),
        Make(@"^로컬 (.+) vs Cloud (.+)$", "Local $1 vs Cloud $2"),
        Make(@"^(\d+)개 · (.+)$", "$1 files · $2"),
        Make(@"^(\d+)개$", "$1 files"),
        Make(
            @"^이 디바이스와 Steam Cloud의 진행도가 다릅니다 \((\d+)개 프로필\)\.\n어느 쪽을 유지할지 선택하세요\.$",
            "This device and Steam Cloud differ across $1 profiles.\nChoose which one to keep."
        ),
        Make(@"^(\d+)승 / (\d+)패$", "$1 wins / $2 losses"),
        Make(@"^(\d+)막 (\d+)층$", "Act $1, Floor $2"),
        Make(@"^복제 중 오류: (.+)$", "Copy failed: $1"),
        Make(@"^복원 중 오류: (.+)$", "Restore failed: $1"),
        Make(@"^복제 완료 \((\d+)개 파일\)\.$", "Profile copied ($1 files)."),
        Make(@"^복원 완료 \((\d+)개 파일\)\.$", "Backup restored ($1 files)."),
        Make(
            @"^복제 완료 및 클라우드 반영됨 \((\d+)개 파일\)\.$",
            "Profile copied and uploaded to Steam Cloud ($1 files)."
        ),
        Make(
            @"^복원 완료 및 클라우드 반영됨 \((\d+)개 파일\)\.$",
            "Backup restored and uploaded to Steam Cloud ($1 files)."
        ),
        Make(
            @"^클라우드 반영 중\.\.\. 남은 파일 (\d+)개$",
            "Updating Steam Cloud... $1 files remaining"
        ),
        Make(@"^클라우드 반영 중\.\.\. (\d+)/(\d+)$", "Updating Steam Cloud... $1/$2"),
        Make(@"^로컬 모드 (\d+)개 설치됨\.$", "$1 local mods installed."),
        Make(@"^가져오는 중 (\d+)/(\d+)…$", "Importing $1/$2…"),
        Make(
            @"^신규 (\d+)개 \+ 업데이트 (\d+)개 — 다운로드 중:$",
            "$1 new + $2 updates — downloading:"
        ),
        Make(@"^ (\d+)개 건너뜀\.$", " $1 skipped."),
        Make(@"^(\d+) / (\d+)개$", "$1 / $2 items"),
        Make(@"^다운로드 중 (\d+(?:\.\d+)?)%$", "Downloading $1%"),
        Make(@"^파일 (\d+)개$", "$1 files"),
        Make(
            @"^(\d+)/(\d+)개 파일 복원 실패\. 복원 직전 백업: (.+)$",
            "$1 of $2 files failed to restore. Pre-restore backup: $3"
        ),
        Make(
            @"^일부 파일 복사 실패: (.+)\. 사전 백업: (.+)$",
            "Some files could not be copied: $1. Pre-copy backup: $2"
        ),
        Make(
            @"^Welcome back, (.+) \(Steam 로그인 곧 만료 — 재로그인 권장\)$",
            "Welcome back, $1 (Steam login expires soon — sign in again recommended)"
        ),
        Make(
            @"^프로필 (\d+)( · 모드)? → 프로필 (\d+)( · 모드)? 복제\.\n\n대상 슬롯의 현재 데이터가 덮어써집니다\. 진행 전 로컬 백업이 자동 생성됩니다\.(\n\n진행 중이던 런\(current_run\)은 복사되지 않습니다\.)?$",
            "Copy Profile $1$2 → Profile $3$4.\n\nThe destination profile will be overwritten. A local backup will be created first.$5"
        ),
        Make(
            @"^'(.+)'을\(를\) 삭제할까요\?\n저장소에서 모드 폴더가 삭제됩니다\.$",
            "Remove '$1'?\nThe mod folder will be deleted from this device."
        ),
        Make(
            @"^'(.+)'은\(는\) 이미 설치되어 있습니다\. 덮어쓸까요\?$",
            "'$1' is already installed. Overwrite it?"
        ),
        Make(
            @"^새 창작마당 모드 (\d+)개 감지 — 다운로드 중:$",
            "$1 new Workshop mods found — downloading:"
        ),
        Make(
            @"^창작마당 모드 업데이트 (\d+)개 감지 — 다운로드 중:$",
            "$1 Workshop mod updates found — downloading:"
        ),
        Make(
            @"^다음 모드는 더 이상 Steam 에서 구독 중이 아니므로 삭제됩니다:\n(.+)$",
            "These mods are no longer subscribed on Steam and will be removed:\n$1"
        ),
        Make(
            @"^'(.+)'의 최신 창작마당 버전이 있습니다\. 지금 받을까요\?\n\(나중에: 다음 동기화 때 자동 업데이트됩니다\.\)$",
            "A newer Workshop version of '$1' is available. Download it now?\n(Otherwise it will update during the next sync.)"
        ),
        Make(
            @"^수동 설치된 '(.+)'을\(를\) 창작마당 버전\((.+)\)으로 교체할까요\?\n수동 설치 폴더는 삭제됩니다\.$",
            "Replace the manually installed '$1' with Workshop version $2?\nThe manual-install folder will be deleted."
        ),
        Make(
            @"^'(.+)' 구독을 해제할까요\? 기기에서 모드가 삭제됩니다\.$",
            "Unsubscribe from '$1'? The mod will be removed from this device."
        ),
        Make(
            @"^id (\d+) 에 해당하는 창작마당 아이템이 없습니다\(또는 이 계정으로 접근 불가\)\.$",
            "No Workshop item exists for id $1, or this account cannot access it."
        ),
        Make(
            @"^'(.+)' 크기는 (.+) 입니다\. 구독하고 다운로드할까요\?$",
            "'$1' is $2. Subscribe and download it?"
        ),
        Make(
            @"^빈 내용\((\d+) bytes\) 쓰기를 차단했습니다\. 클라우드 상태를 아직 확인하지 못해 안전을 위해 보류합니다\.$",
            "Blocked an empty write ($1 bytes) until cloud status can be verified."
        ),
        Make(
            @"^빈 내용\((\d+) bytes\)이 클라우드의 기존 저장\((\d+) bytes\)을 덮어쓰려 해 차단했습니다\.$",
            "Blocked an empty write ($1 bytes) from overwriting the existing cloud save ($2 bytes)."
        ),
        Make(@"^'(.+)' 모드에서 오류가 발생했습니다\.\n(.+)$", "The '$1' mod caused an error.\n$2"),
    };

    private static readonly (string Korean, string English)[] PhraseReplacements =
    {
        (" · 모드", " · Modded"),
        ("앱 재시작 필요", "Restart required"),
        ("남은 파일", "files remaining"),
        ("클라우드 정리 중", "Cleaning up cloud saves"),
        ("클라우드 받는 중", "Downloading cloud saves"),
        ("클라우드 반영 중", "Updating Steam Cloud"),
        ("클라우드 동기화 중", "Syncing cloud saves"),
        ("권한을 허용한 뒤 다시 시도하세요", "Grant access, then try again"),
        ("저장공간 접근 권한이 필요합니다", "Storage access is required"),
        (
            "진행 중이던 런(current_run)은 복사되지 않습니다.",
            "The active run (current_run) will not be copied."
        ),
        ("로그를 확인하세요", "Check the log"),
    };

    public static bool ContainsKorean(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var ch in value)
        {
            if (ch is >= '\uAC00' and <= '\uD7A3')
                return true;
        }
        return false;
    }

    // Loc.Tr calls this even while Korean is active. Capturing both rendered
    // values lets an already-open control switch immediately in either direction.
    public static void Register(string korean, string english)
    {
        if (string.IsNullOrEmpty(korean) || string.IsNullOrEmpty(english))
            return;
        RuntimeEnglish[korean] = english;
        RuntimeKorean[english] = korean;
    }

    public static string RestoreKorean(string value) =>
        !string.IsNullOrEmpty(value) && RuntimeKorean.TryGetValue(value, out var korean)
            ? korean
            : value;

    public static bool TryTranslateRegistered(string value, out string translated) =>
        RuntimeEnglish.TryGetValue(value ?? "", out translated);

    public static bool TryRestoreRegistered(string value, out string restored) =>
        RuntimeKorean.TryGetValue(value ?? "", out restored);

    public static string Translate(string value)
    {
        if (!ContainsKorean(value))
            return value;
        if (RuntimeEnglish.TryGetValue(value, out var runtime))
            return runtime;
        if (TranslationCache.TryGetValue(value, out var cached))
            return cached;
        if (Exact.TryGetValue(value, out var exact))
        {
            TranslationCache[value] = exact;
            return exact;
        }

        var translated = value;
        foreach (var (pattern, replacement) in Patterns)
        {
            if (pattern.IsMatch(value))
            {
                translated = pattern.Replace(value, replacement);
                break;
            }
        }

        foreach (var (korean, english) in PhraseReplacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);
        TranslationCache[value] = translated;
        return translated;
    }

    private static (Regex Pattern, string Replacement) Make(string pattern, string replacement) =>
        (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline), replacement);
}
