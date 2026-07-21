// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { assembleRelease } from "./assemble-release.mjs";
import { assetsForVersion } from "./release-contract.mjs";
import { verifyRelease } from "./verify-release.mjs";

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

test("stable mode cannot use unsigned or ad hoc evidence", () => {
  const paths = fixture();
  try {
    assert.throws(
      () =>
        assembleRelease({
          payloadDirectory: paths.payloads,
          outputDirectory: paths.output,
          version,
          commit,
          mode: "stable-signed",
        }),
      /stable Windows assets require verified Authenticode/,
    );
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
