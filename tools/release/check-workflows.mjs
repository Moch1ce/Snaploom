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
const stableSdkPromotion = readFileSync(
  join(workflowDirectory, "release-promote-sdk.yml"),
  "utf8",
);
const dotnetPackageVerifier = readFileSync(
  join(repository, "tools", "sdk", "verify-dotnet-package.mjs"),
  "utf8",
);
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
  "1462876c00c07d44f1df56f341f7828040b55a962efaec250013dda9c45aa5fb";
const approvedStableSdkPromotionJobSha256 =
  "c05a4ce09af0a2f26ff63ffb5947a127017257e49b1137d373b8a1d3ba18bbcf";
const approvedStablePublishVerifierSha256 = {
  "verify-stable-publish.mjs": "64e50bf26cb2d5843fba724c7b7e7dd76008348b0e8ad675895c221ab13a66c1",
  "verify-draft-release.mjs": "4a3ef40958ab07527cf640126171dd4139c0a6e98b0fa17911e09aac4a6b37f7",
  "release-contract.mjs": "2be72154dbf50c61b2a985d3106e0dbdbc3e349e45edf7bcb18c293800335994",
  "verify-release.mjs": "4f4ae402d5eb2b8b4b0b39c0cb7911b3d8cde59c29e224ab605296d0b90d4a68",
};

function canonicalText(contents) {
  return contents.replace(/\r\n?/g, "\n");
}

