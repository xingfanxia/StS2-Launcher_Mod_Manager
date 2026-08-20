using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace STS2Mobile.Launcher.Components;

// Simplified-Chinese overlay kept beside the existing English overlay so
// upstream KR/EN call sites stay stable. Exact/pattern coverage is enforced by
// tools/localization-audit; missing launcher copy deliberately remains visible
// as its source language instead of silently falling back to English.
internal static class SimplifiedChineseLocalization
{
    private static readonly ConcurrentDictionary<string, string> RuntimeChinese = new(
        StringComparer.Ordinal
    );
    private static readonly ConcurrentDictionary<string, string> RuntimeCanonical = new(
        StringComparer.Ordinal
    );

    private static readonly Dictionary<string, string> ExactKorean = new(StringComparer.Ordinal)
    {
        ["클라우드 상태 확인 중..."] = "正在检查云存档状态…",
        ["잠시만 기다려 주세요"] = "请稍候",
        ["클라우드 백업 중"] = "正在备份到云端",
        ["클라우드 동기화 중"] = "正在同步云存档",
        ["클라우드 정리 중"] = "正在清理云存档",
        ["클라우드 반영 중"] = "正在更新云存档",
        ["클라우드 반영 중..."] = "正在更新云存档…",
        ["클라우드 받는 중"] = "正在下载云存档",
        ["동기화 적용 중..."] = "正在应用同步…",
        ["한국어로 전환했습니다."] = "已切换到韩语。",
        ["영어로 전환했습니다."] = "已切换到英语。",
        ["중국어(간체)로 전환했습니다."] = "已切换到简体中文。",
        ["영어로 전환하려면 누르세요."] = "点击切换为英语。",
        ["Steam 계정 전환"] = "切换 Steam 账号",
        ["계정 전환 취소"] = "取消账号切换",
        ["계정마다 세이브와 런처 설정을 따로 보관합니다. 게임 파일과 모드는 공유됩니다."] =
            "每个账号分别保存存档和启动器设置；游戏文件与 mod 共用。",
        ["다른 계정 추가"] = "添加其他账号",
        ["현재"] = "当前",
        [
            "Steam 계정을 전환할까요?\n\n대기 중인 클라우드 쓰기를 먼저 마친 뒤 앱을 재시작합니다. 게임, 세이브, Workshop/mod, 로컬 백업 및 클라우드 데이터는 삭제하지 않습니다."
        ] =
            "现在切换 Steam 账号吗？\n\n将先完成待处理的云端写入，然后重启应用。不会删除游戏、存档、Workshop/mod、本地备份或云端数据。",
        ["전환"] = "切换",
        ["계정 전환 전 클라우드 쓰기를 마무리하는 중..."] = "正在完成云端写入，然后切换账号…",
        ["계정 전환을 완료하지 못했습니다. 현재 계정은 변경되지 않았습니다."] =
            "未能完成账号切换，当前账号保持不变。",
        ["계정 추가 전 클라우드 쓰기를 마무리하는 중..."] = "正在完成云端写入，然后添加账号…",
        ["계정 전환을 시작하지 못했습니다. 현재 계정은 변경되지 않았습니다."] =
            "无法开始账号切换，当前账号保持不变。",
        ["다른 Steam 계정을 추가하려면 로그인하세요"] = "登录以添加其他 Steam 账号",

        ["프로필별로 로컬과 Steam Cloud의 진행도를 확인하고 개별적으로 동기화할 수 있습니다."] =
            "可分别查看各个配置档的本地与 Steam Cloud 进度并进行同步。",
        ["프로필 복제"] = "复制配置档",
        ["백업 복원"] = "恢复备份",
        ["닫기"] = "关闭",
        ["일치"] = "一致",
        ["충돌"] = "冲突",
        ["로컬만"] = "仅本地",
        ["클라우드만"] = "仅云端",
        ["확인 불가"] = "无法确认",
        ["일시적으로 클라우드를 확인하지 못함 · 다시 시도해 주세요"] =
            "暂时无法检查云端状态 · 请重试",
        ["복원할 로컬 백업 시점을 선택하세요. 현재 상태는 복원 직전에 자동 백업됩니다."] =
            "请选择要恢复的本地备份。恢复前会自动备份当前状态。",
        ["수동"] = "手动",
        ["자동 · 일치"] = "自动 · 一致",
        ["자동 · 충돌(유지)"] = "自动 · 冲突（保留）",
        ["자동 · 충돌(폐기)"] = "自动 · 冲突（舍弃）",
        ["클라우드 동기화가 꺼져 있어 로컬 기능만 사용할 수 있습니다."] =
            "云同步已关闭，仅可使用本地功能。",

        ["이미지 캐시 정리"] = "清除图像缓存",
        ["포션 / 카드 / 유물 이미지가 잘못 표시될 때 사용"] = "药水、卡牌或遗物图像显示异常时使用",
        [
            "이미지 인덱스 캐시 정리\n\n포션 / 카드 / 유물 등 이미지가 잘못 표시될 때 사용하세요.\n게임 텍스처 캐시(약 660개) 를 삭제하고 앱을 재시작합니다.\n\n* 다음 실행이 30~60초 더 걸립니다 (재import)\n* 게임을 다시 다운로드하지 않습니다\n* 세이브 / 진행도 / 로그인 정보는 보존됩니다"
        ] =
            "清除图像索引缓存\n\n药水、卡牌或遗物等图像显示异常时使用。\n将删除约 660 个游戏纹理缓存并重启应用。\n\n* 下次启动会因重新导入而多花 30–60 秒\n* 不会重新下载游戏\n* 存档、进度和登录信息都会保留",
        ["완료"] = "成功",
        ["실패"] = "失败",
        ["확인"] = "确定",

        ["세이브 동기화 상태"] = "存档同步状态",
        ["로컬과 Steam Cloud의 진행도가 일치합니다.\n별도 작업이 필요하지 않습니다."] =
            "本地与 Steam Cloud 的进度一致。\n无需其他操作。",
        ["로컬과 Steam Cloud 모두 진행도 데이터가 없습니다."] = "本地与 Steam Cloud 均无进度数据。",
        ["세이브 데이터 동기화"] = "同步存档数据",
        ["Steam Cloud에 진행도가 없습니다.\n이 디바이스 진행도를 클라우드로 업로드할까요?"] =
            "Steam Cloud 中没有进度数据。\n是否上传此设备的进度？",
        ["이 디바이스에 진행도가 없습니다.\nSteam Cloud의 진행도를 가져올까요?"] =
            "此设备上没有进度数据。\n是否下载 Steam Cloud 中的进度？",
        ["세이브 상태 확인 불가"] = "无法检查存档状态",
        ["일시적으로 Steam Cloud 상태를 확인하지 못했습니다.\n잠시 후 다시 시도해 주세요."] =
            "暂时无法检查 Steam Cloud 状态。\n请稍后重试。",
        ["세이브 데이터 충돌"] = "存档数据冲突",
        ["이 디바이스와 Steam Cloud의 진행도가 다릅니다.\n어느 쪽을 유지할지 선택하세요."] =
            "此设备与 Steam Cloud 的进度不同。\n请选择要保留的一方。",
        ["📱  이 디바이스 (로컬)"] = "📱  此设备（本地）",
        ["취소"] = "取消",
        ["로컬 유지"] = "保留本地",
        ["클라우드 유지"] = "保留云端",
        ["최근"] = "较新",
        ["진행도 데이터 없음"] = "无进度数据",
        ["파일 생성 시간"] = "文件创建时间",
        ["파일 크기"] = "文件大小",
        ["총 플레이타임"] = "总游戏时间",
        ["현재 진행"] = "当前进度",
        ["전적"] = "战绩",
        ["최고 승천"] = "最高进阶",
        ["올라간 층"] = "到达楼层",
        ["발견 유물"] = "发现的遗物",
        ["(상세 통계를 읽지 못함 — 파일은 존재함)"] = "（无法读取详细统计——文件存在）",

        ["백업 완료"] = "备份完成",
        ["백업 실패"] = "备份失败",
        ["백업된 파일"] = "已备份文件",
        ["총 크기"] = "总大小",
        ["저장 위치"] = "保存位置",
        ["백업 중 오류가 발생했습니다."] = "备份时发生错误。",
        ["프로필"] = "配置档",
        ["비어 있음"] = "空",
        ["데이터 있음"] = "有数据",

        ["백업하려면 저장공간 접근 권한이 필요합니다.\n권한을 허용한 뒤 다시 시도하세요."] =
            "备份需要存储空间访问权限。\n请授权后重试。",
        ["현재 세이브 데이터를 로컬에 백업할까요?"] = "是否将当前存档数据备份到本地？",
        [
            "Steam 로그인이 만료되었습니다.\n다시 로그인하거나, 클라우드 동기화·창작마당 없이 오프라인으로 계속할 수 있습니다."
        ] = "Steam 登录已过期。\n你可以重新登录，或在不使用云同步和创意工坊的情况下离线继续。",
        ["다시 로그인"] = "重新登录",
        ["오프라인으로 계속"] = "离线继续",
        ["Steam 로그인이 만료되었습니다. 다시 로그인해 주세요."] = "Steam 登录已过期，请重新登录。",
        ["오프라인 모드 — 클라우드 동기화·창작마당은 재로그인 필요"] =
            "离线模式——云同步和创意工坊需要重新登录",
        [
            "Steam 로그인이 만료되어 이 기능을 쓸 수 없습니다.\n앱을 재실행한 뒤 다시 로그인해 주세요."
        ] = "Steam 登录已过期，无法使用此功能。\n请重启应用并重新登录。",
        ["업데이트 적용을 위해 재시작합니다..."] = "正在重启以应用更新…",
        ["앱 재시작 필요"] = "需要重启应用",

        ["슬롯 정보 확인 중..."] = "正在检查配置档槽位…",
        ["슬롯 정보를 확인하지 못했습니다."] = "无法读取配置档槽位信息。",
        ["복제할 데이터가 있는 슬롯이 없습니다."] = "没有包含可复制数据的配置档。",
        ["복제할 원본 슬롯"] = "源配置档",
        ["복제할 프로필을 선택하세요."] = "请选择要复制的配置档。",
        ["덮어쓸 대상 슬롯"] = "目标配置档",
        ["복제본을 덮어쓸 대상 프로필을 선택하세요."] = "请选择要被副本覆盖的配置档。",
        ["대상 슬롯의 현재 데이터가 덮어써집니다. 진행 전 로컬 백업이 자동 생성됩니다."] =
            "目标配置档的当前数据将被覆盖。操作前会自动创建本地备份。",
        ["진행 중이던 런(current_run)은 복사되지 않습니다."] =
            "不会复制正在进行的游戏（current_run）。",
        ["프로필 복제 중..."] = "正在复制配置档…",
        ["복제 중 오류가 발생했습니다."] = "复制时发生错误。",
        ["클라우드에도 반영할까요?"] = "是否也将更改应用到 Steam Cloud？",
        ["예"] = "是",
        ["아니오"] = "否",
        ["클라우드 반영에 실패했습니다. 이번 세션은 로컬 전용으로 전환됩니다."] =
            "无法更新 Steam Cloud，本次会话将切换为仅本地模式。",
        [
            "복제는 완료됐지만 클라우드에는 반영하지 않았습니다.\n다음 동기화에서 클라우드 진행도가 더 높으면 복사본이 되돌려질 수 있습니다."
        ] = "配置档已复制，但未更新云端。\n下次同步时，如果云端进度较新，副本可能会被覆盖。",
        ["백업 목록 확인 중..."] = "正在加载备份列表…",
        ["백업 목록을 확인하지 못했습니다."] = "无法加载备份列表。",
        ["백업이 없습니다."] = "没有可用备份。",
        ["이 백업 시점으로 전체 세이브를 되돌립니다. 현재 상태는 복원 직전에 자동 백업됩니다."] =
            "将全部存档恢复到此备份时间点。恢复前会自动备份当前状态。",
        ["복원 중..."] = "正在恢复备份…",
        ["복원 중 오류가 발생했습니다."] = "恢复备份时发生错误。",
        ["클라우드 반영이 시간 초과되었습니다. 일부 파일이 반영되지 않았을 수 있습니다."] =
            "更新 Steam Cloud 超时，部分文件可能尚未上传。",
        ["클라우드 반영 중 오류가 발생했습니다. 로그를 확인하세요."] =
            "更新 Steam Cloud 时发生错误，请查看日志。",
        [
            "복원은 완료됐지만 클라우드에는 반영하지 않았습니다.\n다음 동기화에서 클라우드 진행도가 더 높으면 복사본이 되돌려질 수 있습니다."
        ] = "备份已恢复，但未更新云端。\n下次同步时，如果云端进度较新，恢复内容可能会被覆盖。",

        ["모드를 관리하려면 저장소 권한이 필요합니다."] = "管理 mod 需要存储空间权限。",
        ["\"Import Mod (.zip)\"를 누르거나 WORKSHOP 탭에서 구독하세요."] =
            "点击“导入 mod（.zip）”，或在创意工坊页面订阅。",
        ["권한을 허용한 뒤 여기로 돌아와 Refresh 를 누르세요."] = "授权后返回此处并点击刷新。",
        ["창작마당 기능을 쓰려면 Steam 로그인이 필요합니다."] = "使用创意工坊功能需要登录 Steam。",
        ["아직 구독한 창작마당 모드가 없습니다."] = "尚未订阅任何创意工坊 mod。",
        ["WORKSHOP 탭에서 둘러보고 구독하면 자동으로 다운로드됩니다."] =
            "在创意工坊页面浏览并订阅后会自动下载。",
        ["수동 설치본 존재 — 창작마당 버전 미적용"] = "检测到手动安装版本——未应用创意工坊版本",
        ["비활성 · 업데이트 있음 — 활성화 후 다운로드"] = "已禁用 · 有可用更新——启用后下载",
        ["조회 시간 초과 — 다시 시도해 주세요."] = "查询超时——请重试。",
        ["Steam 구독은 해제됨; 로컬 정리 건너뜀."] = "已取消 Steam 订阅；已跳过本地清理。",

        [
            "Steam 로그인이 만료되어 클라우드 세이브 기능을 쓸 수 없습니다.\n앱을 재실행한 뒤 다시 로그인해 주세요.\n(로컬 기능은 계속 사용할 수 있습니다)"
        ] = "Steam 登录已过期，无法使用云存档。\n请重启应用并重新登录。\n（本地功能仍可使用）",
        ["모드 오류 감지"] = "检测到 mod 错误",
        ["이 알림을 보고 싶지 않으면\n런처 화면 우측 상단의 Debug 토글을 OFF 하세요."] =
            "若不想看到此提醒，请关闭启动器右上角的 Debug 开关。",
        ["저장소 권한이 없습니다."] = "没有存储空间权限。",
        ["백업할 세이브가 없습니다."] = "没有可备份的存档。",
        ["백업을 찾을 수 없습니다."] = "找不到备份。",
        ["원본과 대상 슬롯이 동일합니다."] = "源配置档与目标配置档相同。",
        ["Steam 로그인이 만료되었거나 취소되었습니다. 다시 로그인해 주세요."] =
            "Steam 登录已过期或被取消，请重新登录。",
        ["세이브 파일이 불완전(손상)해 클라우드 업로드를 보류했습니다."] =
            "存档文件不完整或已损坏，已暂停上传云端。",
        ["세이브 보호 — 클라우드 덮어쓰기 차단"] = "存档保护——已阻止覆盖云端",
        ["이미 수동 설치된 모드와 충돌(덮어쓰지 않음)"] = "与已手动安装的 mod 冲突（未覆盖）",
        ["모드 매니페스트 없음"] = "缺少 mod 清单",
        ["다운로드 실패"] = "下载失败",
        ["취소됨"] = "已取消",
        [
            "이번 세션은 세이브를 복구 모드로 열었습니다(게임 버전 차이). 로컬을 클라우드로 덮으시겠습니까?"
        ] = "由于游戏版本不同，本次会话以恢复模式打开了存档。是否用本地存档覆盖云端？",
        ["덮어쓰기"] = "覆盖",

        // Explicit KR/EN call sites kept out of high-churn controllers.
        ["한국어"] = "韩语",
        ["언어 선택"] = "选择语言",
        ["시작 복구"] = "启动恢复",
        ["안전 모드로 계속 (권장)"] = "以安全模式继续（推荐）",
        ["이번 실행에서 모드 절반 테스트"] = "本次运行测试一半 mod",
        ["평소대로 시작"] = "正常启动",
        ["설치됨"] = "已安装",
        ["업데이트 있음"] = "有可用更新",
        ["비활성"] = "已禁用",
        ["구독 중"] = "已订阅",
        ["‹ 뒤로"] = "‹ 返回",
        ["설명"] = "说明",
        ["업데이트 노트"] = "更新说明",
        ["토론"] = "讨论",
        ["댓글"] = "评论",
        ["업데이트 노트를 불러오는 중…"] = "正在加载更新说明…",
        ["이 모드의 토론은 Steam 커뮤니티에서 볼 수 있습니다. 아래 버튼으로 브라우저에서 여세요."] =
            "可在 Steam 社区查看此 mod 的讨论。点击下方按钮在浏览器中打开。",
        ["아직 댓글이 없습니다."] = "暂无评论。",
        ["댓글은 Steam 커뮤니티에서 읽고 작성할 수 있습니다. 아래 버튼으로 브라우저에서 여세요."] =
            "可在 Steam 社区阅读和发布评论。点击下方按钮在浏览器中打开。",
        ["브라우저에서 열기"] = "在浏览器中打开",
        ["(설명 없음)"] = "（无说明）",
        ["업데이트 노트가 없습니다."] = "没有更新说明。",
        ["(내용 없음)"] = "（无内容）",
        ["업데이트 노트를 불러오지 못했습니다."] = "无法加载更新说明。",
        ["게임 렌더링 준비 중…"] = "正在准备游戏渲染…",
        ["⤢ 세로"] = "⤢ 竖屏",
        ["⤢ 가로"] = "⤢ 横屏",
        ["설치된 로컬 모드가 없습니다."] = "没有已安装的本地 mod。",
        ["파일 선택기 여는 중…"] = "正在打开文件选择器…",
        ["창작마당 검색 또는 URL/ID 붙여넣기…"] = "搜索创意工坊或粘贴项目 URL/ID…",
        ["Steam 연결 중…"] = "正在连接 Steam…",
        ["불러오는 중…"] = "正在加载…",
        ["아이템 조회 중…"] = "正在查询项目…",
        ["구독 해제됨."] = "已取消订阅。",
        ["동기화됨."] = "已同步。",
        ["구독 동기화 중…"] = "正在同步订阅…",
        ["동기화 실패(오프라인?)"] = "同步失败（是否离线？）",
        ["정리 중…"] = "正在清理…",
        ["진행 중"] = "进行中",
        ["대기 중"] = "等待中",
        ["다운로드 대기"] = "等待下载",
        ["구독 해제 중…"] = "正在取消订阅…",
        ["셰이더 컴파일 중…"] = "正在编译着色器…",
        ["리소스 목록 확인 중…"] = "正在检查资源列表…",
        ["셰이더 검색 중…"] = "正在扫描着色器…",
        ["필요할 때 셰이더를 준비하며 계속합니다…"] = "继续运行，并在需要时准备着色器…",
        ["메모리를 보호하기 위해 사전 준비를 중단했습니다."] = "为保护可用内存，已停止预热。",
        ["게임 시작 단계 확인 중…"] = "正在检查游戏启动阶段…",
        ["일반 모드로 재시작"] = "以普通模式重启",
        ["이 세션 계속"] = "继续本次运行",
        [
            "이번 실행에서는 시작 충돌 복구를 위해 OpenGL 호환 렌더러를 사용합니다.\n\n이 변경은 이번 실행에만 적용되며 다음 실행은 자동으로 기본 Vulkan을 사용합니다. 지금 Vulkan으로 다시 시작하거나 호환 모드로 계속할 수 있습니다."
        ] =
            "本次运行使用 OpenGL 兼容渲染器进行启动故障恢复。\n\n此更改仅对本次运行生效；下次启动会自动恢复默认 Vulkan。你可以立即使用 Vulkan 重启，或继续使用兼容模式。",
        ["Vulkan으로 다시 시작"] = "使用 Vulkan 重启",
        ["호환 모드로 계속"] = "以兼容模式继续",
        ["이전 시작 상태 확인 중..."] = "正在检查上次启动状态…",
    };

