// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
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
const stablePublish = readFileSync(join(workflowDirectory, "release-stable.yml"), "utf8");
const stablePublishVerifierNames = [
  "verify-stable-publish.mjs",
  "verify-draft-release.mjs",
  "release-contract.mjs",
  "verify-release.mjs",
];
const stablePublishVerifierClosure = Object.fromEntries(
  stablePublishVerifierNames.map((name) => [
    name,
    readFileSync(join(repository, "tools", "release", name), "utf8"),
  ]),
);
const publishJobStart = stablePublish.indexOf("\n  publish:");
const evidenceJobStart = stablePublish.indexOf("\n  record-evidence:");
const pullRequestCi = readFileSync(join(workflowDirectory, "ci.yml"), "utf8");
const approvedStablePublishJobSha256 =
  "7547b9b1ab73aaa15b7ed1ef99d06fa8e902f445a3f2e663c1f560aff2417307";
const approvedStablePublishVerifierSha256 = {
  "verify-stable-publish.mjs": "b479a5473baddef54c98689fd8cd76b39a65810e3cedda4b345973ceb355d70b",
  "verify-draft-release.mjs": "3216d1180f5e05a58e7771a428438c7d7ba35db765386708a40c90456fc2a3de",
  "release-contract.mjs": "d1a224cfa4c6d95f9b844634c7f4f34ebed8f13618c9f5e3762a6496f56f9aec",
  "verify-release.mjs": "f71ae74c290d4cc76a9b0b04ec87617fac417e3c83812ff7aebebe028c83af8c",
};

function canonicalText(contents) {
  return contents.replace(/\r\n?/g, "\n");
}

export function checkStablePublishVerifier(closure) {
  const names = Object.keys(closure ?? {}).sort();
  if (
    names.length !== stablePublishVerifierNames.length ||
    !names.every((name, index) => name === [...stablePublishVerifierNames].sort()[index]) ||
    names.some(
      (name) =>
        createHash("sha256").update(canonicalText(closure[name])).digest("hex") !==
        approvedStablePublishVerifierSha256[name],
    )
  ) {
    throw new Error("protected publish verifier implementation is not allowlisted");
  }
}

