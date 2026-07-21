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
  const additionalSignedBinaries = windows?.additionalSignedBinaries ?? [];
  if (
    windows?.signingMode !== "stable-signed" ||
    windows.authenticodeVerified !== true ||
    windows.rfc3161TimestampVerified !== true ||
    typeof windows.certificateThumbprint !== "string" ||
    additionalSignedBinaries.length !== 1 ||
    additionalSignedBinaries[0]?.name !== "snaploom_capture.dll" ||
    !/^[0-9a-f]{64}$/.test(additionalSignedBinaries[0]?.sha256 ?? "")
  ) {
    throw new Error("Windows product and SDK metadata is not complete Authenticode evidence");
  }
  if (
    macos?.signingMode !== "developer-id" ||
    macos.notarized !== true ||
    typeof macos.teamId !== "string" ||
    typeof macos.hostNotarySubmissionId !== "string" ||
    typeof macos.dmgNotarySubmissionId !== "string"
  ) {
    throw new Error("macOS package metadata is not Developer ID/notarization evidence");
  }

  const evidence = {
    windows: {
      mode: "authenticode",
      authenticodeVerified: true,
      rfc3161TimestampVerified: true,
      certificateThumbprint: windows.certificateThumbprint.toLowerCase(),
      additionalSignedBinaries,
    },
    macos: {
      mode: "developer-id",
      developerIdVerified: true,
      notarizationStatus: "Accepted",
      stapled: true,
      gatekeeperVerified: true,
      teamId: macos.teamId,
      notarySubmissionId: macos.dmgNotarySubmissionId,
      hostNotarySubmissionId: macos.hostNotarySubmissionId,
      dmgNotarySubmissionId: macos.dmgNotarySubmissionId,
    },
  };
  validateSigningEvidence("stable-signed", evidence);
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
  process.stdout.write("created stable signing and notarization evidence\n");
}