    private static readonly Dictionary<string, string> ExactEnglish = new(StringComparer.Ordinal)
    {
        ["Initializing..."] = "正在初始化…",
        ["Console"] = "控制台",
        ["Select language"] = "选择语言",
        ["Select game version"] = "选择游戏版本",
        ["Pick a Steam branch to download. Beta branches may be unstable."] =
            "选择要下载的 Steam 分支。Beta 分支可能不稳定。",
        ["Cancel"] = "取消",
        ["OK"] = "确定",
        ["Starting download..."] = "正在开始下载…",
        ["Save Manager"] = "存档管理器",
        ["CLOSE"] = "关闭",
        ["DETAIL"] = "详情",
        ["ENABLE"] = "启用",
        ["DISABLE"] = "禁用",
        ["UNSUBSCRIBE"] = "取消订阅",
        ["SUBSCRIBE"] = "订阅",
        ["Installed"] = "已安装",
        ["Update available"] = "有可用更新",
        ["Disabled"] = "已禁用",
        ["Subscribed"] = "已订阅",
        ["This mod requires:"] = "此 mod 需要：",
        ["Download complete! Restart to play."] = "下载完成！请重启后开始游戏。",
        ["Download cancelled"] = "下载已取消",
        ["Update available!"] = "有可用更新！",
        ["UP TO DATE"] = "已是最新版本",
        ["CHECK FAILED"] = "检查失败",
        ["Mod Manager"] = "Mod 管理器",
        ["No connection — saved credentials will be used"] = "无网络连接——将使用已保存的凭据",
        ["Connection failed. Internet required for first launch."] = "连接失败。首次启动需要网络。",
        ["Connecting to Steam..."] = "正在连接 Steam…",
        ["Authenticating..."] = "正在验证身份…",
        ["Verifying game ownership..."] = "正在验证游戏所有权…",
        ["Verifying code..."] = "正在验证代码…",
        ["Loading branches..."] = "正在加载分支…",
        ["Checking..."] = "正在检查…",
        ["CHECK LAUNCHER UPDATE"] = "检查启动器更新",
        ["CHECK GAME UPDATE"] = "检查游戏更新",
        ["Debug: ON"] = "调试：开",
        ["Debug: OFF"] = "调试：关",
        ["MOD MANAGER"] = "MOD 管理器",
        ["SAVE MANAGER"] = "存档管理器",
        ["RETRY"] = "重试",
        ["Local Backup"] = "本地备份",
        ["Auto Sync: OFF"] = "自动同步：关",
        ["Auto Sync: ON"] = "自动同步：开",
        ["Push to Cloud"] = "上传到云端",
        ["Pull from Cloud"] = "从云端下载",
        ["LAUNCH"] = "启动游戏",
        ["PLAY"] = "开始游戏",
        ["RESTART APP"] = "重启应用",
        ["Enter Steam Guard code"] = "输入 Steam Guard 验证码",
        ["Code"] = "验证码",
        ["SUBMIT"] = "提交",
        ["Code was incorrect. Enter new code:"] = "验证码错误，请输入新验证码：",
        ["DOWNLOAD GAME FILES"] = "下载游戏文件",
        ["Steam Username"] = "Steam 用户名",
        ["Password"] = "密码",
        ["LOGIN"] = "登录",
        ["‹ BACK"] = "‹ 返回",
        ["Mod Hub"] = "Mod 中心",
        ["Grant Storage Permission"] = "授予存储权限",
        ["Import Mod (.zip)..."] = "导入 mod (.zip)…",
        ["Refresh"] = "刷新",
        ["SEARCH"] = "搜索",
        ["Popular"] = "热门",
        ["Newest"] = "最新",
        ["Trending"] = "趋势",
        ["Last Updated"] = "最近更新",
        ["Top Rated"] = "最高评分",
        ["TAGS"] = "标签",
        ["LOAD MORE"] = "加载更多",
        ["CANCEL ALL"] = "全部取消",
        ["No downloads. Steam login is required for Workshop features."] =
            "没有下载任务。使用创意工坊功能需要登录 Steam。",
        ["No downloads."] = "没有下载任务。",
        ["Workshop items you subscribe to are downloaded here."] =
            "你订阅的创意工坊项目会在此下载。",
        ["USE WORKSHOP"] = "使用创意工坊版本",
        ["Enter host IP address"] = "输入主机 IP 地址",
        ["JOIN"] = "加入",
        [
            "Allow 'All Files Access'?\n\nNeeded for installing mods, saving local game backups, and writing debug logs under /storage/emulated/0/StS2LauncherMM/.\n\nIf you cancel, this prompt will appear again on the next launch."
        ] =
            "是否允许“所有文件访问权限”？\n\n安装 mod、保存本地游戏备份以及向 /storage/emulated/0/StS2LauncherMM/ 写入调试日志都需要此权限。\n\n如果取消，下次启动时会再次提示。",
        [
            "A Workshop download is still in progress. Leaving the Mod Manager will cancel it. Leave anyway?"
        ] = "创意工坊下载仍在进行。离开 Mod 管理器将取消下载。仍要离开吗？",
        ["Connection timed out. Valid ownership marker found."] =
            "连接超时，但已找到有效的游戏所有权标记。",
        [
            "You're already on the latest launcher version.\n\nOpen the GitHub releases page anyway?"
        ] = "当前已是最新启动器版本。\n\n仍要打开 GitHub Release 页面吗？",
        ["Launcher update download cancelled."] = "启动器更新下载已取消。",
        ["Debug logging disabled."] = "调试日志已关闭。",
        ["(failed to start)"] = "（启动失败）",
        ["Backing up saves locally..."] = "正在将存档备份到本地…",
        ["Local backup needs storage permission."] = "本地备份需要存储空间权限。",
        ["Push local saves to cloud?\nThis will overwrite your cloud saves."] =
            "是否将本地存档上传到云端？\n这会覆盖你的云存档。",
        ["Pushing local saves to cloud..."] = "正在将本地存档上传到云端…",
        ["Push complete."] = "上传完成。",
        [
            "Push timed out — some saves may not have finished uploading. Check your connection and try again."
        ] = "上传超时——部分存档可能尚未上传完成。请检查网络连接后重试。",
        ["Push finished with errors — some saves may not have uploaded. Check the log."] =
            "上传结束，但出现错误——部分存档可能未上传。请查看日志。",
        ["Push finished."] = "上传结束。",
        ["Pull cloud saves to local?\nThis will overwrite your local saves."] =
            "是否将云存档下载到本地？\n这会覆盖你的本地存档。",
        ["Pulling cloud saves to local..."] = "正在将云存档下载到本地…",
        ["Pull complete."] = "下载完成。",
        ["Pull finished with errors — some saves may not have downloaded. Check the log."] =
            "下载结束，但出现错误——部分存档可能未下载。请查看日志。",
        ["Pull finished."] = "下载结束。",
        ["Min game version"] = "最低游戏版本",
        ["Path"] = "路径",
        ["Remove Mod"] = "移除 mod",
        ["Enable"] = "启用",
        ["Disable"] = "禁用",
        ["by "] = "作者 ",
        ["Queued"] = "等待中",
        ["Completed"] = "已完成",
        ["WORKSHOP"] = "创意工坊",
        ["SUBSCRIBED"] = "已订阅",
        ["LOCAL"] = "本地",
        ["DOWNLOADS"] = "下载",
    };

    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    {
        Make(@"^프로필 (\d+)( · 모드)?$", "配置档 $1$2"),
        Make(@"^로컬에만 있음 · (.+)$", "仅本地 · $1"),
        Make(@"^Cloud에만 있음 · (.+)$", "仅云端 · $1"),
        Make(@"^동기화됨 · (.+) · (\d+)h (\d+)m$", "已同步 · $1 · $2 小时 $3 分钟"),
        Make(@"^동기화됨 · (.+) · (\d+)m$", "已同步 · $1 · $2 分钟"),
        Make(@"^동기화됨 · (.+)$", "已同步 · $1"),
        Make(@"^로컬 (.+) vs Cloud (.+)$", "本地 $1 vs 云端 $2"),
        Make(@"^(\d+)개 · (.+)$", "$1 个文件 · $2"),
        Make(@"^(\d+)개$", "$1 个文件"),
        Make(
            @"^이 디바이스와 Steam Cloud의 진행도가 다릅니다 \((\d+)개 프로필\)\.\n어느 쪽을 유지할지 선택하세요\.$",
            "此设备与 Steam Cloud 的 $1 个配置档进度不同。\n请选择要保留的一方。"
        ),
        Make(@"^(\d+)승 / (\d+)패$", "$1 胜 / $2 负"),
        Make(@"^(\d+)막 (\d+)층$", "第 $1 幕，第 $2 层"),
        Make(@"^복제 중 오류: (.+)$", "复制失败：$1"),
        Make(@"^복원 중 오류: (.+)$", "恢复失败：$1"),
        Make(@"^복제 완료 \((\d+)개 파일\)\.$", "配置档复制完成（$1 个文件）。"),
        Make(@"^복원 완료 \((\d+)개 파일\)\.$", "备份恢复完成（$1 个文件）。"),
        Make(
            @"^복제 완료 및 클라우드 반영됨 \((\d+)개 파일\)\.$",
            "配置档已复制并上传到 Steam Cloud（$1 个文件）。"
        ),
        Make(
            @"^복원 완료 및 클라우드 반영됨 \((\d+)개 파일\)\.$",
            "备份已恢复并上传到 Steam Cloud（$1 个文件）。"
        ),
        Make(@"^클라우드 반영 중\.\.\. 남은 파일 (\d+)개$", "正在更新 Steam Cloud… 剩余 $1 个文件"),
        Make(@"^클라우드 반영 중\.\.\. (\d+)/(\d+)$", "正在更新 Steam Cloud… $1/$2"),
        Make(@"^로컬 모드 (\d+)개 설치됨\.$", "已安装 $1 个本地 mod。"),
        Make(@"^가져오는 중 (\d+)/(\d+)…$", "正在导入 $1/$2…"),
        Make(
            @"^신규 (\d+)개 \+ 업데이트 (\d+)개 — 다운로드 중:$",
            "$1 个新增 + $2 个更新——正在下载："
        ),
        Make(@"^ (\d+)개 건너뜀\.$", " 已跳过 $1 个。"),
        Make(@"^(\d+) / (\d+)개$", "$1 / $2 项"),
        Make(@"^다운로드 중 (\d+(?:\.\d+)?)%$", "正在下载 $1%"),
        Make(@"^파일 (\d+)개$", "$1 个文件"),
        Make(
            @"^(\d+)/(\d+)개 파일 복원 실패\. 복원 직전 백업: (.+)$",
            "$2 个文件中有 $1 个恢复失败。恢复前备份：$3"
        ),
        Make(
            @"^일부 파일 복사 실패: (.+)\. 사전 백업: (.+)$",
            "部分文件复制失败：$1。复制前备份：$2"
        ),
        Make(
            @"^Welcome back, (.+) \(Steam 로그인 곧 만료 — 재로그인 권장\)$",
            "欢迎回来，$1（Steam 登录即将过期——建议重新登录）"
        ),
        Make(
            @"^프로필 (\d+)( · 모드)? → 프로필 (\d+)( · 모드)? 복제\.\n\n대상 슬롯의 현재 데이터가 덮어써집니다\. 진행 전 로컬 백업이 자동 생성됩니다\.(\n\n진행 중이던 런\(current_run\)은 복사되지 않습니다\.)?$",
            "复制配置档 $1$2 → 配置档 $3$4。\n\n目标配置档的当前数据将被覆盖。操作前会自动创建本地备份。$5"
        ),
        Make(
            @"^'(.+)'을\(를\) 삭제할까요\?\n저장소에서 모드 폴더가 삭제됩니다\.$",
            "是否移除“$1”？\n此设备上的 mod 文件夹将被删除。"
        ),
        Make(@"^'(.+)'은\(는\) 이미 설치되어 있습니다\. 덮어쓸까요\?$", "“$1”已安装。是否覆盖？"),
        Make(
            @"^새 창작마당 모드 (\d+)개 감지 — 다운로드 중:$",
            "发现 $1 个新的创意工坊 mod——正在下载："
        ),
        Make(
            @"^창작마당 모드 업데이트 (\d+)개 감지 — 다운로드 중:$",
            "发现 $1 个创意工坊 mod 更新——正在下载："
        ),
        Make(
            @"^다음 모드는 더 이상 Steam 에서 구독 중이 아니므로 삭제됩니다:\n(.+)$",
            "以下 mod 已不再于 Steam 订阅，将被移除：\n$1"
        ),
        Make(
            @"^'(.+)'의 최신 창작마당 버전이 있습니다\. 지금 받을까요\?\n\(나중에: 다음 동기화 때 자동 업데이트됩니다\.\)$",
            "“$1”有新的创意工坊版本。现在下载吗？\n（稍后会在下次同步时自动更新。）"
        ),
        Make(
            @"^수동 설치된 '(.+)'을\(를\) 창작마당 버전\((.+)\)으로 교체할까요\?\n수동 설치 폴더는 삭제됩니다\.$",
            "是否将手动安装的“$1”替换为创意工坊版本（$2）？\n手动安装文件夹将被删除。"
        ),
        Make(
            @"^'(.+)' 구독을 해제할까요\? 기기에서 모드가 삭제됩니다\.$",
            "是否取消订阅“$1”？该 mod 将从此设备移除。"
        ),
        Make(
            @"^id (\d+) 에 해당하는 창작마당 아이템이 없습니다\(또는 이 계정으로 접근 불가\)\.$",
            "不存在 ID 为 $1 的创意工坊项目，或此账户无权访问。"
        ),
        Make(
            @"^'(.+)' 크기는 (.+) 입니다\. 구독하고 다운로드할까요\?$",
            "“$1”大小为 $2。是否订阅并下载？"
        ),
        Make(
            @"^빈 내용\((\d+) bytes\) 쓰기를 차단했습니다\. 클라우드 상태를 아직 확인하지 못해 안전을 위해 보류합니다\.$",
            "已阻止写入空内容（$1 字节），等待确认云端状态。"
        ),
        Make(
            @"^빈 내용\((\d+) bytes\)이 클라우드의 기존 저장\((\d+) bytes\)을 덮어쓰려 해 차단했습니다\.$",
            "已阻止用空内容（$1 字节）覆盖现有云存档（$2 字节）。"
        ),
        Make(@"^이번 실행에서 '(.+)' 제외$", "本次运行排除“$1”"),
        Make(@"^댓글 (.+)개$", "$1 条评论"),
        Make(@"^구독 실패: (.+)$", "订阅失败：$1"),
        Make(@"^구독 해제 실패: (.+)$", "取消订阅失败：$1"),
        Make(@"^조회 실패: (.+)$", "查询失败：$1"),
        Make(@"^구독 (.+)$", "订阅 $1"),
        Make(@"^즐겨찾기 (.+)$", "收藏 $1"),
        Make(@"^조회 (.+)$", "浏览 $1"),
        Make(@"^업데이트 (.+)$", "更新 $1"),
        Make(@"^게시 (.+)$", "发布 $1"),
        Make(@"^'(.+)' 활성화 중…$", "正在启用“$1”…"),
        Make(@"^'(.+)' 비활성화 중…$", "正在禁用“$1”…"),
        Make(@"^'(.+)' 활성화됨\.$", "已启用“$1”。"),
        Make(@"^'(.+)' 비활성화됨\(보관\)\.$", "已禁用“$1”（已保留）。"),
        Make(@"^'(.+)' 삭제 중…$", "正在删除“$1”…"),
        Make(@"^(.+) 삭제됨\.$", "已删除 $1。"),
        Make(@"^(.+) 삭제 실패\.$", "删除 $1 失败。"),
        Make(@"^검색 실패: (.+)$", "搜索失败：$1"),
        Make(
            @"^id (.+) 에 해당하는 창작마당 아이템이 없습니다\(또는 이 계정으로 접근 불가\)\.$",
            "不存在 ID 为 $1 的创意工坊项目，或此账户无权访问。"
        ),
        Make(@"^(\d+)개 \(직접 조회\)$", "$1 项（直接查询）"),
        Make(@"^다운로드 중 (.+)%$", "正在下载 $1%"),
        Make(@"^실패: (.+)$", "失败：$1"),
        Make(@"^'(.+)' 교체 중…$", "正在替换“$1”…"),
        Make(@"^교체 실패: (.+)$", "替换失败：$1"),
        Make(@"^'(.+)' 구독 해제 중…$", "正在取消订阅“$1”…"),
        Make(@"^'(.+)' 구독을 해제할까요\?$", "是否取消订阅“$1”？"),
        Make(
            @"^'(.+)' 모드에서 오류가 발생했습니다\.\n\((.+)\)\n게임은 계속 진행할 수 있지만, 문제가 반복되면 Mod Hub에서 해당 모드를 비활성화하세요\.\n\n이 알림을 보고 싶지 않으면\n런처 화면 우측 상단의 Debug 토글을 OFF 하세요\.$",
            "mod“$1”发生错误。\n（$2）\n游戏可以继续运行；如果问题反复出现，请在 Mod Hub 中禁用该 mod。\n\n若不想看到此提醒，请关闭启动器右上角的 Debug 开关。"
        ),
        Make(@"^'(.+)' 모드에서 오류가 발생했습니다\.\n(.+)$", "mod“$1”发生错误。\n$2"),
    };

