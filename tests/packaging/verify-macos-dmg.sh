#!/usr/bin/env bash

set -euo pipefail

usage() {
  echo "Usage: verify-macos-dmg.sh --dmg-directory PATH [--version X.Y.Z] --expected-signing adhoc|developer-id" >&2
}

dmg_directory=""
version="1.0.0"
expected_signing=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dmg-directory)
      dmg_directory="${2:-}"
      shift 2
      ;;
    --version)
      version="${2:-}"
      shift 2
      ;;
    --expected-signing)
      expected_signing="${2:-}"
      shift 2
      ;;
    *)
      usage
      exit 2
      ;;
  esac
done

if [[ -z "$dmg_directory" || ( "$expected_signing" != "adhoc" && "$expected_signing" != "developer-id" ) ]]; then
  usage
  exit 2
fi

metadata_path="$dmg_directory/macos-dmg-metadata.json"
if [[ ! -f "$metadata_path" ]]; then
  echo "DMG metadata is missing." >&2
  exit 1
fi

metadata_value() {
  plutil -extract "$1" raw -o - "$metadata_path"
}

if [[ "$(metadata_value product)" != "Snaploom" || \
      "$(metadata_value bundleId)" != "com.snaploom.app" || \
      "$(metadata_value version)" != "$version" || \
      "$(metadata_value runtime)" != "osx-arm64" || \
      "$(metadata_value selfContained)" != "true" || \
      "$(metadata_value signingMode)" != "$expected_signing" ]]; then
  echo "DMG metadata does not match the macOS distribution contract." >&2
  exit 1
fi

expected_notarized=false
if [[ "$expected_signing" == "developer-id" ]]; then
  expected_notarized=true
fi
if [[ "$(metadata_value notarized)" != "$expected_notarized" ]]; then
  echo "DMG notarization metadata does not match its signing mode." >&2
  exit 1
fi

dmg_name="$(metadata_value diskImage)"
dmg_path="$dmg_directory/$dmg_name"
if [[ ! -f "$dmg_path" ]]; then
  echo "The DMG named by the metadata is missing." >&2
  exit 1
fi

dmg_bytes="$(stat -f%z "$dmg_path")"
if [[ "$dmg_bytes" != "$(metadata_value diskImageBytes)" || \
      "$dmg_bytes" -gt "$(metadata_value maxDiskImageBytes)" || \
      "$(metadata_value maxDiskImageBytes)" != "50000000" ]]; then
  echo "DMG size metadata is invalid or exceeds 50 MB." >&2
  exit 1
fi

actual_hash="$(shasum -a 256 "$dmg_path" | awk '{print $1}')"
expected_checksum="$actual_hash  $dmg_name"
actual_checksum="$(tr -d '\r\n' < "$dmg_path.sha256")"
if [[ "$actual_hash" != "$(metadata_value sha256)" || "$actual_checksum" != "$expected_checksum" ]]; then
  echo "DMG SHA256 metadata does not match the generated image." >&2
  exit 1
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/snaploom-dmg-verification.XXXXXX")"
mount_path="$temporary_root/volume"
install_path="$temporary_root/Applications/Snaploom.app"
app_process=""
mounted=false

cleanup() {
  if [[ -n "$app_process" ]] && kill -0 "$app_process" 2>/dev/null; then
    kill "$app_process" 2>/dev/null || true
    wait "$app_process" 2>/dev/null || true
  fi
  if [[ "$mounted" == true ]]; then
    hdiutil detach "$mount_path" -quiet || true
  fi
  rm -rf "$temporary_root"
}
trap cleanup EXIT

mkdir -p "$mount_path" "$(dirname "$install_path")" "$temporary_root/home"
hdiutil attach "$dmg_path" -readonly -nobrowse -mountpoint "$mount_path" -quiet
mounted=true

source_app="$mount_path/Snaploom.app"
if [[ ! -d "$source_app" || ! -L "$mount_path/Applications" || "$(readlink "$mount_path/Applications")" != "/Applications" ]]; then
  echo "The DMG must contain Snaploom.app and an Applications shortcut." >&2
  exit 1
fi

