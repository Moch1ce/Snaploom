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
import {
  verifyPublicAssetSubset,
  verifyPublicRelease,
} from "./verify-public-release.mjs";

const version = "1.2.3";
const commit = "3".repeat(40);
const signingEvidence = {
  windows: {
    mode: "authenticode",
    authenticodeVerified: true,
    rfc3161TimestampVerified: true,
    certificateThumbprint: "a".repeat(40),
    additionalSignedBinaries: [{ name: "snaploom_capture.dll", sha256: "1".repeat(64) }],
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
  const root = mkdtempSync(join(tmpdir(), "snaploom-public-release-test-"));
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
    mode: "stable-signed",
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
    id: 84,
    name: `Snaploom ${version}`,
    body: "Stable Snaploom release",
    draft: false,
    prerelease: false,
    immutable: true,
    published_at: "2026-07-22T00:00:00Z",
    tag_name: `v${version}`,
    target_commitish: commit,
    resolved_tag_commit: commit,
    html_url: `https://github.com/Moch1ce/Snaploom/releases/tag/v${version}`,
    assets,
  };
  return { root, directory, release };
}

test("accepts one exact immutable public stable release after redownload", () => {
  const paths = fixture();
  try {
    const result = verifyPublicRelease({
      release: paths.release,
      directory: paths.directory,
      version,
      commit,
      skipArchiveBoundaries: true,
    });
    assert.equal(result.release.id, 84);
    assert.equal(result.manifest.mode, "stable-signed");
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects draft, mutable, prerelease, partial, and digest-mismatched releases", () => {
  const paths = fixture();
  try {
    const options = {
      directory: paths.directory,
      version,
      commit,
      skipArchiveBoundaries: true,
    };
    for (const release of [
      { ...paths.release, draft: true },
      { ...paths.release, immutable: false },
      { ...paths.release, prerelease: true },
      { ...paths.release, published_at: null },
    ]) {
      assert.throws(
        () => verifyPublicRelease({ ...options, release }),
        /not an immutable public stable release/,
      );
    }
    assert.throws(
      () =>
        verifyPublicRelease({
          ...options,
          release: { ...paths.release, assets: paths.release.assets.slice(1) },
        }),
      /exact authoritative/,
    );
    const damagedAssets = paths.release.assets.map((asset, index) =>
      index === 0 ? { ...asset, digest: `sha256:${"0".repeat(64)}` } : asset,
    );
    assert.throws(
      () =>
        verifyPublicRelease({
          ...options,
          release: { ...paths.release, assets: damagedAssets },
        }),
      /differs from downloaded bytes/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("accepts only an exact redownloaded SDK asset subset with its checksum sidecar", () => {
  const paths = fixture();
  const subset = join(paths.root, "subset");
  mkdirSync(subset);
  const name = `Snaploom.Capture.${version}.nupkg`;
  for (const selected of [name, `${name}.sha256`]) {
    writeFileSync(join(subset, selected), readFileSync(join(paths.directory, selected)));
  }
  try {
    assert.doesNotThrow(() =>
      verifyPublicAssetSubset({
        release: paths.release,
        directory: subset,
        names: [name, `${name}.sha256`],
        version,
        commit,
      }),
    );
    writeFileSync(join(subset, `${name}.sha256`), `${"0".repeat(64)}  ${name}\n`);
    assert.throws(
      () =>
        verifyPublicAssetSubset({
          release: paths.release,
          directory: subset,
          names: [name, `${name}.sha256`],
          version,
          commit,
        }),
      /stored asset metadata differs|checksum sidecar differs/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});
