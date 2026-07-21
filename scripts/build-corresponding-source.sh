#!/usr/bin/env bash
# Copyright (C) 2026 Snaploom contributors
# SPDX-License-Identifier: GPL-3.0-or-later

set -euo pipefail

version=""
commit=""
output=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:-}"; shift 2 ;;
    --commit) commit="${2:-}"; shift 2 ;;
    --output) output="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || ! "$commit" =~ ^[0-9a-f]{40}$ || -z "$output" ]]; then
  echo "--version X.Y.Z, --commit SHA, and --output FILE are required." >&2
  exit 2
fi
command -v git >/dev/null
command -v zstd >/dev/null
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ "$(git -C "$repo_root" rev-parse "$commit^{commit}")" == "$commit" ]] || {
  echo "Corresponding source commit cannot be resolved exactly." >&2
  exit 1
}
node "$repo_root/tools/release/check-release-version.mjs" --version "$version"
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/snaploom-source.XXXXXX")"
trap 'rm -rf "$temporary_root"' EXIT
source_tar="$temporary_root/source.tar"
git -C "$repo_root" archive \
  --format=tar \
  --prefix="snaploom-$version-source/" \
  --output="$source_tar" \
  "$commit"
mkdir -p "$(dirname "$output")"
zstd -19 --threads=1 --no-progress -f "$source_tar" -o "$output"
echo "$output"
