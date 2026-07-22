// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { writeFileSync } from "node:fs";
import { basename, resolve } from "node:path";
import {
  hostedQualificationIdentity,
  hostedQualificationLimitations,
} from "./hosted-qualification-policy.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

export function createHostedQualificationEvidence({
  platform,
  runnerImage,
  architecture,
  version,
  commit,
}) {
  const identity = hostedQualificationIdentity(platform);
  if (
    !identity ||
    identity.runnerImage !== runnerImage ||
    identity.architecture !== architecture
  ) {
    throw new Error("unsupported GitHub-hosted qualification identity");
  }
  if (!/^\d+\.\d+\.\d+$/.test(version ?? "") || !/^[0-9a-f]{40}$/.test(commit ?? "")) {
    throw new Error("hosted qualification requires an exact version and commit");
  }
  return {
    schemaVersion: 1,
    executionEnvironment: "github-hosted",
    platform,
    runnerImage,
    architecture,
    version,
    commit,
    passed: true,
    contracts: [...identity.contracts],
    limitations: [...hostedQualificationLimitations],
  };
}

if (
  process.argv[1] &&
  basename(process.argv[1]) === "create-hosted-qualification-evidence.mjs"
) {
  const output = argument("output");
  if (!output) {
    throw new Error(
      "--output, --platform, --runner-image, --architecture, --version, and --commit are required",
    );
  }
  const evidence = createHostedQualificationEvidence({
    platform: argument("platform"),
    runnerImage: argument("runner-image"),
    architecture: argument("architecture"),
    version: argument("version"),
    commit: argument("commit"),
  });
  writeFileSync(resolve(output), `${JSON.stringify(evidence, null, 2)}\n`, "utf8");
  process.stdout.write(
    `GitHub-hosted qualification passed for ${evidence.platform} on ${evidence.runnerImage}: ${evidence.contracts.join(", ")}\n`,
  );
}
