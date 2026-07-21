// swift-tools-version: 5.9

import PackageDescription

let snaploomBinaryChecksum = "e921da8ad65de9f7e714f01e80bf5967cd89343781f493ceb028f372636e58c0"

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
