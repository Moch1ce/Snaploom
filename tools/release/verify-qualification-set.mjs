// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, readdirSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { isDeepStrictEqual } from "node:util";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

const expected = [
  {
    platform: "macos-arm64",
    runnerImage: "macos-15",
    architecture: "arm64",
    contracts: ["QA-02", "DIST-03"],
  },
  {
    platform: "windows-x64",
    runnerImage: "windows-2025",
    architecture: "x86_64",
    contracts: ["QA-02", "DIST-02"],
  },
];
const hostedLimitations = ["no-interactive-desktop", "no-real-machine-performance"];

export function verifyQualificationSet(directory, { version, commit }) {
  const documents = readdirSync(directory, { recursive: true })
    .filter((name) => name.endsWith(".json"))
    .map((name) => JSON.parse(readFileSync(join(directory, name), "utf8")));
  const byPlatform = new Map(documents.map((document) => [document.platform, document]));
  if (byPlatform.size !== expected.length || documents.length !== expected.length) {
    throw new Error("qualification set must contain exactly two unique hosted platform evaluations");
  }
  for (const identity of expected) {
    const document = byPlatform.get(identity.platform);
    if (
      document?.schemaVersion !== 1 ||
      document.executionEnvironment !== "github-hosted" ||
      document?.passed !== true ||
      document.version !== version ||
      document.commit !== commit ||
      document.runnerImage !== identity.runnerImage ||
      document.architecture !== identity.architecture ||
      !isDeepStrictEqual(document.contracts, identity.contracts) ||
      !isDeepStrictEqual(document.limitations, hostedLimitations)
    ) {
      throw new Error(
        `hosted qualification set does not approve ${identity.platform} for ${commit}`,
      );
    }
  }
  return expected.map(({ platform }) => byPlatform.get(platform));
}

if (process.argv[1] && basename(process.argv[1]) === "verify-qualification-set.mjs") {
  const directory = resolve(argument("directory") ?? ".");
  const version = argument("version");
  const commit = argument("commit");
  verifyQualificationSet(directory, { version, commit });
  process.stdout.write(
    `qualified ${expected.map(({ platform }) => platform).join(", ")} on GitHub-hosted runners for ${commit}\n`,
  );
}
