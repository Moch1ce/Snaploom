// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

export const RELEASE_SCHEMA_VERSION = 1;
export const MAX_INSTALLER_BYTES = 50_000_000;

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
      signing.macos?.mode !== "adhoc" ||
      signing.macos?.notarizationStatus !== "not-submitted"
    ) {
      throw new Error("candidate release must be explicitly unsigned/ad hoc");
    }
    return;
  }
  if (mode !== "stable-signed") {
    throw new Error(`unsupported release mode: ${mode}`);
  }
  if (
    signing.windows?.mode !== "authenticode" ||
    signing.windows?.authenticodeVerified !== true ||
    signing.windows?.rfc3161TimestampVerified !== true ||
    typeof signing.windows?.certificateThumbprint !== "string" ||
    signing.windows.certificateThumbprint.length < 16
  ) {
    throw new Error("stable Windows assets require verified Authenticode and RFC3161 evidence");
  }
  if (
    signing.macos?.mode !== "developer-id" ||
    signing.macos?.developerIdVerified !== true ||
    signing.macos?.notarizationStatus !== "Accepted" ||
    signing.macos?.stapled !== true ||
    signing.macos?.gatekeeperVerified !== true ||
    typeof signing.macos?.teamId !== "string" ||
    signing.macos.teamId.length < 5 ||
    typeof signing.macos?.notarySubmissionId !== "string" ||
    signing.macos.notarySubmissionId.length < 8
  ) {
    throw new Error("stable macOS assets require Developer ID, notarization, stapling, and Gatekeeper evidence");
  }
}
