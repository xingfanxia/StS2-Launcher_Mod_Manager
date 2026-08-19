#!/usr/bin/env swift

import Foundation
import Vision

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("ERROR: \(message)\n".utf8))
    exit(1)
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard let imageArgument = arguments.first else {
    fail("usage: audit-screenshot.swift IMAGE [--require-no-hangul] [--require-no-tofu] [--require-chinese] [--locate-language-selector] [--locate-game-continue]")
}

let imageUrl = URL(fileURLWithPath: imageArgument)
guard FileManager.default.fileExists(atPath: imageUrl.path) else {
    fail("image does not exist")
}

func recognize(_ languages: [String]) -> [VNRecognizedTextObservation] {
    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    request.usesLanguageCorrection = true
    request.recognitionLanguages = languages
    do {
        try VNImageRequestHandler(url: imageUrl).perform([request])
    } catch {
        fail("Vision text recognition failed")
    }
    return request.results ?? []
}

func recognize(_ language: String) -> [VNRecognizedTextObservation] {
    recognize([language])
}

func recognizeAutomatically() -> [VNRecognizedTextObservation] {
    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    request.usesLanguageCorrection = false
    do {
        try VNImageRequestHandler(url: imageUrl).perform([request])
    } catch {
        fail("Vision automatic text recognition failed")
    }
    return request.results ?? []
}

let englishObservations = recognize("en-US")
let koreanObservations = recognize("ko-KR")
// Forced Korean OCR can hallucinate Hangul for real Han glyphs at low
// confidence. The unforced pass plus high-confidence Korean candidates provide
// the residue decision; language-specific passes remain available for locators.
let automaticObservations = recognizeAutomatically()
let needsChineseRecognition = arguments.contains("--locate-game-continue")
    || arguments.contains("--require-chinese")
    || arguments.contains("--locate-language-selector")
    || arguments.contains("--locate-branch-picker")
    || arguments.contains("--locate-safe-mode")
    || arguments.contains("--locate-compatibility-mode")
let chineseObservations = needsChineseRecognition
    ? recognize("zh-Hans") : []
let hangul = try! NSRegularExpression(pattern: "[가-힣]")
let cjk = try! NSRegularExpression(pattern: "[\u{3400}-\u{9FFF}]")
let tofu = try! NSRegularExpression(pattern: "[□▢▣�]")
var hangulLines = 0
var forcedKoreanHangulLines = 0
var forcedKoreanHangulConfidenceMax: Float = 0
var simplifiedChineseLines = 0
var tofuLines = 0
var edgeClippedLines = 0
var safeModeCenter: (CGFloat, CGFloat)?
var compatibilityModeCenter: (CGFloat, CGFloat)?
var branchPickerTitleFound = false
var publicBranchCenter: (CGFloat, CGFloat)?
var publicBetaBranchCenter: (CGFloat, CGFloat)?
var branchPickerOkCenter: (CGFloat, CGFloat)?
var gameContinueCenter: (CGFloat, CGFloat)?
var languageSelectorCenter: (CGFloat, CGFloat)?

for observation in automaticObservations {
    guard let text = observation.topCandidates(1).first?.string else { continue }
    let range = NSRange(text.startIndex..., in: text)
    if hangul.firstMatch(in: text, range: range) != nil {
        hangulLines += 1
    }
    if tofu.firstMatch(in: text, range: range) != nil {
        tofuLines += 1
    }
}

for observation in koreanObservations {
    guard let candidate = observation.topCandidates(1).first else { continue }
    let text = candidate.string
    let range = NSRange(text.startIndex..., in: text)
    if hangul.firstMatch(in: text, range: range) != nil {
        forcedKoreanHangulLines += 1
        forcedKoreanHangulConfidenceMax = max(forcedKoreanHangulConfidenceMax, candidate.confidence)
        if candidate.confidence >= 0.8 {
            hangulLines += 1
        }
    }
    if arguments.contains("--locate-safe-mode"),
       text.replacingOccurrences(of: " ", with: "").contains("안전모드로계속") {
        let box = observation.boundingBox
        safeModeCenter = (box.midX, 1.0 - box.midY)
    }
}

for observation in englishObservations {
    let box = observation.boundingBox
    let normalizedCenter = (box.midX, 1.0 - box.midY)
    if box.minX < 0.002 || box.maxX > 0.998 || box.minY < 0.002 || box.maxY > 0.998 {
        edgeClippedLines += 1
    }
    if arguments.contains("--locate-safe-mode"),
       let text = observation.topCandidates(1).first?.string.lowercased(),
       text.contains("continue in safe mode") {
        safeModeCenter = normalizedCenter
    }
    if arguments.contains("--locate-compatibility-mode"),
       let text = observation.topCandidates(1).first?.string.lowercased(),
       text.contains("continue in compatibility mode") {
        compatibilityModeCenter = normalizedCenter
    }
    if arguments.contains("--locate-branch-picker"),
       let text = observation.topCandidates(1).first?.string.lowercased() {
        if text.contains("select game version") {
            branchPickerTitleFound = true
        } else if text.contains("public-beta") {
            publicBetaBranchCenter = normalizedCenter
        } else if text == "public" || (text.contains("public") && text.contains("current")) {
            publicBranchCenter = normalizedCenter
        } else if text == "ok" {
            branchPickerOkCenter = normalizedCenter
        }
    }
}

