// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import {
  checkDotnetConsumerRestorePolicy,
  checkDotnetVerifierCallSites,
  checkStableSdkPromotionPolicy,
  checkStablePublishMutationPolicy,
  checkStablePublishVerifier,
} from "./check-workflows.mjs";

const workflowPath = fileURLToPath(
  new URL("../../.github/workflows/release-stable.yml", import.meta.url),
);
const workflow = readFileSync(workflowPath, "utf8");
const sdkPromotionWorkflow = readFileSync(
  fileURLToPath(
    new URL("../../.github/workflows/release-promote-sdk.yml", import.meta.url),
  ),
  "utf8",
);
const dotnetPackageVerifier = readFileSync(
  fileURLToPath(new URL("../sdk/verify-dotnet-package.mjs", import.meta.url)),
  "utf8",
);
const ciWorkflow = readFileSync(
  fileURLToPath(new URL("../../.github/workflows/ci.yml", import.meta.url)),
  "utf8",
);
const legalRcWorkflow = readFileSync(
  fileURLToPath(
    new URL("../../.github/workflows/release-legal-rc.yml", import.meta.url),
  ),
  "utf8",
);
const verifierClosure = Object.fromEntries(
  [
    "verify-stable-publish.mjs",
    "verify-draft-release.mjs",
    "release-contract.mjs",
    "verify-release.mjs",
  ].map((name) => [
    name,
    readFileSync(fileURLToPath(new URL(`./${name}`, import.meta.url)), "utf8"),
  ]),
);

function beforePublishPatch(command) {
  return workflow.replace(
    "          gh api --method PATCH",
    `          ${command}\n          gh api --method PATCH`,
  );
}

function asCrlf(contents) {
  return contents.replace(/\r\n?/g, "\n").replaceAll("\n", "\r\n");
}

test("stable publish mutation policy accepts the reviewed one-PATCH workflow", () => {
  assert.doesNotThrow(() => checkStablePublishMutationPolicy(workflow));
});

test("stable publish workflow authenticates owner approval without external reviewer configuration", () => {
  const publicationEvidenceStep = workflow.slice(
    workflow.indexOf("- name: Prove immutable publication and unchanged assets"),
    workflow.indexOf("evidence_json=", workflow.indexOf("- name: Prove immutable publication and unchanged assets")),
  );
  assert.match(workflow, /owner_approval_comment_id/);
  assert.match(workflow, /gh api --paginate --slurp/);
  assert.match(workflow, /--issue-comments-json/);
  assert.match(workflow, /--owner-approval-comment-id/);
  assert.match(workflow, /--owner-permission-json/);
  assert.doesNotMatch(workflow, /LEGAL_REVIEWER_LOGINS|--allowed-reviewers/);
  assert.doesNotMatch(workflow, /--review-comment-json|--reviewer-permission-json/);
  assert.doesNotMatch(workflow, /external legal approval/);
  assert.match(
    publicationEvidenceStep,
    /OWNER_APPROVAL_COMMENT_ID: \$\{\{ inputs\.owner_approval_comment_id \}\}/,
  );
});

test("stable publish mutation policy accepts an equivalent CRLF checkout", () => {
  assert.doesNotThrow(() => checkStablePublishMutationPolicy(asCrlf(workflow)));
});

test("stable publish mutation policy rejects extra REST mutations and implicit POST fields", () => {
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch('gh api --method POST "repos/${GITHUB_REPOSITORY}/releases"'),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch(
          'gh api "repos/${GITHUB_REPOSITORY}/releases/${DRAFT_RELEASE_ID}" -Fmutate=true',
        ),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch('gh api -XPOST "repos/${GITHUB_REPOSITORY}/releases"'),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch('gh api --method=DELETE "repos/${GITHUB_REPOSITORY}/releases/1"'),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch(
          'gh api "repos/${GITHUB_REPOSITORY}/releases/${DRAFT_RELEASE_ID}" -fdraft=false',
        ),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch('gh api -X PUT "repos/${GITHUB_REPOSITORY}/releases/1"'),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch(
          'gh api "repos/${GITHUB_REPOSITORY}/releases/${DRAFT_RELEASE_ID}" -f draft=false',
        ),
      ),
    /only one exact Release PATCH/,
  );
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        workflow.replace(
          "-F draft=false -F prerelease=false -f make_latest=true",
          "-F draft=false -F prerelease=false -f make_latest=true --input payload.json",
        ),
      ),
    /only one exact Release PATCH/,
  );
});

test("stable publish mutation policy rejects alternate mutation, build, sign, and upload tools", () => {
  for (const command of [
    'curl -X DELETE "${GITHUB_API_URL}/repos/${GITHUB_REPOSITORY}/releases/1"',
    "npm run build",
    'codesign --sign "${IDENTITY}" payload',
    "gh release upload v1.0.0 payload --clobber",
    'node -e "fetch(process.env.GITHUB_API_URL,{method:\'POST\'})"',
    'python3 -c "print(\'alternate mutation\')"',
    'command curl -X POST "${GITHUB_API_URL}"',
    'env node -e "process.exit(0)"',
    '/usr/bin/curl -X PUT "${GITHUB_API_URL}"',
  ]) {
    assert.throws(
      () => checkStablePublishMutationPolicy(beforePublishPatch(command)),
      /forbidden build, sign, upload, delete, or alternate mutation command/,
    );
  }
});

