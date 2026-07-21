// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { copyFileSync, mkdirSync, readdirSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { assetsForVersion } from "./release-contract.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}

const input = argument("input");
const output = argument("output");
const version = argument("version");
if (!input || !output || !version) throw new Error("--input, --output, and --version are required");
const expected = new Set(
  assetsForVersion(version)
    .filter(({ kind }) => kind !== "corresponding-source" && kind !== "aggregate-sbom" && kind !== "aggregate-notices")
    .map(({ name }) => name),
);
const matches = new Map();
for (const path of walk(resolve(input))) {
  const name = basename(path);
  if (!expected.has(name)) continue;
  if (matches.has(name)) throw new Error(`duplicate release payload from platform jobs: ${name}`);
  matches.set(name, path);
}
const missing = [...expected].filter((name) => !matches.has(name));
if (missing.length) throw new Error(`platform jobs did not produce: ${missing.join(", ")}`);
mkdirSync(resolve(output), { recursive: true });
for (const [name, path] of matches) copyFileSync(path, join(resolve(output), name));
process.stdout.write(`collected ${matches.size} platform and SDK payloads\n`);
