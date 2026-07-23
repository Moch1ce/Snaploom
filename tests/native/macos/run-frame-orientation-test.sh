#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../../.." && pwd)"
test_output="${TMPDIR:-/tmp}/snaploom-frame-orientation-test"

xcrun swiftc \
  "$repo_root/src/Snaploom.Platform.MacOS/Native/SnaploomMacOSBridge.swift" \
  "$repo_root/tests/native/macos/FrameOrientationTest.swift" \
  -swift-version 5 \
  -target arm64-apple-macos14.0 \
  -framework AppKit \
  -framework Carbon \
  -framework CoreGraphics \
  -framework CoreVideo \
  -framework ScreenCaptureKit \
  -framework UniformTypeIdentifiers \
  -o "$test_output"

"$test_output"
