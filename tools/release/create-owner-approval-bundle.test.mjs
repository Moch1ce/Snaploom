// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  lstatSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { assembleRelease } from "./assemble-release.mjs";
import {
  createOwnerApprovalBundle,
  verifyLegalRcRunEvidence,
  verifyRunEvidence,
} from "./create-owner-approval-bundle.mjs";
import { assetsForVersion, expectedReleaseFiles } from "./release-contract.mjs";

const version = "1.2.3";
const commit = "3".repeat(40);

function runEvidence() {
  const required = [
    "Rust supply chain",
    "Windows x64",
    "macOS Apple Silicon",
    ".NET package",
    "Atomic release contract",
    ".NET consumer (windows-x64, net8.0)",
    ".NET consumer (windows-x64, net10.0)",
    ".NET consumer (macos-arm64, net8.0)",
    ".NET consumer (macos-arm64, net10.0)",
  ];
  const ciRun = {
    workflowName: "CI",
    conclusion: "success",
    headSha: commit,
    databaseId: 11,
    url: "https://example.test/ci/11",
    jobs: required.map((name) => ({ name, conclusion: "success" })),
  };
  const qualificationRun = {
    workflowName: "Release qualification",
    conclusion: "success",
    headSha: commit,
    databaseId: 12,
    url: "https://example.test/qualification/12",
    jobs: [
      "True machine (windows-10-x64)",
      "True machine (windows-11-x64)",
      "True machine (macos-14-arm64)",
      "Complete true-machine and PERF-01 gate",
    ].map((name) => ({ name, conclusion: "success" })),
  };
  const legalRcRun = {
    workflowName: "Signed legal RC draft",
    databaseId: 13,
    url: "https://example.test/legal-rc/13",
    jobs: [
      "Authenticode products and Windows SDK",
      "Developer ID and notarized macOS products",
      "Package signed dual-RID NuGet",
      ".NET signed consumer (windows-x64, net8.0)",
      ".NET signed consumer (windows-x64, net10.0)",
      ".NET signed consumer (macos-arm64, net8.0)",
      ".NET signed consumer (macos-arm64, net10.0)",
      "Assemble exact stable-signed asset set",
      "Attest stable-signed payload provenance and SBOM",
    ].map((name) => ({ name, conclusion: "success" })),
  };
  return { ciRun, qualificationRun, legalRcRun };
}

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "snaploom-legal-bundle-test-"));
  const payloads = join(root, "payloads");
  const releaseDirectory = join(root, "release");
  const qualificationDirectory = join(root, "qualification");
  const outputDirectory = join(root, "review");
  mkdirSync(payloads);
  mkdirSync(qualificationDirectory);
  for (const { name } of assetsForVersion(version)) {
    writeFileSync(join(payloads, name), `payload:${name}\n`);
  }
  const signingEvidence = {
    windows: {
      mode: "authenticode",
      authenticodeVerified: true,
      rfc3161TimestampVerified: true,
      certificateThumbprint: "a".repeat(40),
      additionalSignedBinaries: [{ name: "snaploom_capture.dll", sha256: "1".repeat(64) }],
    },
    macos: {
      mode: "developer-id",
      developerIdVerified: true,
      notarizationStatus: "Accepted",
      stapled: true,
      gatekeeperVerified: true,
      teamId: "ABCDE12345",
      notarySubmissionId: "dmg-submission",
      hostNotarySubmissionId: "host-submission",
      dmgNotarySubmissionId: "dmg-submission",
    },
  };
  assembleRelease({
    payloadDirectory: payloads,
    outputDirectory: releaseDirectory,
    version,
    commit,
    mode: "stable-signed",
    signingEvidence,
  });
  const assets = expectedReleaseFiles(version).map((name, index) => {
    const path = join(releaseDirectory, name);
    return {
      id: index + 1,
      name,
      size: lstatSync(path).size,
      digest: `sha256:${createHash("sha256").update(readFileSync(path)).digest("hex")}`,
      state: "uploaded",
    };
  });
  const release = {
    id: 42,
    draft: true,
    prerelease: false,
    published_at: null,
    tag_name: `v${version}`,
    target_commitish: commit,
    resolved_tag_commit: commit,
    html_url: "https://example.test/draft/42",
    assets,
  };
  for (const platform of ["macos-14-arm64", "windows-10-x64", "windows-11-x64"]) {
    writeFileSync(
      join(qualificationDirectory, `${platform}.json`),
      `${JSON.stringify({
        platform,
        version,
        commit,
        passed: true,
        contracts: ["PERF-01", "QA-03"],
      })}\n`,
    );
  }
  const windowsMetadataPath = join(root, "windows.json");
  const macosMetadataPath = join(root, "macos.json");
  const ciRunLogPath = join(root, "ci-run.log");
  const qualificationRunLogPath = join(root, "qualification-run.log");
  writeFileSync(ciRunLogPath, "reuse lint\ncargo deny\nverify-cyclonedx.mjs\n");
  writeFileSync(qualificationRunLogPath, "True machine\nPERF-01\n");
  const platformEvidenceDirectory = join(root, "platform");
  mkdirSync(join(platformEvidenceDirectory, "apple-signing-evidence"), { recursive: true });
  writeFileSync(
    windowsMetadataPath,
    `${JSON.stringify({
      signingMode: "stable-signed",
      authenticodeVerified: true,
      rfc3161TimestampVerified: true,
      certificateThumbprint: "a".repeat(40),
      additionalSignedBinaries: [{ name: "snaploom_capture.dll", sha256: "1".repeat(64) }],
    })}\n`,
  );
  writeFileSync(
    macosMetadataPath,
    `${JSON.stringify({
      signingMode: "developer-id",
      notarized: true,
      teamId: "ABCDE12345",
      hostNotarySubmissionId: "host-submission",
      dmgNotarySubmissionId: "dmg-submission",
    })}\n`,
  );
  writeFileSync(
    join(platformEvidenceDirectory, "windows-builder.json"),
    '{"imageOS":"win25","imageVersion":"1","runnerArchitecture":"X64","node":"v22","pnpm":"10","python":"3.13.14","rustc":"1.97","cargo":"1.97","windowsSdk":"10.0.26100.0","signTool":"10.0.26100"}\n',
  );
  writeFileSync(
    join(platformEvidenceDirectory, "macos-builder.json"),
    '{"imageOS":"macos15","imageVersion":"1","runnerArchitecture":"ARM64","node":"v22","pnpm":"10","python":"3.13.14","rustc":"1.97","cargo":"1.97","xcode":"Xcode 16.4 Build version 16F6","swift":"Swift 6"}\n',
  );
  writeFileSync(
    join(platformEvidenceDirectory, "dotnet-builder.json"),
    '{"imageOS":"ubuntu24","imageVersion":"1","runnerArchitecture":"X64","node":"v22","dotnet":"8.0.423"}\n',
  );
  writeFileSync(
    join(platformEvidenceDirectory, "apple-signing-evidence", "host-notary.json"),
    '{"id":"host-submission","status":"Accepted"}\n',
  );
  writeFileSync(
    join(platformEvidenceDirectory, "apple-signing-evidence", "dmg-notary.json"),
    '{"id":"dmg-submission","status":"Accepted"}\n',
  );
  writeFileSync(
    join(platformEvidenceDirectory, "apple-signing-evidence", "apple-verification.log"),
    "codesign, stapler, and Gatekeeper passed\n",
  );
  mkdirSync(join(platformEvidenceDirectory, "consumer-logs"));
  for (const name of [
    "windows-c-cpp-consumer.log",
    "macos-c-cpp-consumer.log",
    "macos-swift-consumer.log",
    "dotnet-windows-x64-net8.0-consumer.log",
    "dotnet-windows-x64-net10.0-consumer.log",
    "dotnet-macos-arm64-net8.0-consumer.log",
    "dotnet-macos-arm64-net10.0-consumer.log",
  ]) {
    writeFileSync(join(platformEvidenceDirectory, "consumer-logs", name), "passed\n");
  }
  writeFileSync(join(platformEvidenceDirectory, "windows-signing.log"), "signtool passed\n");
  writeFileSync(join(platformEvidenceDirectory, "macos-signing.log"), "codesign passed\n");
  writeFileSync(
    join(platformEvidenceDirectory, "windows-binary-inspection.log"),
    "dumpbin exports and dependencies\n",
  );
  writeFileSync(
    join(platformEvidenceDirectory, "macos-binary-inspection.log"),
    "nm and otool output\n",
  );
  writeFileSync(
    join(platformEvidenceDirectory, "nuget-package-listing.log"),
    "unzip package listing\n",
  );
  return {
    root,
    releaseDirectory,
    qualificationDirectory,
    outputDirectory,
    windowsMetadataPath,
    macosMetadataPath,
    ciRunLogPath,
    qualificationRunLogPath,
    platformEvidenceDirectory,
    release,
  };
}

