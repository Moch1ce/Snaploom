// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { lstatSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, join, relative, resolve } from "node:path";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return walk(path);
    return entry.isFile() ? [path] : [];
  });
}

export function generateFileSbom({ root, output, name, version, license }) {
  const outputPath = resolve(output);
  const components = walk(root)
    .filter((path) => resolve(path) !== outputPath)
    .map((path) => {
      const relativePath = relative(root, path).replaceAll("\\", "/");
      const stat = lstatSync(path);
      if (!stat.isFile() || stat.isSymbolicLink()) {
        throw new Error(`SBOM input must be a regular file: ${relativePath}`);
      }
      return {
        type: "file",
        "bom-ref": `file:${relativePath}`,
        name: relativePath,
        version,
        hashes: [
          {
            alg: "SHA-256",
            content: createHash("sha256").update(readFileSync(path)).digest("hex"),
          },
        ],
        properties: [{ name: "snaploom:file-bytes", value: String(stat.size) }],
      };
    })
    .sort((left, right) => left.name.localeCompare(right.name, "en"));
  const document = {
    bomFormat: "CycloneDX",
    specVersion: "1.5",
    version: 1,
    metadata: {
      component: {
        type: "application",
        "bom-ref": `pkg:generic/${encodeURIComponent(name)}@${version}`,
        name,
        version,
        licenses: [{ license: { id: license } }],
      },
    },
    components,
  };
  writeFileSync(outputPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");
  return document;
}

if (process.argv[1] && basename(process.argv[1]) === "generate-file-sbom.mjs") {
  const root = argument("root");
  const output = argument("output");
  const name = argument("name");
  const version = argument("version");
  const license = argument("license");
  if (!root || !output || !name || !version || !license) {
    throw new Error("--root, --output, --name, --version, and --license are required");
  }
  const document = generateFileSbom({
    root: resolve(root),
    output: resolve(output),
    name,
    version,
    license,
  });
  process.stdout.write(`generated SBOM with ${document.components.length} packaged files\n`);
}
