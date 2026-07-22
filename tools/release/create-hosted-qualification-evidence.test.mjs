// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import test from "node:test";
import { createHostedQualificationEvidence } from "./create-hosted-qualification-evidence.mjs";

const version = "1.2.3";
const commit = "3".repeat(40);

test("records an honest GitHub-hosted platform qualification", () => {
  assert.deepEqual(
    createHostedQualificationEvidence({
      platform: "windows-x64",
      runnerImage: "windows-2025",
      architecture: "x86_64",
      version,
      commit,
    }),
    {
      schemaVersion: 1,
      executionEnvironment: "github-hosted",
      platform: "windows-x64",
      runnerImage: "windows-2025",
      architecture: "x86_64",
      version,
      commit,
      passed: true,
      contracts: ["QA-02", "DIST-02"],
      limitations: ["no-interactive-desktop", "no-real-machine-performance"],
    },
  );
});

test("rejects custom runners and mismatched hosted platform identities", () => {
  for (const options of [
    {
      platform: "windows-x64",
      runnerImage: "snaploom-windows-10-x64",
      architecture: "x86_64",
    },
    { platform: "windows-x64", runnerImage: "windows-2025", architecture: "arm64" },
    { platform: "macos-arm64", runnerImage: "macos-14", architecture: "arm64" },
  ]) {
    assert.throws(
      () => createHostedQualificationEvidence({ ...options, version, commit }),
      /unsupported GitHub-hosted qualification identity/,
    );
  }
});
