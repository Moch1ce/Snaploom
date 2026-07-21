#!/usr/bin/env bash
# Copyright (C) 2026 Snaploom contributors
# SPDX-License-Identifier: GPL-3.0-or-later

set -euo pipefail

archive=""
version=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --archive) archive="${2:-}"; shift 2 ;;
    --version) version="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -s "$archive" && "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || exit 2
listing="$(tar --zstd -tf "$archive")"
prefix="snaploom-$version-source"
for required in \
  Cargo.toml Cargo.lock product/Cargo.toml product/Cargo.lock sdk/Cargo.toml sdk/Cargo.lock \
  package.json pnpm-lock.yaml Package.swift REUSE.toml rust-toolchain.toml \
  sdk/protocol/proto/capture.proto scripts/build-macos-packages.sh \
  scripts/build-windows-packages.ps1 packaging/windows/Snaploom.iss \
  docs/distribution/build-from-source.md; do
  grep -Fxq "$prefix/$required" <<< "$listing" || {
    echo "Corresponding source is missing $required" >&2
    exit 1
  }
done
if grep -Eiq '(^|/)(\.env|id_rsa|.*\.pfx|.*\.p12|node_modules|target|artifacts)(/|$)' <<< "$listing"; then
  echo "Corresponding source contains a secret or build-output path." >&2
  exit 1
fi
echo "Corresponding source contains locked source/build/package inputs and no forbidden output paths."