ditto "$source_app" "$install_path"
info_plist="$install_path/Contents/Info.plist"
if [[ "$(plutil -extract CFBundleIdentifier raw -o - "$info_plist")" != "com.snaploom.app" || \
      "$(plutil -extract CFBundleShortVersionString raw -o - "$info_plist")" != "$version" || \
      "$(plutil -extract CFBundleIconFile raw -o - "$info_plist")" != "Snaploom" || \
      -z "$(plutil -extract NSScreenCaptureUsageDescription raw -o - "$info_plist")" ]]; then
  echo "The application bundle metadata is incomplete." >&2
  exit 1
fi

for localized_info in \
  "$install_path/Contents/Resources/en.lproj/InfoPlist.strings" \
  "$install_path/Contents/Resources/zh-Hans.lproj/InfoPlist.strings"; do
  if [[ ! -f "$localized_info" || -z "$(plutil -extract NSScreenCaptureUsageDescription raw -o - "$localized_info")" ]]; then
    echo "A localized screen capture permission description is missing." >&2
    exit 1
  fi
done

if [[ ! -f "$install_path/Contents/Resources/Snaploom.icns" ]]; then
  echo "The application icon is missing." >&2
  exit 1
fi

codesign --verify --deep --strict "$install_path"
entitlements_path="$temporary_root/entitlements.plist"
codesign -d --entitlements :- "$install_path" > "$entitlements_path" 2>/dev/null
if [[ "$(plutil -extract 'com\.apple\.security\.cs\.allow-jit' raw -o - "$entitlements_path")" != "true" ]]; then
  echo "The JIT entitlement required by the self-contained .NET runtime is missing." >&2
  exit 1
fi
if plutil -extract 'com\.apple\.security\.get-task-allow' raw -o - "$entitlements_path" >/dev/null 2>&1; then
  echo "The release application must not contain get-task-allow." >&2
  exit 1
fi
if [[ "$expected_signing" == "adhoc" ]]; then
  if [[ "$(plutil -extract 'com\.apple\.security\.cs\.disable-library-validation' raw -o - "$entitlements_path")" != "true" ]]; then
    echo "The ad hoc CI package must explicitly allow libraries without a Team ID." >&2
    exit 1
  fi
elif plutil -extract 'com\.apple\.security\.cs\.disable-library-validation' raw -o - "$entitlements_path" >/dev/null 2>&1; then
  echo "The Developer ID package must keep library validation enabled." >&2
  exit 1
fi

mach_o_count=0
while IFS= read -r -d '' candidate; do
  if file -b "$candidate" | grep -q 'Mach-O'; then
    architectures="$(lipo -archs "$candidate")"
    if [[ "$architectures" != "arm64" ]]; then
      echo "Non-arm64 or universal Mach-O file found in the DMG." >&2
      exit 1
    fi
    mach_o_count=$((mach_o_count + 1))
  fi
done < <(find "$install_path" -type f -print0)
if [[ $mach_o_count -eq 0 ]]; then
  echo "The installed application contains no Mach-O executables." >&2
  exit 1
fi

if [[ "$expected_signing" == "developer-id" ]]; then
  if ! codesign -dv --verbose=4 "$install_path" 2>&1 | grep -q '^Authority=Developer ID Application:'; then
    echo "The application is not signed with Developer ID Application." >&2
    exit 1
  fi
  xcrun stapler validate "$dmg_path"
  spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg_path"
  spctl --assess --type execute --verbose=2 "$install_path"
fi

HOME="$temporary_root/home" "$install_path/Contents/MacOS/Snaploom.App" \
  > "$temporary_root/launch.log" 2>&1 &
app_process=$!
sleep 5
if ! kill -0 "$app_process" 2>/dev/null; then
  wait "$app_process" || true
  echo "Installed Snaploom did not remain running." >&2
  exit 1
fi
kill "$app_process"
wait "$app_process" 2>/dev/null || true
app_process=""

rm -rf "$install_path"
if [[ -e "$install_path" ]]; then
  echo "Uninstall verification left the application bundle behind." >&2
  exit 1
fi

echo "macOS DMG metadata, architecture, signing, install, launch, and uninstall checks passed."
