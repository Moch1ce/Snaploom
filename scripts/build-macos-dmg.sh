#!/usr/bin/env bash

set -euo pipefail

usage() {
  echo "Usage: build-macos-dmg.sh [--version X.Y.Z] [--configuration Debug|Release] (--adhoc | --developer-id IDENTITY --notarize)" >&2
}

version="1.0.0"
configuration="Release"
signing_mode=""
developer_id=""
notarize=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version="${2:-}"
      shift 2
      ;;
    --configuration)
      configuration="${2:-}"
      shift 2
      ;;
    --adhoc)
      signing_mode="adhoc"
      shift
      ;;
    --developer-id)
      signing_mode="developer-id"
      developer_id="${2:-}"
      shift 2
      ;;
    --notarize)
      notarize=true
      shift
      ;;
    *)
      usage
      exit 2
      ;;
  esac
done

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must use X.Y.Z format." >&2
  exit 2
fi

if [[ "$configuration" != "Debug" && "$configuration" != "Release" ]]; then
  echo "Configuration must be Debug or Release." >&2
  exit 2
fi

if [[ "$signing_mode" == "developer-id" && "$notarize" != true ]]; then
  echo "Developer ID packages must include --notarize; production builds cannot silently skip notarization." >&2
  exit 2
fi

if [[ "$signing_mode" == "adhoc" && "$notarize" == true ]]; then
  echo "Ad hoc packages cannot be notarized." >&2
  exit 2
fi

if [[ -z "$signing_mode" ]]; then
  usage
  exit 2
fi

if [[ "$signing_mode" == "developer-id" ]]; then
  : "${APPLE_NOTARY_KEY_PATH:?APPLE_NOTARY_KEY_PATH is required for notarization.}"
  : "${APPLE_NOTARY_KEY_ID:?APPLE_NOTARY_KEY_ID is required for notarization.}"
  : "${APPLE_NOTARY_ISSUER_ID:?APPLE_NOTARY_ISSUER_ID is required for notarization.}"
  if [[ ! -f "$APPLE_NOTARY_KEY_PATH" ]]; then
    echo "APPLE_NOTARY_KEY_PATH does not point to a file." >&2
    exit 2
  fi

  if ! security find-identity -v -p codesigning | grep -Fq "\"$developer_id\""; then
    echo "The requested Developer ID Application identity is not available in the keychain." >&2
    exit 2
  fi
fi

for command in dotnet iconutil plutil codesign hdiutil lipo file shasum xcrun; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $command" >&2
    exit 1
  fi
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_root="$repo_root/artifacts/macos-arm64"
publish_directory="$artifact_root/publish"
dmg_directory="$artifact_root/dmg"
app_path="$publish_directory/bundle/Snaploom.app"
dmg_name="snaploom-$version-macos-arm64.dmg"
dmg_path="$dmg_directory/$dmg_name"
metadata_path="$dmg_directory/macos-dmg-metadata.json"
entitlements_path="$repo_root/packaging/macos/Snaploom.entitlements"
adhoc_entitlements_path="$repo_root/packaging/macos/Snaploom.AdHoc.entitlements"
max_dmg_bytes=50000000
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/snaploom-dmg.XXXXXX")"
iconset_path="$temporary_root/Snaploom.iconset"
staging_path="$temporary_root/volume"

cleanup() {
  rm -rf "$temporary_root"
}
trap cleanup EXIT

rm -rf "$artifact_root"
mkdir -p "$publish_directory" "$dmg_directory" "$staging_path"

dotnet run \
  --project "$repo_root/tools/Snaploom.AssetGenerator/Snaploom.AssetGenerator.csproj" \
  --configuration "$configuration" \
  -- macos-iconset "$iconset_path"
iconutil -c icns -o "$repo_root/src/Snaploom.App/Assets/Snaploom.icns" "$iconset_path"

dotnet publish "$repo_root/src/Snaploom.App/Snaploom.App.csproj" \
  --configuration "$configuration" \
  --runtime osx-arm64 \
  --self-contained true \
  --output "$publish_directory" \
  -p:Version="$version" \
  -p:FileVersion="$version.0" \
  -p:InformationalVersion="$version" \
  -p:DebugSymbols=false \
  -p:DebugType=None

if [[ ! -d "$app_path" ]]; then
  echo "The macOS application bundle was not created." >&2
  exit 1
fi

find "$publish_directory" -type f -name '*.pdb' -delete
plutil -replace CFBundleVersion -string "$version" "$app_path/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$app_path/Contents/Info.plist"
xattr -cr "$app_path"

