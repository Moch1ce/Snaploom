// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

export const RELEASE_SCHEMA_VERSION = 1;
export const MAX_INSTALLER_BYTES = 50_000_000;
export const WINDOWS_UNSIGNED_RISK_LANGUAGE =
  "Windows executables and DLLs are unsigned and may show Unknown publisher or Microsoft Defender SmartScreen warnings; verify SHA256SUMS and GitHub attestations before running them.";
export const MACOS_UNSIGNED_RISK_LANGUAGE =
  "macOS apps and disk images are not signed with Apple Developer ID or notarized and may be blocked by Gatekeeper; verify SHA256SUMS and GitHub attestations before opening them.";

export function assertVersion(version) {
  if (!/^\d+\.\d+\.\d+$/.test(version)) {
    throw new Error(`release version must use X.Y.Z: ${version}`);
  }
}

export function assetsForVersion(version) {
  assertVersion(version);
  return [
    {
      name: `snaploom-${version}-windows-x64-setup.exe`,
      kind: "desktop-installer",
      license: "GPL-3.0-or-later",
      platform: "windows-x64",
      signing: "windows",
      maxBytes: MAX_INSTALLER_BYTES,
    },
    {
      name: `snaploom-${version}-macos-arm64.dmg`,
      kind: "desktop-disk-image",
      license: "GPL-3.0-or-later",
      platform: "macos-arm64",
      signing: "macos",
      maxBytes: MAX_INSTALLER_BYTES,
    },
    {
      name: `snaploom-capture-host-${version}-windows-x64.zip`,
      kind: "capture-host",
      license: "GPL-3.0-or-later",
      platform: "windows-x64",
      signing: "windows",
    },
    {
      name: `snaploom-capture-host-${version}-macos-arm64.zip`,
      kind: "capture-host",
      license: "GPL-3.0-or-later",
      platform: "macos-arm64",
      signing: "macos",
    },
    {
      name: `snaploom-capture-sdk-c-${version}-windows-x64.zip`,
      kind: "capture-sdk-c",
      license: "Apache-2.0",
      platform: "windows-x64",
      signing: "windows-sdk",
    },
    {
      name: `snaploom-capture-sdk-c-${version}-macos-arm64.tar.gz`,
      kind: "capture-sdk-c",
      license: "Apache-2.0",
      platform: "macos-arm64",
      signing: "resignable-sdk",
    },
    {
      name: `Snaploom.Capture.${version}.nupkg`,
      kind: "capture-sdk-dotnet",
      license: "Apache-2.0",
      platform: "multi-platform",
      signing: "windows-sdk",
    },
    {
      name: `Snaploom.Capture.${version}.snupkg`,
      kind: "capture-sdk-symbols",
      license: "Apache-2.0",
      platform: "multi-platform",
      signing: "checksum-only",
    },
    {
      name: `CSnaploomCapture-${version}.xcframework.zip`,
      kind: "capture-sdk-swift",
      license: "Apache-2.0",
      platform: "macos-arm64",
      signing: "resignable-sdk",
    },
    {
      name: `snaploom-${version}-corresponding-source.tar.zst`,
      kind: "corresponding-source",
      license: "GPL-3.0-or-later AND Apache-2.0",
      platform: "source",
      signing: "checksum-only",
    },
    {
      name: `snaploom-${version}-sbom.cdx.json`,
      kind: "aggregate-sbom",
      license: "GPL-3.0-or-later AND Apache-2.0",
      platform: "multi-platform",
      signing: "attestation",
    },
    {
      name: `snaploom-${version}-third-party-notices.txt`,
      kind: "aggregate-notices",
      license: "GPL-3.0-or-later AND Apache-2.0",
      platform: "multi-platform",
      signing: "attestation",
    },
  ];
}

export function expectedReleaseFiles(version) {
  const payloads = assetsForVersion(version).map(({ name }) => name);
  return [
    ...payloads,
    ...payloads.map((name) => `${name}.sha256`),
    "release-manifest.json",
    "SHA256SUMS",
  ].sort();
}

export function candidateSigningEvidence() {
  return {
    windows: {
      mode: "unsigned",
      authenticodeVerified: false,
      rfc3161TimestampVerified: false,
      unknownPublisherWarning: true,
    },
    macos: {
      mode: "adhoc",
      developerIdVerified: false,
      notarizationStatus: "not-submitted",
      stapled: false,
      gatekeeperVerified: false,
    },
  };
}

export function validateSigningEvidence(mode, signing) {
  if (!signing || typeof signing !== "object") {
    throw new Error("release manifest is missing signing evidence");
  }
  if (mode === "candidate-unsigned") {
    if (
      signing.windows?.mode !== "unsigned" ||
      signing.windows?.authenticodeVerified !== false ||
      signing.windows?.unknownPublisherWarning !== true ||
      signing.macos?.mode !== "adhoc" ||
      signing.macos?.notarizationStatus !== "not-submitted"
    ) {
      throw new Error("candidate release must be explicitly unsigned/ad hoc");
    }
    return;
  }
  if (mode !== "stable-release") {
    throw new Error(`unsupported release mode: ${mode}`);
  }
  if (
    signing.windows?.mode !== "unsigned" ||
    signing.windows?.authenticodeVerified !== false ||
    signing.windows?.rfc3161TimestampVerified !== false ||
    signing.windows?.unknownPublisherWarning !== true
  ) {
    throw new Error(
      "stable Windows assets must be explicitly unsigned with unknown-publisher risk disclosure",
    );
  }
  if (
    signing.macos?.mode !== "unsigned" ||
    signing.macos?.developerIdVerified !== false ||
    signing.macos?.notarizationStatus !== "not-submitted" ||
    signing.macos?.stapled !== false ||
    signing.macos?.gatekeeperVerified !== false ||
    signing.macos?.adHocSignatureVerified !== true ||
    signing.macos?.gatekeeperWarning !== true
  ) {
    throw new Error(
      "stable macOS assets require explicit unsigned risk and verified ad hoc package evidence",
    );
  }
}
