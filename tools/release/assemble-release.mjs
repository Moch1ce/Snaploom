// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { basename, join, resolve } from "node:path";
import {
  RELEASE_SCHEMA_VERSION,
  assetsForVersion,
  candidateSigningEvidence,
  validateSigningEvidence,
} from "./release-contract.mjs";

function argument(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

export function assembleRelease({
  payloadDirectory,
  outputDirectory,
  version,
  tag = `v${version}`,
  commit,
  mode = "candidate-unsigned",
  repository = "Moch1ce/Snaploom",
  runId = "local",
  signingEvidence,
}) {
  if (!/^[0-9a-f]{40}$/.test(commit)) {
    throw new Error("release commit must be a full 40-character lowercase SHA");
  }
  if (tag !== `v${version}`) {
    throw new Error(`release tag ${tag} does not match version ${version}`);
  }
  const expected = assetsForVersion(version);
  const actualPayloads = readdirSync(payloadDirectory).sort();
  const expectedPayloads = expected.map(({ name }) => name).sort();
  if (JSON.stringify(actualPayloads) !== JSON.stringify(expectedPayloads)) {
    throw new Error(
      `payload set mismatch\nexpected: ${expectedPayloads.join(", ")}\nactual: ${actualPayloads.join(", ")}`,
    );
  }
  if (existsSync(outputDirectory) && readdirSync(outputDirectory).length !== 0) {
    throw new Error("release output directory must be empty; assets are never overwritten");
  }
  mkdirSync(outputDirectory, { recursive: true });

  const signing = signingEvidence ?? candidateSigningEvidence();
  validateSigningEvidence(mode, signing);
  const assets = expected.map((contract) => {
    const source = join(payloadDirectory, contract.name);
    const stat = lstatSync(source);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size === 0) {
      throw new Error(`release payload must be a non-empty regular file: ${contract.name}`);
    }
    if (contract.maxBytes && stat.size > contract.maxBytes) {
      throw new Error(`${contract.name} exceeds ${contract.maxBytes} bytes`);
    }
    const digest = sha256(source);
    copyFileSync(source, join(outputDirectory, contract.name));
    writeFileSync(
      join(outputDirectory, `${contract.name}.sha256`),
      `${digest}  ${contract.name}\n`,
      "utf8",
    );
    return {
      ...contract,
      bytes: stat.size,
      sha256: digest,
    };
  });

  const sums = assets
    .map(({ name, sha256: digest }) => `${digest}  ${name}`)
    .sort()
    .join("\n");
  writeFileSync(join(outputDirectory, "SHA256SUMS"), `${sums}\n`, "utf8");

  const swiftAsset = assets.find(({ kind }) => kind === "capture-sdk-swift");
  const manifest = {
    schemaVersion: RELEASE_SCHEMA_VERSION,
    repository,
    tag,
    commit,
    version,
    mode,
    builder: {
      system: "github-actions",
      runId: String(runId),
    },
    signing,
    swiftBinaryChecksum: swiftAsset.sha256,
    nuget: {
      packageId: "Snaploom.Capture",
      version,
    },
    assets,
  };
  writeFileSync(
    join(outputDirectory, "release-manifest.json"),
    `${JSON.stringify(manifest, null, 2)}\n`,
    "utf8",
  );
  return manifest;
}

if (process.argv[1] && basename(process.argv[1]) === "assemble-release.mjs") {
  const payloadDirectory = resolve(argument("payloads", "release-payloads"));
  const outputDirectory = resolve(argument("output", "release-assets"));
  const version = argument("version");
  const commit = argument("commit");
  if (!version || !commit) {
    throw new Error("usage: assemble-release.mjs --payloads DIR --output DIR --version X.Y.Z --commit SHA");
  }
  const evidencePath = argument("signing-evidence");
  const signingEvidence = evidencePath
    ? JSON.parse(readFileSync(resolve(evidencePath), "utf8"))
    : undefined;
  const manifest = assembleRelease({
    payloadDirectory,
    outputDirectory,
    version,
    commit,
    tag: argument("tag", `v${version}`),
    mode: argument("mode", "candidate-unsigned"),
    repository: argument("repository", "Moch1ce/Snaploom"),
    runId: argument("run-id", "local"),
    signingEvidence,
  });
  process.stdout.write(
    `assembled ${manifest.assets.length} immutable release payloads in ${outputDirectory}\n`,
  );
}
