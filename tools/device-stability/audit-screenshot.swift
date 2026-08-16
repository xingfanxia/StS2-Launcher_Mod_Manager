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

for observation in koreanObservations {
    guard let text = observation.topCandidates(1).first?.string else { continue }
    let range = NSRange(text.startIndex..., in: text)
    if hangul.firstMatch(in: text, range: range) != nil {
        hangulLines += 1
    }
}

for observation in englishObservations {
    let box = observation.boundingBox
    if box.minX < 0.002 || box.maxX > 0.998 || box.minY < 0.002 || box.maxY > 0.998 {
        edgeClippedLines += 1
    }
}

print("recognized_lines_en=\(englishObservations.count)")
print("recognized_lines_ko=\(koreanObservations.count)")
print("hangul_lines=\(hangulLines)")
print("edge_clipped_lines=\(edgeClippedLines)")

if arguments.contains("--require-no-hangul") && hangulLines > 0 {
    fail("visible Hangul detected; inspect runtime provenance before classifying it")
}