export function checkStablePublishMutationPolicy(contents) {
  const canonicalContents = canonicalText(contents);
  const publishStart = canonicalContents.indexOf("\n  publish:");
  const evidenceStart = canonicalContents.indexOf("\n  record-evidence:");
  if (publishStart < 0 || evidenceStart <= publishStart) {
    throw new Error("stable workflow must have distinct publish and evidence jobs");
  }
  const publishJob = canonicalContents.slice(publishStart, evidenceStart);
  if (publishJob.includes("actions/upload-artifact")) {
    throw new Error("protected publish job cannot upload artifacts");
  }
  const normalized = publishJob.replace(/\\\r?\n\s*/g, " ");
  if (
    /(?:^|\n)\s*(?:curl|wget|http|npm|pnpm|yarn|bun|cargo|rustc|go|dotnet|msbuild|make|cmake|ninja|xcodebuild|swift|codesign|signtool|gpg|cosign|openssl|scp|rsync|aws|az|gsutil|docker|podman|gh\s+release)\b/im.test(
      normalized,
    ) ||
    /(?:^|\n)\s*(?:(?:env|command|exec|sudo|nohup)(?:\s|$)|(?:\/|\.\.?\/)|(?:python\d*|ruby|perl|php|bash|sh|zsh|fish|deno)(?:\s|$))/im.test(
      normalized,
    )
  ) {
    throw new Error(
      "protected publish job contains a forbidden build, sign, upload, delete, or alternate mutation command",
    );
  }

  const nodeCalls = [...normalized.matchAll(/^\s*node\b[^\n]*/gm)].map((match) =>
    match[0].trim(),
  );
  if (
    nodeCalls.some(
      (call) =>
        !call.startsWith("node tools/release/verify-stable-publish.mjs ") ||
        /[;&|`<>]|\$\(/.test(call),
    )
  ) {
    throw new Error(
      "protected publish job contains a forbidden build, sign, upload, delete, or alternate mutation command",
    );
  }

  const apiCalls = [...normalized.matchAll(/^\s*gh api\b[^\n]*/gm)].map((match) =>
    match[0].trim(),
  );
  const methodOption = /(?:^|\s)(?:-X(?:[A-Za-z]+)?|--(?:method|request))(?=\s|=|$)/i;
  const methodOptions = /(?:^|\s)(?:-X(?:[A-Za-z]+)?|--(?:method|request))(?=\s|=|$)/gi;
  const fieldOption =
    /(?:^|\s)(?:-[fF](?=\s|[^-\s]|$)|--(?:field|raw-field|input)(?=\s|=|$))/i;
  const fieldOptions =
    /(?:^|\s)(?:-[fF](?=\s|[^-\s]|$)|--(?:field|raw-field|input)(?=\s|=|$))/gi;
  const patchCalls = apiCalls.filter((call) => /(?:^|\s)--method\s+PATCH\b/i.test(call));
  const expectedEndpoint = '"repos/${GITHUB_REPOSITORY}/releases/${DRAFT_RELEASE_ID}"';
  const patch = patchCalls[0] ?? "";
  const patchFields = patch.match(fieldOptions) ?? [];
  const patchMethods = patch.match(methodOptions) ?? [];
  if (
    patchCalls.length !== 1 ||
    !patch.includes(expectedEndpoint) ||
    !patch.includes("-F draft=false") ||
    !patch.includes("-F prerelease=false") ||
    !patch.includes("-f make_latest=true") ||
    patchFields.length !== 3 ||
    patchMethods.length !== 1 ||
    /(?:^|\s)--(?:field|raw-field|input)(?=\s|=|$)/.test(patch)
  ) {
    throw new Error("protected publish job must contain only one exact Release PATCH");
  }
  for (const call of apiCalls) {
    if (call === patch) continue;
    if (methodOption.test(call) || fieldOption.test(call)) {
      throw new Error("protected publish job must contain only one exact Release PATCH");
    }
  }
  const commandPlanSha256 = createHash("sha256")
    .update(canonicalText(publishJob))
    .digest("hex");
  if (commandPlanSha256 !== approvedStablePublishJobSha256) {
    throw new Error("protected publish job command plan is not allowlisted");
  }
}
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
  !legalRc.includes('name "Snaploom ${VERSION}"') ||
  !legalRc.includes("IPC does not automatically eliminate GPL compliance obligations") ||
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
if (
  !stablePublish.includes("environment: stable-release") ||
  !stablePublish.includes("vars.LEGAL_REVIEWER_LOGINS") ||
  publishJobStart < 0 ||
  evidenceJobStart < 0 ||
  !stablePublish.includes("needs: publish") ||
  (stablePublish.match(/actions\/upload-artifact/g) ?? []).length !== 1 ||
  (stablePublish.match(/collaborators\/\$\{reviewer_login\}\/permission/g) ?? []).length < 2 ||
  (stablePublish.match(/--reviewer-permission-json/g) ?? []).length < 3 ||
  (stablePublish.match(/fetch-depth: 0/g) ?? []).length !== 2 ||
  (stablePublish.match(/verify-stable-publish\.mjs/g) ?? []).length < 2 ||
  !stablePublish.includes(".immutable == true") ||
  !stablePublish.includes("draft=false") ||
  !stablePublish.includes("prerelease=false") ||
  !stablePublish.includes("make_latest=true") ||
  (stablePublish.match(/contents:\s+write/g) ?? []).length !== 1 ||
  (stablePublish.match(/--method PATCH/g) ?? []).length !== 1
) {
  throw new Error("stable workflow must reverify one immutable signed draft before one publish transition");
}
checkStablePublishMutationPolicy(stablePublish);
checkStablePublishVerifier(stablePublishVerifierClosure);
if (
  /^ {2}(?:push|pull_request|schedule|workflow_run|repository_dispatch|workflow_call):/m.test(
    stablePublish,
  ) ||
  stablePublish.includes("immutable-releases") ||
  /(?:gh\s+release\s+(?:create|upload)|--clobber|--method\s+DELETE|cargo\s+(?:build|run)|pnpm\s+(?:build|run\s+build)|dotnet\s+(?:build|pack)|codesign|signtool)/i.test(
    stablePublish,
  )
) {
  throw new Error("stable workflow can only publish an existing reviewed draft from a trusted dispatch");
}
process.stdout.write(`verified immutable action/release references in ${basename(workflowDirectory)}\n`);
