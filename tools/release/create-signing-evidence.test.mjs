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
  signingMode: "stable-unsigned",
  notarized: false,
  adHocSignatureVerified: true,
  gatekeeperWarning: true,
};

test("records explicit unsigned Windows and macOS risk", () => {
  const evidence = createSigningEvidence(windows, macos);
  assert.deepEqual(evidence.windows, {
    mode: "unsigned",
    authenticodeVerified: false,
    rfc3161TimestampVerified: false,
    unknownPublisherWarning: true,
  });
  assert.deepEqual(evidence.macos, {
    mode: "unsigned",
    developerIdVerified: false,
    notarizationStatus: "not-submitted",
    stapled: false,
    gatekeeperVerified: false,
    adHocSignatureVerified: true,
    gatekeeperWarning: true,
  });
});

test("rejects ambiguous Windows or macOS unsigned risk", () => {
  assert.throws(
    () => createSigningEvidence({ ...windows, unknownPublisherWarning: false }, macos),
    /does not explicitly disclose an unsigned release/,
  );
  assert.throws(
    () => createSigningEvidence({ ...windows, signingMode: "authenticode" }, macos),
    /does not explicitly disclose an unsigned release/,
  );
  assert.throws(
    () => createSigningEvidence(windows, { ...macos, gatekeeperWarning: false }),
    /does not explicitly disclose an unsigned release/,
  );
  assert.throws(
    () => createSigningEvidence(windows, { ...macos, signingMode: "developer-id" }),
    /does not explicitly disclose an unsigned release/,
  );
});