mach_o_count=0
while IFS= read -r -d '' candidate; do
  if file -b "$candidate" | grep -q 'Mach-O'; then
    architectures="$(lipo -archs "$candidate")"
    if [[ " $architectures " == *" arm64 "* && "$architectures" != "arm64" ]]; then
      lipo -thin arm64 "$candidate" -output "$candidate.arm64"
      mv "$candidate.arm64" "$candidate"
      architectures="$(lipo -archs "$candidate")"
    fi
    if [[ "$architectures" != "arm64" ]]; then
      echo "Mach-O file has no supported arm64 slice: ${candidate#"$app_path"/} ($architectures)" >&2
      exit 1
    fi
    mach_o_count=$((mach_o_count + 1))
  fi
done < <(find "$app_path" -type f -print0)

if [[ $mach_o_count -eq 0 ]]; then
  echo "The application bundle contains no Mach-O executables." >&2
  exit 1
fi

if [[ "$signing_mode" == "developer-id" ]]; then
  app_signing_arguments=(--force --options runtime --timestamp --sign "$developer_id")
  signing_entitlements_path="$entitlements_path"
else
  app_signing_arguments=(--force --options runtime --sign -)
  signing_entitlements_path="$adhoc_entitlements_path"
fi

main_executable="$app_path/Contents/MacOS/Snaploom.App"
while IFS= read -r -d '' candidate; do
  if [[ "$candidate" != "$main_executable" && "$candidate" == "$app_path/Contents/MacOS/"* ]]; then
    codesign "${app_signing_arguments[@]}" "$candidate"
  fi
done < <(find "$app_path" -type f -print0)

codesign "${app_signing_arguments[@]}" --entitlements "$signing_entitlements_path" "$app_path"
codesign --verify --deep --strict "$app_path"

ditto "$app_path" "$staging_path/Snaploom.app"
ln -s /Applications "$staging_path/Applications"
hdiutil create \
  -volname "Snaploom" \
  -srcfolder "$staging_path" \
  -format UDZO \
  -ov \
  "$dmg_path"

if [[ "$signing_mode" == "developer-id" ]]; then
  codesign --force --timestamp --sign "$developer_id" "$dmg_path"
else
  codesign --force --sign - "$dmg_path"
fi
codesign --verify "$dmg_path"

if [[ "$notarize" == true ]]; then
  notarization_result="$temporary_root/notarization.json"
  xcrun notarytool submit "$dmg_path" \
    --key "$APPLE_NOTARY_KEY_PATH" \
    --key-id "$APPLE_NOTARY_KEY_ID" \
    --issuer "$APPLE_NOTARY_ISSUER_ID" \
    --wait \
    --output-format json > "$notarization_result"

  notarization_status="$(plutil -extract status raw -o - "$notarization_result")"
  if [[ "$notarization_status" != "Accepted" ]]; then
    submission_id="$(plutil -extract id raw -o - "$notarization_result")"
    echo "Notarization failed with status '$notarization_status' (submission $submission_id)." >&2
    exit 1
  fi

  xcrun stapler staple "$dmg_path"
  xcrun stapler validate "$dmg_path"
  spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg_path"
fi

dmg_bytes="$(stat -f%z "$dmg_path")"
if [[ "$dmg_bytes" -gt "$max_dmg_bytes" ]]; then
  echo "DMG is $dmg_bytes bytes; the 50 MB limit is $max_dmg_bytes bytes." >&2
  exit 1
fi

sha256="$(shasum -a 256 "$dmg_path" | awk '{print $1}')"
printf '%s  %s\n' "$sha256" "$dmg_name" > "$dmg_path.sha256"

plutil -create xml1 "$metadata_path"
plutil -insert schemaVersion -integer 1 "$metadata_path"
plutil -insert product -string Snaploom "$metadata_path"
plutil -insert bundleId -string com.snaploom.app "$metadata_path"
plutil -insert version -string "$version" "$metadata_path"
plutil -insert runtime -string osx-arm64 "$metadata_path"
plutil -insert selfContained -bool true "$metadata_path"
plutil -insert diskImage -string "$dmg_name" "$metadata_path"
plutil -insert diskImageBytes -integer "$dmg_bytes" "$metadata_path"
plutil -insert maxDiskImageBytes -integer "$max_dmg_bytes" "$metadata_path"
plutil -insert sha256 -string "$sha256" "$metadata_path"
plutil -insert signingMode -string "$signing_mode" "$metadata_path"
plutil -insert notarized -bool "$notarize" "$metadata_path"
plutil -convert json "$metadata_path"

echo "Created $dmg_name ($dmg_bytes bytes)."
echo "SHA256: $sha256"
