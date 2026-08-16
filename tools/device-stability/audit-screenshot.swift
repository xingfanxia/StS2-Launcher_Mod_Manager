#!/usr/bin/env swift

import Foundation
import Vision

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("ERROR: \(message)\n".utf8))
    exit(1)
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard let imageArgument = arguments.first else {
    fail("usage: audit-screenshot.swift IMAGE [--require-no-hangul]")
}

let imageUrl = URL(fileURLWithPath: imageArgument)
guard FileManager.default.fileExists(atPath: imageUrl.path) else {
    fail("image does not exist")
}

func recognize(_ language: String) -> [VNRecognizedTextObservation] {
    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    request.usesLanguageCorrection = true
    request.recognitionLanguages = [language]
    do {
        try VNImageRequestHandler(url: imageUrl).perform([request])
    } catch {
        fail("Vision text recognition failed")
    }
    return request.results ?? []
}

let englishObservations = recognize("en-US")
let koreanObservations = recognize("ko-KR")
let hangul = try! NSRegularExpression(pattern: "[가-힣]")
var hangulLines = 0
var edgeClippedLines = 0
var safeModeCenter: (CGFloat, CGFloat)?
var compatibilityModeCenter: (CGFloat, CGFloat)?
var branchPickerTitleFound = false
var publicBranchCenter: (CGFloat, CGFloat)?
var publicBetaBranchCenter: (CGFloat, CGFloat)?
var branchPickerOkCenter: (CGFloat, CGFloat)?

for observation in koreanObservations {
    guard let text = observation.topCandidates(1).first?.string else { continue }
    let range = NSRange(text.startIndex..., in: text)
    if hangul.firstMatch(in: text, range: range) != nil {
        hangulLines += 1
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

print("recognized_lines_en=\(englishObservations.count)")
print("recognized_lines_ko=\(koreanObservations.count)")
print("hangul_lines=\(hangulLines)")
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

if arguments.contains("--require-no-hangul") && hangulLines > 0 {
    fail("visible Hangul detected; inspect runtime provenance before classifying it")
}
