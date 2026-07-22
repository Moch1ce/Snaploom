// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { assembleRelease } from "./assemble-release.mjs";
import { assetsForVersion } from "./release-contract.mjs";
import { validateArchiveEntries, verifyRelease } from "./verify-release.mjs";

const version = "1.2.3";
const commit = "1".repeat(40);

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "snaploom-release-test-"));
  const payloads = join(root, "payloads");
  const output = join(root, "release");
  mkdirSync(payloads);
  for (const { name } of assetsForVersion(version)) {
    writeFileSync(join(payloads, name), `payload:${name}\n`);
  }
  return { root, payloads, output };
}

test("assembles and verifies the exact candidate asset set", () => {
  const paths = fixture();
  try {
    const manifest = assembleRelease({
      payloadDirectory: paths.payloads,
      outputDirectory: paths.output,
      version,
      commit,
    });
    assert.equal(manifest.assets.length, 12);
    assert.equal(
      verifyRelease(paths.output, { skipArchiveBoundaries: true }).commit,
      commit,
    );
    assert.match(readFileSync(join(paths.output, "SHA256SUMS"), "utf8"), /Snaploom\.Capture/);
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects extra payloads and refuses to overwrite output", () => {
  const paths = fixture();
  try {
    writeFileSync(join(paths.payloads, "debug.pdb"), "forbidden");
    assert.throws(
      () =>
        assembleRelease({
          payloadDirectory: paths.payloads,
          outputDirectory: paths.output,
          version,
          commit,
        }),
      /payload set mismatch/,
    );
    rmSync(join(paths.payloads, "debug.pdb"));
    mkdirSync(paths.output);
    writeFileSync(join(paths.output, "existing"), "immutable");
    assert.throws(
      () =>
        assembleRelease({
          payloadDirectory: paths.payloads,
          outputDirectory: paths.output,
          version,
          commit,
        }),
      /never overwritten/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("stable release accepts explicit unsigned Windows evidence with notarized macOS assets", () => {
  const paths = fixture();
  try {
    const manifest = assembleRelease({
      payloadDirectory: paths.payloads,
      outputDirectory: paths.output,
      version,
      commit,
      mode: "stable-release",
      signingEvidence: {
        windows: {
          mode: "unsigned",
          authenticodeVerified: false,
          rfc3161TimestampVerified: false,
          unknownPublisherWarning: true,
        },
        macos: {
          mode: "developer-id",
          developerIdVerified: true,
          notarizationStatus: "Accepted",
          stapled: true,
          gatekeeperVerified: true,
          teamId: "ABCDE12345",
          notarySubmissionId: "notary-submission",
        },
      },
    });

    assert.equal(manifest.mode, "stable-release");
    assert.equal(manifest.signing.windows.mode, "unsigned");
    assert.equal(manifest.signing.windows.unknownPublisherWarning, true);
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("checksum damage is a hard failure", () => {
  const paths = fixture();
  try {
    assembleRelease({
      payloadDirectory: paths.payloads,
      outputDirectory: paths.output,
      version,
      commit,
    });
    writeFileSync(join(paths.output, `snaploom-${version}-sbom.cdx.json`), "damaged");
    assert.throws(
      () => verifyRelease(paths.output, { skipArchiveBoundaries: true }),
      /manifest mismatch/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("archive boundaries reject raw and slash-normalized duplicate filenames", () => {
  const contract = assetsForVersion(version).find(
    ({ kind }) => kind === "capture-sdk-dotnet",
  );
  assert.throws(
    () => validateArchiveEntries(["lib/a.dll", "lib/a.dll"], contract),
    /duplicate archive entry/,
  );
  assert.throws(
    () => validateArchiveEntries(["lib/a.dll", "lib\\a.dll"], contract),
    /duplicate archive entry/,
  );
  assert.throws(
    () => validateArchiveEntries(["lib/A.dll", "lib/a.dll"], contract),
    /duplicate archive entry/,
  );
});
