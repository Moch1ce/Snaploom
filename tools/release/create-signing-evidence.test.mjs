// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import test from "node:test";
import { createSigningEvidence } from "./create-signing-evidence.mjs";

const windows = {
  signingMode: "stable-signed",
  authenticodeVerified: true,
  rfc3161TimestampVerified: true,
  certificateThumbprint: "A".repeat(40),
  additionalSignedBinaries: [{ name: "snaploom_capture.dll", sha256: "1".repeat(64) }],
};
const macos = {
  signingMode: "developer-id",
  notarized: true,
  teamId: "ABCDE12345",
  hostNotarySubmissionId: "host-submission",
  dmgNotarySubmissionId: "dmg-submission",
};

test("normalizes platform metadata into stable signing evidence", () => {
  const evidence = createSigningEvidence(windows, macos);
  assert.equal(evidence.windows.certificateThumbprint, "a".repeat(40));
  assert.equal(evidence.macos.notarySubmissionId, "dmg-submission");
  assert.equal(evidence.windows.additionalSignedBinaries.length, 1);
});

test("rejects unsigned or non-notarized platform metadata", () => {
  assert.throws(
    () => createSigningEvidence({ ...windows, authenticodeVerified: false }, macos),
    /not complete Authenticode/,
  );
  assert.throws(
    () => createSigningEvidence({ ...windows, additionalSignedBinaries: [] }, macos),
    /not complete Authenticode/,
  );
  assert.throws(
    () => createSigningEvidence(windows, { ...macos, notarized: false }),
    /not Developer ID\/notarization/,
  );
});