    private static readonly (Regex Pattern, string Replacement)[] EnglishPatterns =
    {
        Make(
            @"^(.+) · (\d+)h (\d+)m · (\d+)막 (\d+)층$",
            "$1 · $2 小时 $3 分钟 · 第 $4 幕, 第 $5 层"
        ),
        Make(@"^(.+) · (\d+)m · (\d+)막 (\d+)층$", "$1 · $2 分钟 · 第 $3 幕, 第 $4 层"),
        Make(@"^(.+) · (\d+)h (\d+)m$", "$1 · $2 小时 $3 分钟"),
        Make(@"^(.+) · (\d+)m$", "$1 · $2 分钟"),
        Make(@"^(\d+) subscriber\(s\) · (.+) · (\d+)% rated$", "$1 位订阅者 · $2 · $3% 好评"),
        Make(@"^(\d+)h (\d+)m$", "$1 小时 $2 分钟"),
        Make(@"^(\d+)m$", "$1 分钟"),
        Make(@"^Downloading launcher v(.+)\.\.\.$", "正在下载启动器 v$1…"),
        Make(@"^(\d+) / (\d+) \((.+)%\)$", "$1 / $2（$3%）"),
        Make(@"^(.+) downloaded$", "已下载 $1"),
        Make(@"^Download failed: (.+)$", "下载失败：$1"),
        Make(@"^Downloading (.+)%$", "正在下载 $1%"),
        Make(@"^Failed: (.+)$", "失败：$1"),
        Make(@"^Update check failed: (.+)$", "更新检查失败：$1"),
        Make(@"^Branch list failed: (.+)$", "分支列表加载失败：$1"),
        Make(@"^Launcher update check failed: (.+)$", "启动器更新检查失败：$1"),
        Make(@"^Failed to check for launcher updates\.\n\n(.+)$", "无法检查启动器更新。\n\n$1"),
        Make(
            @"^Launcher v(.+) is available, but no APK asset was attached\.\n\nOpen the GitHub releases page in a browser\?$",
            "启动器 v$1 已发布，但 Release 中没有 APK 文件。\n\n是否在浏览器中打开 GitHub Release 页面？"
        ),
        Make(
            @"^Launcher v(.+) is available\.\n\nTo install it, allow this app to install other apps\. Open system settings\?$",
            "启动器 v$1 已发布。\n\n安装前需要允许此应用安装其他应用。是否打开系统设置？"
        ),
        Make(
            @"^Launcher v(.+) is available\.\n\nDownload and install now\?$",
            "启动器 v$1 已发布。\n\n现在下载并安装吗？"
        ),
        Make(
            @"^Launcher v(.+) is available\.\n\n(.+)\n\nDownload and install now\?$",
            "启动器 v$1 已发布。\n\n$2\n\n现在下载并安装吗？"
        ),
        Make(
            @"^Launcher update v(.+) downloaded; opening installer\.\.\.$",
            "启动器更新 v$1 已下载；正在打开安装程序…"
        ),
        Make(@"^Launcher update download failed: (.+)$", "启动器更新下载失败：$1"),
        Make(
            @"^Debug logging is ON\.\n\nCurrent log file:\n(.+)\n\nTurn off\?$",
            "调试日志已开启。\n\n当前日志文件：\n$1\n\n是否关闭？"
        ),
        Make(
            @"^Turn debug logging on\?\n\nLogs will be written under:\n(.+)\n\nFor full launch-to-gameplay logs, restart the app after enabling\.$",
            "是否开启调试日志？\n\n日志将写入：\n$1\n\n如需完整记录从启动器到游戏过程，请在开启后重启应用。"
        ),
        Make(@"^Debug logging enabled → (.+)$", "调试日志已开启 → $1"),
        Make(@"^Local backup complete: (\d+) file\(s\)\.$", "本地备份完成：$1 个文件。"),
        Make(@"^Local backup failed: (.+)$", "本地备份失败：$1"),
        Make(@"^Local backup threw: (.+)$", "本地备份发生异常：$1"),
        Make(@"^Push failed: (.+)$", "上传失败：$1"),
        Make(@"^Pull failed: (.+)$", "下载失败：$1"),
        Make(@"^Welcome back, (.+)$", "欢迎回来，$1"),
        Make(@"^Logged in as (.+)$", "已登录为 $1"),
        Make(@"^Checking (.+)\.\.\.$", "正在检查 $1…"),
        Make(@"^v(.+) available$", "v$1 可用"),
        Make(@"^(\d+) item\(s\)\.$", "$1 个项目。"),
        Make(
            @"^installed (.+) · Workshop (.+) — Workshop is newer$",
            "已安装 $1 · 创意工坊 $2——创意工坊版本较新"
        ),
        Make(
            @"^installed (.+) · Workshop (.+) — your copy is newer$",
            "已安装 $1 · 创意工坊 $2——本地版本较新"
        ),
        Make(
            @"^installed (.+) · Workshop (.+) — same version$",
            "已安装 $1 · 创意工坊 $2——版本相同"
        ),
        Make(@"^installed (.+) · Workshop (.+)$", "已安装 $1 · 创意工坊 $2"),
    };