export function checkDotnetConsumerRestorePolicy(contents) {
  if (
    (contents.match(/\n\s*"publish",/g) ?? []).length !== 2 ||
    (contents.match(/"--no-restore"/g) ?? []).length < 3 ||
    (contents.match(/restoreLocked\(/g) ?? []).length < 5 ||
    !contents.includes('rollForward: "disable"') ||
    !contents.includes("expected .NET SDK") ||
    !contents.includes("consumer-trimmed") ||
    !contents.includes("consumer-aot")
  ) {
    throw new Error(
      ".NET consumers must pin one SDK and locked-restore normal, trim, and NativeAOT builds",
    );
  }
}

export function checkDotnetVerifierCallSites(workflows) {
  for (const contents of workflows) {
    const calls = canonicalText(contents).split(
      "node tools/sdk/verify-dotnet-package.mjs",
    );
    for (const block of calls.slice(1)) {
      if (!block.split("\n\n", 1)[0].includes("--sdk-version")) {
        throw new Error("every .NET package verifier call must pass its selected SDK version");
      }
    }
  }
}

export function checkStableSdkPromotionPolicy(contents) {
  const canonicalContents = canonicalText(contents);
  const validateStart = canonicalContents.indexOf("\n  validate:");
  const publishStart = canonicalContents.indexOf("\n  publish-nuget:");
  const consumersStart = canonicalContents.indexOf("\n  dotnet-consumer:");
  const validateJob = canonicalContents.slice(validateStart, publishStart);
  const publishJob = canonicalContents.slice(publishStart, consumersStart);
  const oidcLogin =
    "NuGet/login@8d196754b4036150537f80ac539e15c2f1028841";
  if (
    validateStart < 0 ||
    publishStart < 0 ||
    consumersStart <= publishStart ||
    !publishJob.includes("needs: validate") ||
    !publishJob.includes("environment: nuget-org") ||
    !publishJob.includes("contents: read") ||
    !publishJob.includes("id-token: write") ||
    (canonicalContents.match(/id-token:\s+write/g) ?? []).length !== 1 ||
    (canonicalContents.match(new RegExp(oidcLogin.replace("/", "\\/"), "g")) ?? [])
      .length !== 1
  ) {
    throw new Error("stable SDK promotion must use one protected NuGet OIDC job after validation");
  }
  if (
    !validateJob.includes("node tools/release/verify-public-release.mjs") ||
    !validateJob.includes("node tools/sdk/verify-dotnet-package.mjs") ||
    !validateJob.includes("fetch-depth: 0") ||
    !validateJob.includes("refs/remotes/origin/main")
  ) {
    throw new Error(
      "stable SDK promotion must validate public release and package code before the OIDC job",
    );
  }
  const redownloadIndex = publishJob.indexOf(
    "Redownload the previously verified public NuGet packages",
  );
  const loginIndex = publishJob.indexOf(oidcLogin);
  const pushIndex = publishJob.indexOf("dotnet nuget push");
  if (
    redownloadIndex < 0 ||
    loginIndex <= redownloadIndex ||
    pushIndex <= loginIndex ||
    (publishJob.match(/dotnet nuget push/g) ?? []).length !== 2 ||
    !publishJob.includes('--source "https://api.nuget.org/v3/index.json"') ||
    !publishJob.includes('--api-key "${NUGET_API_KEY}"') ||
    !publishJob.includes("steps.nuget-login.outputs.NUGET_API_KEY") ||
    !publishJob.includes("Snaploom.Capture.${VERSION}.snupkg") ||
    !publishJob.includes(".immutable == true") ||
    !publishJob.includes("sha256sum") ||
    !publishJob.includes("'.digest'") ||
    !publishJob.includes("dotnet nuget verify --all") ||
    !publishJob.includes("Signature type: Repository") ||
    !publishJob.includes("NuGet.org package content differs") ||
    (publishJob.match(/steps\.registry-version\.outputs\.exists == 'false'/g) ?? [])
      .length !== 1 ||
    (publishJob.match(/--no-symbols/g) ?? []).length !== 1
  ) {
    throw new Error(
      "stable SDK promotion must reverify and publish the exact public NuGet pair once",
    );
  }
  if (
    /^ {2}(?:push|pull_request|schedule|workflow_run|repository_dispatch|workflow_call):/m.test(
      canonicalContents,
    ) ||
    /contents:\s+write|--skip-duplicate|secrets\.NUGET_API_KEY|NUGET_AUTH_TOKEN/i.test(
      canonicalContents,
    ) ||
    /(?:dotnet|nuget)\s+pack\b|gh\s+release\s+(?:create|edit|upload|delete)|gh\s+api[^\n]*(?:--method|-X)\s*(?:POST|PUT|PATCH|DELETE)\b/i.test(
      canonicalContents,
    )
  ) {
    throw new Error("stable SDK promotion contains a replay, rebuild, or release mutation path");
  }
  if (
    publishJob.includes("actions/checkout") ||
    /(?:^|\n)\s*node\s|dotnet\s+(?:build|restore|run|test|publish|pack)\b/i.test(
      publishJob,
    )
  ) {
    throw new Error(
      "stable SDK promotion cannot execute repository or package code with OIDC permission",
    );
  }
  const commandPlanSha256 = createHash("sha256")
    .update(canonicalText(publishJob))
    .digest("hex");
  if (commandPlanSha256 !== approvedStableSdkPromotionJobSha256) {
    throw new Error("stable SDK promotion OIDC command plan is not allowlisted");
  }
  if (
    !canonicalContents.includes("Validate immutable public release") ||
    !canonicalContents.includes("verify-public-release.mjs") ||
    !canonicalContents.includes("--consumer-source \"https://api.nuget.org/v3/index.json\"") ||
    !canonicalContents.includes('--sdk-version "${{ matrix.dotnet_version }}"') ||
    !canonicalContents.includes("8.0.423") ||
    !canonicalContents.includes("10.0.302") ||
    (canonicalContents.match(
      /actions\/setup-python@ece7cb06caefa5fff74198d8649806c4678c61a1/g,
    ) ?? []).length !== 3 ||
    !canonicalContents.includes('python-version: "3.13.14"') ||
    (canonicalContents.match(/platform: windows-x64/g) ?? []).length !== 2 ||
    (canonicalContents.match(/platform: macos-arm64/g) ?? []).length !== 2 ||
    !canonicalContents.includes("verify-stable-swift-consumer.mjs") ||
    !canonicalContents.includes("CSnaploomCapture-${VERSION}.xcframework.zip") ||
    !canonicalContents.includes("Xcode_16.4.app") ||
    !canonicalContents.includes("swift --version") ||
    (canonicalContents.match(/ImageOS/g) ?? []).length < 4 ||
    (canonicalContents.match(/ImageVersion/g) ?? []).length < 4 ||
    !canonicalContents.includes("toolchainsRecordedInJobSummaries:true")
  ) {
    throw new Error("stable SDK promotion must prove all clean public package consumers");
  }
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
  !legalRc.includes("issue-33-owner-approval") ||
  !legalRc.includes("environment: release-signing") ||
  !legalRc.includes("environment: release-draft") ||
  !legalRc.includes(".NET final consumer") ||
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
  throw new Error("legal RC workflow must create only a protected, owner-approved draft");
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
  stablePublish.includes("vars.LEGAL_REVIEWER_LOGINS") ||
  stablePublish.includes("--allowed-reviewers") ||
  !stablePublish.includes("owner_approval_comment_id") ||
  publishJobStart < 0 ||
  evidenceJobStart < 0 ||
  !stablePublish.includes("needs: publish") ||
  (stablePublish.match(/actions\/upload-artifact/g) ?? []).length !== 1 ||
  (stablePublish.match(/collaborators\/\$\{GITHUB_REPOSITORY_OWNER\}\/permission/g) ?? [])
    .length < 2 ||
  (stablePublish.match(/--owner-permission-json/g) ?? []).length < 3 ||
  (stablePublish.match(/--issue-comments-json/g) ?? []).length < 3 ||
  (stablePublish.match(/--owner-approval-comment-id/g) ?? []).length < 3 ||
  (stablePublish.match(/gh api --paginate --slurp/g) ?? []).length !== 2 ||
  (stablePublish.match(/fetch-depth: 0/g) ?? []).length !== 2 ||
  (stablePublish.match(/verify-stable-publish\.mjs/g) ?? []).length < 2 ||
  !stablePublish.includes(".immutable == true") ||
  !stablePublish.includes("draft=false") ||
  !stablePublish.includes("prerelease=false") ||
  !stablePublish.includes("make_latest=true") ||
  (stablePublish.match(/contents:\s+write/g) ?? []).length !== 1 ||
  (stablePublish.match(/--method PATCH/g) ?? []).length !== 1
) {
  throw new Error(
    "stable workflow must reverify one owner-approved immutable draft before one publish transition",
  );
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
checkStableSdkPromotionPolicy(stableSdkPromotion);
checkDotnetConsumerRestorePolicy(dotnetPackageVerifier);
checkDotnetVerifierCallSites([pullRequestCi, legalRc, stableSdkPromotion]);
process.stdout.write(`verified immutable action/release references in ${basename(workflowDirectory)}\n`);
