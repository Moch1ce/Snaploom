// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

const directory = resolve(argument("directory") ?? ".");
const version = argument("version");
const commit = argument("commit");
const expected = ["macos-14-arm64", "windows-10-x64", "windows-11-x64"];
const documents = readdirSync(directory, { recursive: true })
  .filter((name) => name.endsWith(".json"))
  .map((name) => JSON.parse(readFileSync(join(directory, name), "utf8")));
const byPlatform = new Map(documents.map((document) => [document.platform, document]));
if (byPlatform.size !== expected.length || documents.length !== expected.length) {
  throw new Error("qualification set must contain exactly three unique platform evaluations");
}
for (const platform of expected) {
  const document = byPlatform.get(platform);
  if (
    document?.passed !== true ||
    document.version !== version ||
    document.commit !== commit ||
    !document.contracts?.includes("PERF-01") ||
    !document.contracts?.includes("QA-03")
  ) {
    throw new Error(`qualification set does not approve ${platform} for ${commit}`);
  }
}
process.stdout.write(`qualified ${expected.join(", ")} for ${commit}\n`);
