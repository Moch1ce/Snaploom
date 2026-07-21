// swift-tools-version: 5.9

import PackageDescription

let snaploomBinaryChecksum = "1eab4bcf9585acf4559010057c5b8c9dc367da527e62c231596fea655af5140e"

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
