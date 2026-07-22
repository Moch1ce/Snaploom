// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { lstatSync, readFileSync, readdirSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { expectedReleaseFiles } from "./release-contract.mjs";
import { verifyRelease } from "./verify-release.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

export function verifyPublicRelease({
  release,
  directory,
  version,
  commit,
  skipArchiveBoundaries = false,
}) {
  const manifest = verifyRelease(directory, { version, skipArchiveBoundaries });
  if (manifest.mode !== "stable-signed" || manifest.commit !== commit) {
    throw new Error(
      "public release does not contain the stable-signed manifest for the requested commit",
    );
  }
  verifyPublicReleaseIdentity(release, { version, commit });

  const expectedNames = expectedReleaseFiles(version);
  const releaseAssets = release.assets ?? [];
  const actualNames = releaseAssets.map(({ name }) => name).sort();
  if (
    releaseAssets.length !== expectedNames.length ||
    new Set(actualNames).size !== expectedNames.length ||
    JSON.stringify(actualNames) !== JSON.stringify(expectedNames)
  ) {
    throw new Error("GitHub stable release does not contain the exact authoritative asset set");
  }
  verifyPublicAssetSubset({
    release,
    directory,
    names: expectedNames,
    version,
    commit,
  });
  return { release, manifest };
}

function verifyPublicReleaseIdentity(release, { version, commit }) {
  if (
    release?.draft !== false ||
    release.prerelease !== false ||
    release.immutable !== true ||
    typeof release.published_at !== "string" ||
    !Number.isFinite(Date.parse(release.published_at)) ||
    release.tag_name !== `v${version}` ||
    release.resolved_tag_commit !== commit ||
    release.name !== `Snaploom ${version}` ||
    typeof release.body !== "string" ||
    release.body.trim().length === 0 ||
    !Number.isInteger(release.id) ||
    !/^https:\/\/github\.com\//.test(release.html_url ?? "")
  ) {
    throw new Error("GitHub release is not an immutable public stable release");
  }
}

export function verifyPublicAssetSubset({
  release,
  directory,
  names,
  version,
  commit,
}) {
  verifyPublicReleaseIdentity(release, { version, commit });
  const expectedNames = expectedReleaseFiles(version);
  if (
    !Array.isArray(names) ||
    names.length === 0 ||
    new Set(names).size !== names.length ||
    names.some((name) => !expectedNames.includes(name))
  ) {
    throw new Error("public release asset subset is not part of the authoritative contract");
  }
  const downloadedNames = readdirSync(directory).sort();
  if (JSON.stringify(downloadedNames) !== JSON.stringify([...names].sort())) {
    throw new Error("download directory does not contain the exact requested public asset subset");
  }
  const assets = release.assets ?? [];
  for (const name of names) {
    const matches = assets.filter((asset) => asset.name === name);
    if (matches.length !== 1) {
      throw new Error(`GitHub stable release asset is missing or duplicated: ${name}`);
    }
    const [asset] = matches;
    const path = join(directory, asset.name);
    const stat = lstatSync(path);
    const digest = sha256(path);
    if (
      asset.state !== "uploaded" ||
      asset.size !== stat.size ||
      asset.digest !== `sha256:${digest}`
    ) {
      throw new Error(`GitHub stored asset metadata differs from downloaded bytes: ${asset.name}`);
    }
  }
  for (const name of names.filter((entry) => !entry.endsWith(".sha256"))) {
    if (!names.includes(`${name}.sha256`)) continue;
    const expected = `${sha256(join(directory, name))}  ${name}\n`;
    if (readFileSync(join(directory, `${name}.sha256`), "utf8") !== expected) {
      throw new Error(`public checksum sidecar differs from downloaded bytes: ${name}`);
    }
  }
  return { release, names };
}

if (process.argv[1] && basename(process.argv[1]) === "verify-public-release.mjs") {
  const releasePath = argument("release-json");
  const directory = resolve(argument("directory") ?? "release-download");
  const version = argument("version");
  const commit = argument("commit");
  if (!releasePath || !version || !commit) {
    throw new Error("--release-json, --directory, --version, and --commit are required");
  }
  const release = JSON.parse(readFileSync(resolve(releasePath), "utf8"));
  const names = argument("asset-names")?.split(",");
  if (names) {
    verifyPublicAssetSubset({ release, directory, names, version, commit });
    process.stdout.write(
      `verified ${names.length} exact assets from immutable public GitHub release ${release.id}\n`,
    );
  } else {
    const { manifest } = verifyPublicRelease({
      release,
      directory,
      version,
      commit,
    });
    process.stdout.write(
      `verified immutable public GitHub release ${release.id} for ${manifest.tag} at ${manifest.commit}\n`,
    );
  }
}
