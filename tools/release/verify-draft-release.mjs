// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { lstatSync, readFileSync } from "node:fs";
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

export function verifyDraftRelease({
  release,
  directory,
  version,
  commit,
  skipArchiveBoundaries = false,
}) {
  const manifest = verifyRelease(directory, { version, skipArchiveBoundaries });
  if (manifest.mode !== "stable-release" || manifest.commit !== commit) {
    throw new Error("draft does not contain the stable release manifest for the requested commit");
  }
  if (
    release?.draft !== true ||
    release.prerelease !== false ||
    release.published_at !== null ||
    release.tag_name !== `v${version}` ||
    release.resolved_tag_commit !== commit ||
    typeof release.target_commitish !== "string" ||
    release.target_commitish.length === 0 ||
    !Number.isInteger(release.id) ||
    !/^https:\/\//.test(release.html_url ?? "")
  ) {
    throw new Error("GitHub release metadata is not an unpublished immutable-subject draft");
  }

  const expectedNames = expectedReleaseFiles(version);
  const assets = release.assets ?? [];
  const actualNames = assets.map(({ name }) => name).sort();
  if (
    assets.length !== expectedNames.length ||
    new Set(actualNames).size !== expectedNames.length ||
    JSON.stringify(actualNames) !== JSON.stringify(expectedNames)
  ) {
    throw new Error("GitHub draft does not contain the exact authoritative asset set");
  }
  for (const asset of assets) {
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
  return { release, manifest };
}

if (process.argv[1] && basename(process.argv[1]) === "verify-draft-release.mjs") {
  const releasePath = argument("release-json");
  const directory = resolve(argument("directory") ?? "release-download");
  const version = argument("version");
  const commit = argument("commit");
  if (!releasePath || !version || !commit) {
    throw new Error("--release-json, --directory, --version, and --commit are required");
  }
  const { release, manifest } = verifyDraftRelease({
    release: JSON.parse(readFileSync(resolve(releasePath), "utf8")),
    directory,
    version,
    commit,
  });
  process.stdout.write(
    `verified GitHub draft ${release.id} for stable release ${manifest.tag} at ${manifest.commit}\n`,
  );
}
