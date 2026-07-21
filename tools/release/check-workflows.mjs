// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, readdirSync } from "node:fs";
import { basename, join } from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));
const workflowDirectory = join(repository, ".github", "workflows");
for (const name of readdirSync(workflowDirectory).filter((entry) => /\.ya?ml$/.test(entry))) {
  const contents = readFileSync(join(workflowDirectory, name), "utf8");
  for (const match of contents.matchAll(/^\s*uses:\s*([^\s#]+)(?:\s+#.*)?$/gm)) {
    const reference = match[1];
    if (reference.startsWith("./")) continue;
    const revision = reference.slice(reference.lastIndexOf("@") + 1);
    if (!/^[0-9a-f]{40}$/.test(revision)) {
      throw new Error(`${name} uses a mutable action reference: ${reference}`);
    }
  }
  if (/gh\s+release\s+upload[^\n]*--clobber/.test(contents)) {
    throw new Error(`${name} can overwrite immutable release assets`);
  }
}

const legalRc = readFileSync(join(workflowDirectory, "release-legal-rc.yml"), "utf8");
const pullRequestCi = readFileSync(join(workflowDirectory, "ci.yml"), "utf8");
if (
  !pullRequestCi.includes("--skip-manifest-checksum") ||
  legalRc.includes("--skip-manifest-checksum")
) {
  throw new Error(
    "branch CI must defer the runner-specific Swift checksum while legal RC enforces it",
  );
}
if (
  !pullRequestCi.includes("web-contracts:") ||
  !pullRequestCi.includes(
    "mcr.microsoft.com/playwright@sha256:5b8f294aff9041b7191c34a4bab3ac270157a28774d4b0660e9743297b697e48",
  ) ||
  !pullRequestCi.includes('test "$(uname -m)" = x86_64') ||
  !pullRequestCi.includes("- web-contracts")
) {
  throw new Error(
    "branch CI must gate the atomic release contract on pinned Chromium render goldens",
  );
}
if (
  !legalRc.includes("draft:true") ||
  !legalRc.includes("Stable publish remains blocked by Issue #33") ||
  !legalRc.includes("environment: release-signing") ||
  !legalRc.includes("environment: release-draft") ||
  !legalRc.includes(".NET signed consumer") ||
  !legalRc.includes("refs/snaploom-legal-rc") ||
  !legalRc.includes("apple-signing-evidence") ||
  !legalRc.includes("legal-rc-run.json") ||
  !legalRc.includes("3.13.14") ||
  !legalRc.includes("10.0.26100.0") ||
  !legalRc.includes("-winsdk=10.0.26100.0") ||
  !legalRc.includes("Xcode_16.4.app") ||
  !legalRc.includes("8.0.423") ||
  !legalRc.includes("10.0.302")
) {
  throw new Error("legal RC workflow must create only a protected, externally reviewed draft");
}
if (
  /(?:draft["']?\s*:\s*false|make_latest|gh\s+release\s+edit|--draft=false|(?:--method|--request)\s+PATCH)/.test(legalRc) ||
  /^ {2}(?:push|pull_request|schedule|workflow_run|repository_dispatch|workflow_call):/m.test(
    legalRc,
  ) ||
  (legalRc.match(/contents:\s+write/g) ?? []).length !== 1
) {
  throw new Error("legal RC workflow contains a stable/public or untrusted trigger path");
}
process.stdout.write(`verified immutable action/release references in ${basename(workflowDirectory)}\n`);