test("creates an immutable owner approval subject without granting stable publish", () => {
  const paths = fixture();
  const { ciRun, qualificationRun, legalRcRun } = runEvidence();
  try {
    const subject = createOwnerApprovalBundle({
      ...paths,
      ciRun,
      qualificationRun,
      legalRcRun,
      version,
      commit,
      skipArchiveBoundaries: true,
    });
    assert.equal(subject.stablePublishAllowed, false);
    assert.equal(subject.approvalIssue, 33);
    assert.equal(subject.legalStatus, "owner-risk-acceptance-required");
    assert.deepEqual(subject.requiredHumanFields, [
      "ownerName",
      "ownerGithubLogin",
      "approvalDate",
      "riskAcknowledgement",
      "acknowledgedWithoutExternalLegalReview",
      "conclusion",
      "requiredChanges",
      "approvedPublicRiskLanguage",
      "decisionRecordSha256AndControlledLocation",
      "signature",
    ]);
    assert.equal(subject.evidence.qualificationPlatforms.length, 3);
    assert.equal(subject.evidence.signedConsumerJobs.length, 7);
    const readme = readFileSync(join(paths.outputDirectory, "README.md"), "utf8");
    assert.match(readme, /仓库所有者/);
    assert.match(readme, /未经过外部法律复核/);
    assert.match(readme, /stable publish/);
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects incomplete CI, qualification, or signed consumer evidence", () => {
  const { ciRun, qualificationRun, legalRcRun } = runEvidence();
  assert.throws(
    () => verifyRunEvidence({ ...ciRun, conclusion: "failure" }, qualificationRun, commit),
    /complete release contract/,
  );
  assert.throws(
    () => verifyRunEvidence(ciRun, { ...qualificationRun, jobs: [] }, commit),
    /complete true-machine/,
  );
  assert.throws(
    () => verifyLegalRcRunEvidence({ ...legalRcRun, jobs: [] }),
    /signed release consumers/,
  );
  assert.throws(
    () => verifyRunEvidence(ciRun, { ...qualificationRun, headSha: "4".repeat(40) }, commit),
    /complete true-machine/,
  );
});
