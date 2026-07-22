// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import {
  checkStablePublishMutationPolicy,
  checkStablePublishVerifier,
} from "./check-workflows.mjs";

const workflowPath = fileURLToPath(
  new URL("../../.github/workflows/release-stable.yml", import.meta.url),
);
const workflow = readFileSync(workflowPath, "utf8");
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

test("stable publish mutation policy accepts the reviewed one-PATCH workflow", () => {
  assert.doesNotThrow(() => checkStablePublishMutationPolicy(workflow));
});

test("stable publish mutation policy accepts an equivalent CRLF checkout", () => {
  assert.doesNotThrow(() =>
    checkStablePublishMutationPolicy(workflow.replaceAll("\n", "\r\n")),
  );
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
          contents.replaceAll("\n", "\r\n"),
        ]),
      ),
    ),
  );
});