    private static readonly (string Korean, string Chinese)[] PhraseReplacements =
    {
        (" · 모드", " · 使用 mod"),
        ("앱 재시작 필요", "需要重启应用"),
        ("남은 파일", "剩余文件"),
        ("클라우드 정리 중", "正在清理云存档"),
        ("클라우드 받는 중", "正在下载云存档"),
        ("클라우드 반영 중", "正在更新 Steam Cloud"),
        ("클라우드 동기화 중", "正在同步云存档"),
        ("권한을 허용한 뒤 다시 시도하세요", "请授权后重试"),
        ("저장공간 접근 권한이 필요합니다", "需要存储空间访问权限"),
        (
            "진행 중이던 런(current_run)은 복사되지 않습니다.",
            "不会复制正在进行的游戏（current_run）。"
        ),
        ("로그를 확인하세요", "请查看日志"),
    };

    internal static void Register(string korean, string english, string chinese)
    {
        chinese = ForDisplay(chinese);
        if (string.IsNullOrEmpty(chinese))
            return;
        if (!string.IsNullOrEmpty(korean))
        {
            RuntimeChinese[korean] = chinese;
            RuntimeCanonical[chinese] = korean;
        }
        if (!string.IsNullOrEmpty(english))
        {
            RuntimeChinese[english] = chinese;
            RuntimeCanonical.TryAdd(chinese, korean ?? english);
        }
    }

