// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { assertVersion } from "./release-contract.mjs";

const repository = fileURLToPath(new URL("../../", import.meta.url));

function argument(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

function read(path) {
  return readFileSync(join(repository, path), "utf8");
}

function tomlValue(path, section, key) {
  const contents = read(path);
  const escapedSection = section.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = contents.match(
    new RegExp(`(?:^|\\n)\\[${escapedSection}\\]\\n([\\s\\S]*?)(?=\\n\\[|$)`),
  );
  const value = match?.[1].match(new RegExp(`^${key}\\s*=\\s*"([^"]+)"`, "m"))?.[1];
  if (!value) throw new Error(`could not read ${section}.${key} from ${path}`);
  return value;
}

export function checkReleaseVersion(version) {
  assertVersion(version);
  const observed = new Map();
  observed.set("package.json", JSON.parse(read("package.json")).version);
  observed.set("product workspace", tomlValue("product/Cargo.toml", "workspace.package", "version"));
  observed.set("SDK workspace", tomlValue("sdk/Cargo.toml", "workspace.package", "version"));
  for (const configPath of [
    "product/apps/desktop/tauri.conf.json",
    "product/apps/capture-host/tauri.conf.json",
  ]) {
    observed.set(configPath, JSON.parse(read(configPath)).version);
  }
  const project = read("sdk/dotnet/src/Snaploom.Capture/Snaploom.Capture.csproj");
  observed.set("NuGet", project.match(/<Version>([^<]+)<\/Version>/)?.[1]);
  const swift = read("Package.swift");
  observed.set(
    "Swift release URL",
    swift.match(/releases\/download\/v([^/]+)\/CSnaploomCapture-/)?.[1],
  );
  const mismatch = [...observed].filter(([, actual]) => actual !== version);
  if (mismatch.length !== 0) {
    throw new Error(
      `release version ${version} is inconsistent:\n${mismatch
        .map(([source, actual]) => `${source}: ${actual ?? "missing"}`)
        .join("\n")}`,
    );
  }
  const cxxMajor = read("sdk/cpp/include/snaploom/snaploom_capture.hpp").match(
    /sdk_semver_major_v1\s*=\s*(\d+)/,
  )?.[1];
  if (cxxMajor !== version.split(".")[0]) {
    throw new Error("C++ SDK compatibility major does not match the release version");
  }
  return observed;
}

if (process.argv[1] && basename(process.argv[1]) === "check-release-version.mjs") {
  const version = argument("version");
  if (!version) throw new Error("--version is required");
  const observed = checkReleaseVersion(version);
  process.stdout.write(`release version ${version} matches ${observed.size + 1} authoritative inputs\n`);
}
