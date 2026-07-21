// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { lstatSync, readFileSync, readdirSync } from "node:fs";
import { basename, join, relative, resolve } from "node:path";

function walk(root, directory = root) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return walk(root, path);
    if (entry.isFile()) return [path];
    if (entry.isSymbolicLink()) {
      throw new Error(`tree hash refuses symlink: ${relative(root, path)}`);
    }
    return [];
  });
}

export function hashTree(root) {
  const digest = createHash("sha256");
  for (const path of walk(root).sort()) {
    const name = relative(root, path).replaceAll("\\", "/");
    const stat = lstatSync(path);
    digest.update(`${name}\0${stat.mode & 0o777}\0${stat.size}\0`);
    digest.update(readFileSync(path));
    digest.update("\0");
  }
  return digest.digest("hex");
}

if (process.argv[1] && basename(process.argv[1]) === "hash-tree.mjs") {
  const root = process.argv[2];
  if (!root) throw new Error("usage: hash-tree.mjs <directory>");
  process.stdout.write(`${hashTree(resolve(root))}\n`);
}