    internal static bool TryTranslateRegistered(string value, out string translated) =>
        RuntimeChinese.TryGetValue(value ?? "", out translated);

    internal static bool TryRestoreRegistered(string value, out string canonical) =>
        RuntimeCanonical.TryGetValue(value ?? "", out canonical);

    internal static string RestoreCanonical(string value) =>
        TryRestoreRegistered(value, out var canonical) ? canonical : value;

    internal static string Translate(string korean, string english = null)
    {
        if (string.IsNullOrEmpty(korean))
            return korean;
        if (RuntimeChinese.TryGetValue(korean, out var runtime))
            return ForDisplay(runtime);
        if (ExactKorean.TryGetValue(korean, out var exact))
            return ForDisplay(exact);

        foreach (var (pattern, replacement) in Patterns)
        {
            if (pattern.IsMatch(korean))
                return ForDisplay(ApplyPhraseReplacements(pattern.Replace(korean, replacement)));
        }

        var phraseTranslated = ApplyPhraseReplacements(korean);
        if (phraseTranslated != korean)
            return ForDisplay(phraseTranslated);

        if (!string.IsNullOrEmpty(english))
        {
            if (RuntimeChinese.TryGetValue(english, out runtime))
                return ForDisplay(runtime);
            if (ExactEnglish.TryGetValue(english, out exact))
                return ForDisplay(exact);
            foreach (var (pattern, replacement) in EnglishPatterns)
            {
                if (pattern.IsMatch(english))
                    return ForDisplay(pattern.Replace(english, replacement));
            }
        }
        return korean;
    }