for observation in chineseObservations {
    guard let text = observation.topCandidates(1).first?.string else { continue }
    let range = NSRange(text.startIndex..., in: text)
    if cjk.firstMatch(in: text, range: range) != nil {
        simplifiedChineseLines += 1
    }
    if tofu.firstMatch(in: text, range: range) != nil {
        tofuLines += 1
    }
    let compact = text.replacingOccurrences(of: " ", with: "")
    let box = observation.boundingBox
    let normalizedCenter = (box.midX, 1.0 - box.midY)
    if arguments.contains("--locate-safe-mode"),
       compact.contains("安全模式继续") {
        safeModeCenter = normalizedCenter
    }
    if arguments.contains("--locate-compatibility-mode"),
       compact.contains("兼容模式继续") {
        compatibilityModeCenter = normalizedCenter
    }
}

if arguments.contains("--locate-branch-picker") {
    for observation in chineseObservations {
        guard let text = observation.topCandidates(1).first?.string else { continue }
        let compact = text.replacingOccurrences(of: " ", with: "")
        let box = observation.boundingBox
        let normalizedCenter = (box.midX, 1.0 - box.midY)
        if compact.contains("选择游戏版本") {
            branchPickerTitleFound = true
        } else if compact == "确定" {
            branchPickerOkCenter = normalizedCenter
        }
    }
}

if arguments.contains("--locate-game-continue") {
    for observation in englishObservations + koreanObservations + chineseObservations {
        guard let candidate = observation.topCandidates(1).first?.string else { continue }
        let compact = candidate
            .lowercased()
            .replacingOccurrences(of: " ", with: "")
        let isContinue = compact == "continue"
            || compact == "continuegame"
            || compact == "继续游戏"
            || compact == "繼續遊戲"
            || compact == "계속"
            || compact == "계속하기"
        if !isContinue { continue }
        let box = observation.boundingBox
        let center = (box.midX, 1.0 - box.midY)
        // The game main-menu action column is central and above the lower
        // settings/quit actions. Reject edge/system text and dialog buttons.
        if center.0 >= 0.20 && center.0 <= 0.80
            && center.1 >= 0.35 && center.1 <= 0.75 {
            gameContinueCenter = center
            break
        }
    }
}

if arguments.contains("--locate-language-selector") {
    for observation in chineseObservations {
        guard let candidate = observation.topCandidates(1).first?.string else { continue }
        let compact = candidate.replacingOccurrences(of: " ", with: "")
        if !compact.contains("简体中文") { continue }
        let box = observation.boundingBox
        let center = (box.midX, 1.0 - box.midY)
        // The title-row selector lives in the central-left header. Exclude a
        // transient status sentence such as "已切换到简体中文。" below it.
        if center.0 < 0.30 || center.0 > 0.60 { continue }
        // When the popup is open both the closed selection and the menu item
        // are visible. Prefer the lower menu item; otherwise keep the only hit.
        if languageSelectorCenter == nil || center.1 > languageSelectorCenter!.1 {
            languageSelectorCenter = center
        }
    }
}

print("recognized_lines_en=\(englishObservations.count)")
print("recognized_lines_ko=\(koreanObservations.count)")
print("recognized_lines_auto=\(automaticObservations.count)")
print("recognized_lines_zh=\(chineseObservations.count)")
print("simplified_chinese_lines=\(simplifiedChineseLines)")
print("hangul_lines=\(hangulLines)")
print("forced_ko_hangul_lines=\(forcedKoreanHangulLines)")
print(String(format: "forced_ko_hangul_confidence_max=%.3f", forcedKoreanHangulConfidenceMax))
print("tofu_lines=\(tofuLines)")
print("edge_clipped_lines=\(edgeClippedLines)")
if arguments.contains("--locate-safe-mode") {
    guard let center = safeModeCenter else {
        fail("Safe Mode button label was not found")
    }
    print(String(format: "safe_mode_center_normalized=%.6f,%.6f", center.0, center.1))
}
if arguments.contains("--locate-compatibility-mode") {
    guard let center = compatibilityModeCenter else {
        fail("compatibility-mode button label was not found")
    }
    print(String(format: "compatibility_mode_center_normalized=%.6f,%.6f", center.0, center.1))
}
if arguments.contains("--locate-branch-picker") {
    guard branchPickerTitleFound,
          let publicCenter = publicBranchCenter,
          let betaCenter = publicBetaBranchCenter,
          let okCenter = branchPickerOkCenter else {
        fail("complete branch picker was not found")
    }
    print(String(format: "public_branch_center_normalized=%.6f,%.6f", publicCenter.0, publicCenter.1))
    print(String(format: "public_beta_branch_center_normalized=%.6f,%.6f", betaCenter.0, betaCenter.1))
    print(String(format: "branch_picker_ok_center_normalized=%.6f,%.6f", okCenter.0, okCenter.1))
}
if arguments.contains("--locate-game-continue") {
    guard let center = gameContinueCenter else {
        fail("game Continue action was not found")
    }
    print(String(format: "game_continue_center_normalized=%.6f,%.6f", center.0, center.1))
}
if arguments.contains("--locate-language-selector") {
    guard let center = languageSelectorCenter else {
        fail("Simplified Chinese language selector was not found")
    }
    print(String(format: "language_selector_center_normalized=%.6f,%.6f", center.0, center.1))
}

if arguments.contains("--require-no-hangul") && hangulLines > 0 {
    fail("visible Hangul detected; inspect runtime provenance before classifying it")
}
if arguments.contains("--require-no-tofu") && tofuLines > 0 {
    fail("possible tofu glyph detected; inspect the screenshot and font fallback")
}
if arguments.contains("--require-chinese") && simplifiedChineseLines == 0 {
    fail("no visible Simplified Chinese text was recognized")
}
