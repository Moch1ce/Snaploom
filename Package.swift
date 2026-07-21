// swift-tools-version: 5.9

import PackageDescription

let snaploomBinaryChecksum = "0079cfcd4816da0f6d3325ca0a423287c7149bd205504859dd3b6f951cfbb2af"

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
