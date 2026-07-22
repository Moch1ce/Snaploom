// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import test from "node:test";
import { createSigningEvidence } from "./create-signing-evidence.mjs";

const windows = {
  signingMode: "stable-unsigned",
  authenticodeVerified: false,
  rfc3161TimestampVerified: false,
  unknownPublisherWarning: true,
};
const macos = {
  signingMode: "developer-id",
  notarized: true,
  teamId: "ABCDE12345",
  hostNotarySubmissionId: "host-submission",
  dmgNotarySubmissionId: "dmg-submission",
};

test("records explicit unsigned Windows risk beside notarized macOS evidence", () => {
  const evidence = createSigningEvidence(windows, macos);
  assert.deepEqual(evidence.windows, {
    mode: "unsigned",
    authenticodeVerified: false,
    rfc3161TimestampVerified: false,
    unknownPublisherWarning: true,
  });
  assert.equal(evidence.macos.notarySubmissionId, "dmg-submission");
});

test("rejects ambiguous Windows risk or non-notarized macOS metadata", () => {
  assert.throws(
    () => createSigningEvidence({ ...windows, unknownPublisherWarning: false }, macos),
    /does not explicitly disclose an unsigned release/,
  );
  assert.throws(
    () => createSigningEvidence({ ...windows, signingMode: "authenticode" }, macos),
    /does not explicitly disclose an unsigned release/,
  );
  assert.throws(
    () => createSigningEvidence(windows, { ...macos, notarized: false }),
    /not Developer ID\/notarization/,
  );
});
