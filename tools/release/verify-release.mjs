// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { lstatSync, readFileSync, readdirSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import {
  RELEASE_SCHEMA_VERSION,
  assetsForVersion,
  expectedReleaseFiles,
  validateSigningEvidence,
} from "./release-contract.mjs";

function argument(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function archiveEntries(path) {
  if (path.endsWith(".zip") || path.endsWith(".nupkg") || path.endsWith(".snupkg")) {
    return execFileSync(
      process.platform === "win32" ? "python" : "python3",
      [
        "-c",
        "import sys,zipfile; print('\\n'.join(zipfile.ZipFile(sys.argv[1]).namelist()))",
        path,
      ],
      { encoding: "utf8" },
    ).trim().split("\n").filter(Boolean);
  }
  if (path.endsWith(".tar.gz")) {
    return execFileSync("tar", ["-tzf", path], { encoding: "utf8" })
      .trim().split("\n").filter(Boolean);
  }
  if (path.endsWith(".tar.zst")) {
    return execFileSync("tar", ["--zstd", "-tf", path], { encoding: "utf8" })
      .trim().split("\n").filter(Boolean);
  }
  return [];
}

export function validateArchiveEntries(entries, contract) {
  const normalizedEntries = entries.map((entry) => entry.replaceAll("\\", "/"));
  const portableEntries = normalizedEntries.map((entry) =>
    entry.normalize("NFC").toLowerCase(),
  );
  if (
    new Set(entries).size !== entries.length ||
    new Set(normalizedEntries).size !== entries.length ||
    new Set(portableEntries).size !== entries.length
  ) {
    throw new Error(`duplicate archive entry in ${contract.name}`);
  }
  for (const [index, entry] of entries.entries()) {
    const normalized = normalizedEntries[index];
    if (
      normalized.startsWith("/") ||
      /^[A-Za-z]:\//.test(normalized) ||
      normalized.split("/").includes("..")
    ) {
      throw new Error(`unsafe archive entry in ${contract.name}: ${entry}`);
    }
  }
  if (!contract.kind.startsWith("capture-sdk")) return;
  const lowered = normalizedEntries.map((entry) => entry.toLowerCase());
  const forbidden = /(?:capture[-_ ]?host|capture[-_ ]?session|(?:^|\/)product\/|tauri|gpl-3\.0)/i;
  const violation = normalizedEntries.find((entry) => forbidden.test(entry));
  if (violation) {
    throw new Error(`SDK package crosses the GPL/Host boundary: ${contract.name}:${violation}`);
  }
  if (contract.kind !== "capture-sdk-symbols") {
    const hasLicense = lowered.some((entry) => entry.endsWith("licenses/apache-2.0.txt"));
    const hasNotice = lowered.some((entry) => /(?:^|\/)notice$/.test(entry));
    const hasSbom = lowered.some((entry) => entry.endsWith("sbom.cdx.json"));
    if (!hasLicense || !hasNotice || !hasSbom) {
      throw new Error(`${contract.name} must contain Apache LICENSE, NOTICE, and SBOM`);
    }
  }
}

function validateArchiveBoundary(path, contract) {
  validateArchiveEntries(archiveEntries(path), contract);
}

export function verifyRelease(directory, { version, skipArchiveBoundaries = false } = {}) {
  const manifestPath = join(directory, "release-manifest.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  const releaseVersion = version ?? manifest.version;
  if (
    manifest.schemaVersion !== RELEASE_SCHEMA_VERSION ||
    manifest.version !== releaseVersion ||
    manifest.tag !== `v${releaseVersion}` ||
    !/^[0-9a-f]{40}$/.test(manifest.commit)
  ) {
    throw new Error("release manifest identity is invalid");
  }
  validateSigningEvidence(manifest.mode, manifest.signing);

  const actualFiles = readdirSync(directory).sort();
  const expectedFiles = expectedReleaseFiles(releaseVersion);
  if (JSON.stringify(actualFiles) !== JSON.stringify(expectedFiles)) {
    throw new Error("release directory does not contain the exact authoritative asset set");
  }
  for (const name of actualFiles) {
    const stat = lstatSync(join(directory, name));
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size === 0) {
      throw new Error(`release file is empty, non-regular, or a symlink: ${name}`);
    }
  }

  const contracts = assetsForVersion(releaseVersion);
  const byName = new Map(manifest.assets.map((asset) => [asset.name, asset]));
  if (byName.size !== contracts.length || manifest.assets.length !== contracts.length) {
    throw new Error("release manifest has duplicate or missing assets");
  }
  const expectedSums = [];
  for (const contract of contracts) {
    const path = join(directory, contract.name);
    const stat = lstatSync(path);
    const digest = sha256(path);
    const asset = byName.get(contract.name);
    if (
      !asset ||
      asset.kind !== contract.kind ||
      asset.license !== contract.license ||
      asset.platform !== contract.platform ||
      asset.bytes !== stat.size ||
      asset.sha256 !== digest
    ) {
      throw new Error(`manifest mismatch for ${contract.name}`);
    }
    if (contract.maxBytes && stat.size > contract.maxBytes) {
      throw new Error(`${contract.name} exceeds the 50 MB distribution limit`);
    }
    const expectedLine = `${digest}  ${contract.name}\n`;
    if (readFileSync(`${path}.sha256`, "utf8") !== expectedLine) {
      throw new Error(`invalid individual checksum for ${contract.name}`);
    }
    expectedSums.push(expectedLine.trimEnd());
    if (!skipArchiveBoundaries) validateArchiveBoundary(path, contract);
  }
  const sums = `${expectedSums.sort().join("\n")}\n`;
  if (readFileSync(join(directory, "SHA256SUMS"), "utf8") !== sums) {
    throw new Error("SHA256SUMS is not the stable authoritative payload list");
  }
  const swift = byName.get(`CSnaploomCapture-${releaseVersion}.xcframework.zip`);
  if (manifest.swiftBinaryChecksum !== swift.sha256) {
    throw new Error("Swift binary checksum does not match the release asset");
  }
  return manifest;
}

if (process.argv[1] && basename(process.argv[1]) === "verify-release.mjs") {
  const directory = resolve(argument("directory", "release-assets"));
  const manifest = verifyRelease(directory, {
    version: argument("version"),
    skipArchiveBoundaries: process.argv.includes("--skip-archive-boundaries"),
  });
  process.stdout.write(
    `verified ${manifest.mode} release ${manifest.tag} at ${manifest.commit}\n`,
  );
}
