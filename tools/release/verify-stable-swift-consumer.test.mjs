// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import test from "node:test";
import {
  verifyResolvedSwiftPackage,
  verifySwiftReleaseManifest,
} from "./verify-stable-swift-consumer.mjs";

const version = "1.2.3";
const commit = "4".repeat(40);
const checksum = "5".repeat(64);
const repositoryUrl = "https://github.com/Moch1ce/Snaploom.git";
const manifest = `
let snaploomBinaryChecksum = "${checksum}"
.binaryTarget(
  name: "CSnaploomCapture",
  url: "https://github.com/Moch1ce/Snaploom/releases/download/v${version}/CSnaploomCapture-${version}.xcframework.zip",
  checksum: snaploomBinaryChecksum
)
`;
const resolved = {
  version: 2,
  pins: [
    {
      identity: "snaploom",
      kind: "remoteSourceControl",
      location: repositoryUrl,
      state: { revision: commit, version },
    },
  ],
};

test("accepts the exact public Swift binary target and resolved tag commit", () => {
  assert.doesNotThrow(() =>
    verifySwiftReleaseManifest(manifest, { version, checksum }),
  );
  assert.doesNotThrow(() =>
    verifyResolvedSwiftPackage(resolved, { version, commit, repositoryUrl }),
  );
  assert.doesNotThrow(() =>
    verifyResolvedSwiftPackage(
      { ...resolved, version: 3, originHash: "8".repeat(64) },
      { version, commit, repositoryUrl },
    ),
  );
});

test("rejects changed Swift URL, checksum, version, commit, or extra dependencies", () => {
  assert.throws(
    () => verifySwiftReleaseManifest(manifest, { version, checksum: "6".repeat(64) }),
    /checksum/,
  );
  assert.throws(
    () =>
      verifySwiftReleaseManifest(manifest.replace("releases/download", "releases/other"), {
        version,
        checksum,
      }),
    /URL/,
  );
  assert.throws(
    () =>
      verifySwiftReleaseManifest(
        `${manifest}\n${manifest
          .slice(manifest.indexOf(".binaryTarget"))
          .replace("releases/download", "releases/other")}`,
        { version, checksum },
      ),
    /URL/,
  );
  assert.throws(
    () =>
      verifyResolvedSwiftPackage(resolved, {
        version,
        commit: "7".repeat(40),
        repositoryUrl,
      }),
    /resolved package/,
  );
  assert.throws(
    () =>
      verifyResolvedSwiftPackage(
        { ...resolved, version: 3, originHash: "invalid" },
        { version, commit, repositoryUrl },
      ),
    /resolved package/,
  );
  assert.throws(
    () =>
      verifyResolvedSwiftPackage(
        { ...resolved, pins: [...resolved.pins, { ...resolved.pins[0], identity: "extra" }] },
        { version, commit, repositoryUrl },
      ),
    /resolved package/,
  );
});
