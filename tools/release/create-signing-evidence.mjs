// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, writeFileSync } from "node:fs";
import { basename, resolve } from "node:path";
import { validateSigningEvidence } from "./release-contract.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

export function createSigningEvidence(windows, macos) {
  if (
    windows?.signingMode !== "stable-unsigned" ||
    windows.authenticodeVerified !== false ||
    windows.rfc3161TimestampVerified !== false ||
    windows.unknownPublisherWarning !== true
  ) {
    throw new Error("Windows metadata does not explicitly disclose an unsigned release");
  }
  if (
    macos?.signingMode !== "stable-unsigned" ||
    macos.notarized !== false ||
    macos.adHocSignatureVerified !== true ||
    macos.gatekeeperWarning !== true
  ) {
    throw new Error("macOS metadata does not explicitly disclose an unsigned release");
  }

  const evidence = {
    windows: {
      mode: "unsigned",
      authenticodeVerified: false,
      rfc3161TimestampVerified: false,
      unknownPublisherWarning: true,
    },
    macos: {
      mode: "unsigned",
      developerIdVerified: false,
      notarizationStatus: "not-submitted",
      stapled: false,
      gatekeeperVerified: false,
      adHocSignatureVerified: true,
      gatekeeperWarning: true,
    },
  };
  validateSigningEvidence("stable-release", evidence);
  return evidence;
}

if (process.argv[1] && basename(process.argv[1]) === "create-signing-evidence.mjs") {
  const windowsPath = argument("windows-metadata");
  const macosPath = argument("macos-metadata");
  const output = argument("output");
  if (!windowsPath || !macosPath || !output) {
    throw new Error("--windows-metadata, --macos-metadata, and --output are required");
  }
  const evidence = createSigningEvidence(
    JSON.parse(readFileSync(resolve(windowsPath), "utf8")),
    JSON.parse(readFileSync(resolve(macosPath), "utf8")),
  );
  writeFileSync(resolve(output), `${JSON.stringify(evidence, null, 2)}\n`, "utf8");
  process.stdout.write("created stable Windows and macOS unsigned risk evidence\n");
}
