// swift-tools-version: 5.9

import PackageDescription

let snaploomBinaryChecksum = "2d6eaa8cf7e3a3b0441e626cc61520a67873d4bbddc5ef29316c1fe9d9a5ab42"

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
