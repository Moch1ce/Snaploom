// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { lstatSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { assembleRelease } from "./assemble-release.mjs";
import { assetsForVersion, expectedReleaseFiles } from "./release-contract.mjs";
import { verifyDraftRelease } from "./verify-draft-release.mjs";

const version = "1.2.3";
const commit = "2".repeat(40);
const signingEvidence = {
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
    notarySubmissionId: "dmg-submission",
  },
};

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "snaploom-draft-test-"));
  const payloads = join(root, "payloads");
  const directory = join(root, "release");
  mkdirSync(payloads);
  for (const { name } of assetsForVersion(version)) {
    writeFileSync(join(payloads, name), `payload:${name}\n`);
  }
  assembleRelease({
    payloadDirectory: payloads,
    outputDirectory: directory,
    version,
    commit,
    mode: "stable-release",
    signingEvidence,
  });
  const assets = expectedReleaseFiles(version).map((name, index) => {
    const path = join(directory, name);
    const digest = createHash("sha256").update(readFileSync(path)).digest("hex");
    return {
      id: index + 1,
      name,
      size: lstatSync(path).size,
      digest: `sha256:${digest}`,
      state: "uploaded",
    };
  });
  const release = {
    id: 42,
    draft: true,
    prerelease: false,
    published_at: null,
    tag_name: `v${version}`,
    target_commitish: commit,
    resolved_tag_commit: commit,
    html_url: "https://github.com/Moch1ce/Snaploom/releases/tag/untagged-test",
    assets,
  };
  return { root, directory, release };
}

test("accepts an exact stable release draft after redownload", () => {
  const paths = fixture();
  try {
    const result = verifyDraftRelease({
      release: paths.release,
      directory: paths.directory,
      version,
      commit,
      skipArchiveBoundaries: true,
    });
    assert.equal(result.release.id, 42);
    assert.equal(result.manifest.mode, "stable-release");
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects public, partial, extra, or digest-mismatched releases", () => {
  const paths = fixture();
  try {
    const options = {
      directory: paths.directory,
      version,
      commit,
      skipArchiveBoundaries: true,
    };
    assert.throws(
      () => verifyDraftRelease({ ...options, release: { ...paths.release, draft: false } }),
      /not an unpublished/,
    );
    assert.throws(
      () =>
        verifyDraftRelease({
          ...options,
          release: { ...paths.release, assets: paths.release.assets.slice(1) },
        }),
      /exact authoritative/,
    );
    const damagedAssets = paths.release.assets.map((asset, index) =>
      index === 0 ? { ...asset, digest: `sha256:${"0".repeat(64)}` } : asset,
    );
    assert.throws(
      () => verifyDraftRelease({ ...options, release: { ...paths.release, assets: damagedAssets } }),
      /differs from downloaded bytes/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});
