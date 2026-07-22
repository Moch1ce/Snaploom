// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

export const hostedQualificationLimitations = Object.freeze([
  "no-interactive-desktop",
  "no-real-machine-performance",
]);

export const hostedQualificationPolicy = Object.freeze([
  Object.freeze({
    platform: "macos-arm64",
    runnerImage: "macos-15",
    architecture: "arm64",
    contracts: Object.freeze(["QA-02", "DIST-03"]),
  }),
  Object.freeze({
    platform: "windows-x64",
    runnerImage: "windows-2025",
    architecture: "x86_64",
    contracts: Object.freeze(["QA-02", "DIST-02"]),
  }),
]);

export function hostedQualificationIdentity(platform) {
  return hostedQualificationPolicy.find((identity) => identity.platform === platform);
}
