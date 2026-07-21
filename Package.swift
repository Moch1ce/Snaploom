// swift-tools-version: 5.9

import PackageDescription

let snaploomBinaryChecksum = "aecac7aa9a53e94d34d6ef6be470be103a7cb9c8d50c0cb67b78f6a9ddbac0df"

let package = Package(
  name: "SnaploomCapture",
  platforms: [
    .macOS(.v14)
  ],
  products: [
    .library(name: "SnaploomCapture", targets: ["SnaploomCapture"])
  ],
  targets: [
    .binaryTarget(
      name: "CSnaploomCapture",
      url:
        "https://github.com/Moch1ce/Snaploom/releases/download/v0.1.0/CSnaploomCapture-0.1.0.xcframework.zip",
      checksum: snaploomBinaryChecksum
    ),
    .target(
      name: "SnaploomCapture",
      dependencies: ["CSnaploomCapture"],
      path: "sdk/swift/Sources/SnaploomCapture"
    ),
    .testTarget(
      name: "SnaploomCaptureTests",
      dependencies: ["SnaploomCapture"],
      path: "sdk/swift/Tests/SnaploomCaptureTests"
    ),
  ]
)