    // The game-provided Android font atlas renders several CJK full-width
    // punctuation glyphs as tofu even though Han characters are available.
    // Equivalent ASCII punctuation keeps the reviewed wording readable on all
    // target devices. Android-native TextViews do not pass through this adapter.
    internal static string ForDisplay(string value) =>
        (value ?? "")
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal)
            .Replace("“", "\"", StringComparison.Ordinal)
            .Replace("”", "\"", StringComparison.Ordinal)
            .Replace("‘", "'", StringComparison.Ordinal)
            .Replace("’", "'", StringComparison.Ordinal)
            .Replace("，", ", ", StringComparison.Ordinal)
            .Replace("。", ".", StringComparison.Ordinal)
            .Replace("：", ": ", StringComparison.Ordinal)
            .Replace("；", "; ", StringComparison.Ordinal)
            .Replace("！", "!", StringComparison.Ordinal)
            .Replace("？", "?", StringComparison.Ordinal)
            .Replace("、", ", ", StringComparison.Ordinal);

    // Raw diagnostics remain available in logcat and the optional debug file.
    // The visible Console gets a concise Chinese summary so paths, exception
    // bodies, and implementation details are neither mistranslated nor left as
    // a screen full of English after an in-place language switch.
    internal static string TranslateDiagnostic(string value)
    {
        value ??= "";
        if (!value.StartsWith("[Cloud]", StringComparison.Ordinal))
            return "启动器诊断详情已写入调试日志。";
        if (value.Contains("cloud sync disabled by user", StringComparison.OrdinalIgnoreCase))
            return "[云存档] 云同步已关闭, 正在使用本地存档。";
        if (value.Contains("No saved credentials", StringComparison.OrdinalIgnoreCase))
            return "[云存档] 未检测到登录凭据, 正在使用本地存档。";
        if (value.Contains("ConstructDefaultPrefix", StringComparison.Ordinal))
            return "[云存档] 初始化检查完成。";
        if (value.Contains("SavePathCompat", StringComparison.Ordinal))
            return "[云存档] 存档路径兼容性检查完成。";
        if (
            value.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("threw", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error", StringComparison.OrdinalIgnoreCase)
        )
            return "[云存档] 操作失败, 详细信息已写入调试日志。";
        if (value.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "[云存档] 操作超时, 详细信息已写入调试日志。";
        if (
            value.Contains("Pull", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Download", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Read", StringComparison.OrdinalIgnoreCase)
        )
            return "[云存档] 正在从 Steam Cloud 下载存档…";
        if (
            value.Contains("Push", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Upload", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Wrote", StringComparison.OrdinalIgnoreCase)
            || value.Contains("write", StringComparison.OrdinalIgnoreCase)
        )
            return "[云存档] 正在上传存档到 Steam Cloud…";
        if (
            value.Contains("Flush", StringComparison.OrdinalIgnoreCase)
            || value.Contains("queue", StringComparison.OrdinalIgnoreCase)
        )
            return "[云存档] 正在提交云存档更改…";
        if (
            value.Contains("cache", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Enumerated", StringComparison.OrdinalIgnoreCase)
        )
            return "[云存档] 正在更新云存档索引…";
        if (
            value.Contains("Sync", StringComparison.OrdinalIgnoreCase)
            || value.Contains("decision", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
        )
            return "[云存档] 正在检查云存档同步状态…";
        return "[云存档] 正在处理云存档…";
    }

    internal static bool LooksUntranslated(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || EnglishLocalization.ContainsKorean(value))
            return EnglishLocalization.ContainsKorean(value);
        if (value.Any(ch => ch is >= '\u3400' and <= '\u9FFF'))
            return false;
        if (
            Regex.IsMatch(
                value,
                @"^\d+(?:\.\d+)?\s*(?:B|KB|MB|GB)(?:\s*·\s*—)?$",
                RegexOptions.CultureInvariant
            )
        )
            return false;
        if (!value.Any(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            return false;
        return value
            is not "StS2 Launcher"
                and not "Made using FMOD Studio by Firelight Technologies Pty Ltd.";
    }

    private static string ApplyPhraseReplacements(string value)
    {
        foreach (var (korean, chinese) in PhraseReplacements)
            value = value.Replace(korean, chinese, StringComparison.Ordinal);
        return value;
    }

    private static (Regex Pattern, string Replacement) Make(string pattern, string replacement) =>
        (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline), replacement);
}
