// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { spawnSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { resolve } from "node:path";

const [manifestPath, outputPath] = process.argv.slice(2);
if (!manifestPath || !outputPath) {
  throw new Error("usage: generate-rust-notice.mjs <Cargo.toml> <output>");
}

const cargo = process.env.SNAPLOOM_CARGO ?? "cargo";
const cargoArguments = [
  ...(process.env.SNAPLOOM_RUSTC
    ? ["--config", `build.rustc=${JSON.stringify(process.env.SNAPLOOM_RUSTC)}`]
    : []),
  "metadata",
  "--format-version",
  "1",
  "--locked",
  "--all-features",
  "--manifest-path",
  manifestPath,
];
const metadata = spawnSync(cargo, cargoArguments, {
  encoding: "utf8",
  maxBuffer: 64 * 1024 * 1024,
});
if (metadata.status !== 0) {
  throw new Error(
    metadata.stderr || metadata.error?.message || "cargo metadata failed",
  );
}

const graph = JSON.parse(metadata.stdout);
const workspace = new Set(graph.workspace_members);
const forbiddenDirect = new Set([
  "scrap",
  "windows",
  "windows-capture",
  "windows-sys",
  "xcap",
]);
for (const pkg of graph.packages.filter((candidate) => workspace.has(candidate.id))) {
  for (const dependency of pkg.dependencies) {
    if (forbiddenDirect.has(dependency.name)) {
      throw new Error(
        `${pkg.name} directly depends on forbidden capture/umbrella crate ${dependency.name}`,
      );
    }
  }
}

const thirdParty = graph.packages
  .filter((pkg) => !workspace.has(pkg.id) && pkg.source !== null)
  .map((pkg) => {
    if (!pkg.license) {
      throw new Error(`${pkg.name} ${pkg.version} has no SPDX license expression`);
    }
    return `${pkg.name} ${pkg.version} | ${pkg.license} | ${pkg.source ?? pkg.repository ?? "local"}`;
  })
  .sort((left, right) => left.localeCompare(right, "en"));

const notice = [
  "Snaploom product Rust third-party dependency notice",
  "Generated from locked Cargo metadata; release packaging performs artifact-specific attribution.",
  "",
  ...thirdParty,
  "",
].join("\n");
writeFileSync(resolve(outputPath), notice, "utf8");