test("stable publish mutation policy keeps upload-artifact outside the protected publish job", () => {
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch(
          "uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
        ),
      ),
    /protected publish job cannot upload artifacts/,
  );
});

test("stable publish mutation policy rejects every unapproved protected-job command plan", () => {
  assert.throws(
    () =>
      checkStablePublishMutationPolicy(
        beforePublishPatch('busybox wget "${GITHUB_API_URL}/repos/${GITHUB_REPOSITORY}"'),
      ),
    /command plan is not allowlisted/,
  );
});

test("stable publish mutation policy pins the complete write-token verifier closure", () => {
  assert.doesNotThrow(() => checkStablePublishVerifier(verifierClosure));
  for (const name of Object.keys(verifierClosure)) {
    assert.throws(
      () =>
        checkStablePublishVerifier({
          ...verifierClosure,
          [name]: `${verifierClosure[name]}\n// unreviewed write-token behavior`,
        }),
      /verifier implementation is not allowlisted/,
    );
  }
});

test("stable publish verifier accepts an equivalent CRLF checkout", () => {
  assert.doesNotThrow(() =>
    checkStablePublishVerifier(
      Object.fromEntries(
        Object.entries(verifierClosure).map(([name, contents]) => [
          name,
          asCrlf(contents),
        ]),
      ),
    ),
  );
});

test("stable SDK promotion accepts only the reviewed OIDC and public-consumer workflow", () => {
  assert.doesNotThrow(() => checkStableSdkPromotionPolicy(sdkPromotionWorkflow));
  assert.doesNotThrow(() =>
    checkStableSdkPromotionPolicy(asCrlf(sdkPromotionWorkflow)),
  );
});

test("stable SDK promotion rejects weaker identity and replay policies", () => {
  for (const changed of [
    sdkPromotionWorkflow.replace("environment: nuget-org", "environment: release-draft"),
    sdkPromotionWorkflow.replace("id-token: write", "id-token: read"),
    sdkPromotionWorkflow.replace(
      "NuGet/login@8d196754b4036150537f80ac539e15c2f1028841",
      "NuGet/login@" + "0".repeat(40),
    ),
    sdkPromotionWorkflow.replace("needs: validate", "needs: []"),
    sdkPromotionWorkflow.replace(
      '--api-key "${NUGET_API_KEY}"',
      '--api-key "${NUGET_API_KEY}" --skip-duplicate',
    ),
    sdkPromotionWorkflow.replace("dotnet nuget verify --all", "dotnet nuget list source"),
    sdkPromotionWorkflow.replace("Signature type: Repository", "Signature type: Author"),
  ]) {
    assert.throws(() => checkStableSdkPromotionPolicy(changed), /stable SDK promotion/);
  }
});

test("stable SDK promotion rejects package rebuilds and GitHub release mutation", () => {
  for (const command of [
    "dotnet pack -c Release",
    "gh release upload v1.2.3 rebuilt.nupkg",
    "gh api --method PATCH repos/example/releases/1 -F draft=false",
    'cp rebuilt.nupkg "${RUNNER_TEMP}/nuget-release/Snaploom.Capture.${VERSION}.nupkg"',
  ]) {
    assert.throws(
      () =>
        checkStableSdkPromotionPolicy(
          sdkPromotionWorkflow.replace(
            "          dotnet nuget push \\",
            `          ${command}\n          dotnet nuget push \\`,
          ),
        ),
      /stable SDK promotion/,
    );
  }
});

test(".NET public consumers pin one SDK and never publish with an implicit restore", () => {
  assert.doesNotThrow(() =>
    checkDotnetConsumerRestorePolicy(dotnetPackageVerifier),
  );
  assert.throws(
    () =>
      checkDotnetConsumerRestorePolicy(
        dotnetPackageVerifier.replaceAll('"--no-restore",', ""),
      ),
    /locked-restore/,
  );
  assert.throws(
    () =>
      checkDotnetConsumerRestorePolicy(
        dotnetPackageVerifier.replace('rollForward: "disable"', 'rollForward: "latestMajor"'),
      ),
    /pin one SDK/,
  );
});

test("every Actions verifier call passes the SDK selected by setup-dotnet", () => {
  const workflows = [ciWorkflow, legalRcWorkflow, sdkPromotionWorkflow];
  assert.doesNotThrow(() => checkDotnetVerifierCallSites(workflows));
  assert.throws(
    () =>
      checkDotnetVerifierCallSites([
        ciWorkflow.replace(
          /\n\s*--sdk-version "\$\{\{ steps\.setup-dotnet\.outputs\.dotnet-version \}\}" \\/,
          "",
        ),
      ]),
    /must pass its selected SDK version/,
  );
});
