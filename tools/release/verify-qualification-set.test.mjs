// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { verifyQualificationSet } from "./verify-qualification-set.mjs";

const version = "1.2.3";
const commit = "3".repeat(40);

function writeEvidence(directory, evidence) {
  writeFileSync(
    join(directory, `${evidence.platform}.json`),
    `${JSON.stringify(evidence)}\n`,
  );
}

function hostedEvidence(platform, runnerImage, architecture, distributionContract) {
  return {
    schemaVersion: 1,
    executionEnvironment: "github-hosted",
    platform,
    runnerImage,
    architecture,
    version,
    commit,
    passed: true,
    contracts: ["QA-02", distributionContract],
    limitations: ["no-interactive-desktop", "no-real-machine-performance"],
  };
}

test("accepts exactly the GitHub-hosted Windows and macOS qualification set", () => {
  const directory = mkdtempSync(join(tmpdir(), "snaploom-hosted-qualification-"));
  try {
    writeEvidence(
      directory,
      hostedEvidence("windows-x64", "windows-2025", "x86_64", "DIST-02"),
    );
    writeEvidence(
      directory,
      hostedEvidence("macos-arm64", "macos-15", "arm64", "DIST-03"),
    );

    assert.deepEqual(
      verifyQualificationSet(directory, { version, commit }).map(({ platform }) => platform),
      ["macos-arm64", "windows-x64"],
    );
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("rejects legacy true-machine records and incomplete hosted identities", () => {
  for (const mutation of [
    (evidence) => (evidence.executionEnvironment = "self-hosted"),
    (evidence) => (evidence.runnerImage = "snaploom-windows-10-x64"),
    (evidence) => (evidence.limitations = []),
    (evidence) => (evidence.contracts = ["PERF-01", "QA-03"]),
  ]) {
    const directory = mkdtempSync(join(tmpdir(), "snaploom-hosted-qualification-"));
    try {
      const windows = hostedEvidence(
        "windows-x64",
        "windows-2025",
        "x86_64",
        "DIST-02",
      );
      mutation(windows);
      writeEvidence(directory, windows);
      writeEvidence(
        directory,
        hostedEvidence("macos-arm64", "macos-15", "arm64", "DIST-03"),
      );
      assert.throws(
        () => verifyQualificationSet(directory, { version, commit }),
        /hosted qualification set does not approve/,
      );
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
  }
});
