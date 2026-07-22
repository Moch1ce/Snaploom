// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import {
  copyFileSync,
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { basename, join, resolve } from "node:path";
import { createSigningEvidence } from "./create-signing-evidence.mjs";
import { verifyDraftRelease } from "./verify-draft-release.mjs";
import { verifyQualificationSet } from "./verify-qualification-set.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function successfulJob(run, name) {
  return run.jobs?.some((job) => job.name === name && job.conclusion === "success");
}

export function verifyRunEvidence(ciRun, qualificationRun, commit) {
  const requiredCiJobs = [
    "Rust supply chain",
    "Windows x64",
    "macOS Apple Silicon",
    ".NET package",
    "Atomic release contract",
  ];
  const consumers =
    ciRun.jobs?.filter((job) => job.name.startsWith(".NET consumer (") && job.conclusion === "success") ?? [];
  if (
    ciRun.workflowName !== "CI" ||
    ciRun.conclusion !== "success" ||
    ciRun.headSha !== commit ||
    !requiredCiJobs.every((name) => successfulJob(ciRun, name)) ||
    consumers.length !== 4
  ) {
    throw new Error("CI run does not prove the complete release contract for this commit");
  }
  const machineJobs =
    qualificationRun.jobs?.filter(
      (job) => job.name.startsWith("True machine (") && job.conclusion === "success",
    ) ?? [];
  if (
    qualificationRun.workflowName !== "Release qualification" ||
    qualificationRun.conclusion !== "success" ||
    qualificationRun.headSha !== commit ||
    !successfulJob(qualificationRun, "Complete true-machine and PERF-01 gate") ||
    machineJobs.length !== 3
  ) {
    throw new Error("qualification run does not prove the complete true-machine gate");
  }
}

const requiredSignedConsumerJobs = [
  "Authenticode products and Windows SDK",
  "Developer ID and notarized macOS products",
  "Package signed dual-RID NuGet",
  ".NET signed consumer (windows-x64, net8.0)",
  ".NET signed consumer (windows-x64, net10.0)",
  ".NET signed consumer (macos-arm64, net8.0)",
  ".NET signed consumer (macos-arm64, net10.0)",
];
const requiredLegalRcGateJobs = [
  ...requiredSignedConsumerJobs,
  "Assemble exact stable-signed asset set",
  "Attest stable-signed payload provenance and SBOM",
];

export function verifyLegalRcRunEvidence(legalRcRun) {
  if (
    legalRcRun.workflowName !== "Signed legal RC draft" ||
    !Number.isInteger(legalRcRun.databaseId) ||
    !/^https:\/\//.test(legalRcRun.url ?? "") ||
    !requiredLegalRcGateJobs.every((name) => successfulJob(legalRcRun, name))
  ) {
    throw new Error("legal RC run does not prove the final signed release consumers");
  }
  return requiredSignedConsumerJobs;
}

export function createOwnerApprovalBundle({
  releaseDirectory,
  release,
  qualificationDirectory,
  ciRun,
  qualificationRun,
  legalRcRun,
  ciRunLogPath,
  qualificationRunLogPath,
  windowsMetadataPath,
  macosMetadataPath,
  platformEvidenceDirectory,
  outputDirectory,
  version,
  commit,
  skipArchiveBoundaries = false,
}) {
  if (existsSync(outputDirectory) && readdirSync(outputDirectory).length !== 0) {
    throw new Error("owner approval bundle output must be empty");
  }
  const { manifest } = verifyDraftRelease({
    release,
    directory: releaseDirectory,
    version,
    commit,
    skipArchiveBoundaries,
  });
  verifyRunEvidence(ciRun, qualificationRun, commit);
  const signedConsumerJobs = verifyLegalRcRunEvidence(legalRcRun);
  const qualification = verifyQualificationSet(qualificationDirectory, { version, commit });
  const ciRunLog = readFileSync(ciRunLogPath, "utf8");
  const qualificationRunLog = readFileSync(qualificationRunLogPath, "utf8");
  if (
    !["reuse lint", "cargo deny", "verify-cyclonedx.mjs"].every((text) =>
      ciRunLog.includes(text),
    ) ||
    !qualificationRunLog.includes("PERF-01") ||
    !qualificationRunLog.includes("True machine")
  ) {
    throw new Error("CI or true-machine raw logs do not contain the required compliance evidence");
  }
  const signing = createSigningEvidence(
    JSON.parse(readFileSync(windowsMetadataPath, "utf8")),
    JSON.parse(readFileSync(macosMetadataPath, "utf8")),
  );
  if (JSON.stringify(signing) !== JSON.stringify(manifest.signing)) {
    throw new Error("platform signing metadata does not match the immutable release manifest");
  }
  const windowsBuilder = JSON.parse(
    readFileSync(join(platformEvidenceDirectory, "windows-builder.json"), "utf8"),
  );
  const macosBuilder = JSON.parse(
    readFileSync(join(platformEvidenceDirectory, "macos-builder.json"), "utf8"),
  );
  const dotnetBuilder = JSON.parse(
    readFileSync(join(platformEvidenceDirectory, "dotnet-builder.json"), "utf8"),
  );
  for (const [name, builder] of [
    ["Windows", windowsBuilder],
    ["macOS", macosBuilder],
  ]) {
    const requiredBuilderFields = [
      "imageOS",
      "imageVersion",
      "runnerArchitecture",
      "node",
      "pnpm",
      "python",
      "rustc",
      "cargo",
      ...(name === "Windows" ? ["windowsSdk", "signTool"] : ["xcode", "swift"]),
    ];
    if (
      !requiredBuilderFields.every(
        (field) => typeof builder[field] === "string" && builder[field].length > 0,
      )
    ) {
      throw new Error(`${name} builder identity is incomplete`);
    }
  }
  if (
    windowsBuilder.windowsSdk !== "10.0.26100.0" ||
    !windowsBuilder.python.includes("3.13.14") ||
    !macosBuilder.python.includes("3.13.14") ||
    !macosBuilder.xcode.includes("Xcode 16.4") ||
    !macosBuilder.xcode.includes("16F6")
  ) {
    throw new Error("platform builder toolchains do not match the pinned release contract");
  }
  if (
    !["imageOS", "imageVersion", "runnerArchitecture", "node", "dotnet"].every(
      (field) =>
        typeof dotnetBuilder[field] === "string" && dotnetBuilder[field].length > 0,
    ) ||
    dotnetBuilder.dotnet !== "8.0.423"
  ) {
    throw new Error("NuGet builder identity is incomplete or not pinned");
  }
  const appleEvidence = join(platformEvidenceDirectory, "apple-signing-evidence");
  const hostNotary = JSON.parse(readFileSync(join(appleEvidence, "host-notary.json"), "utf8"));
  const dmgNotary = JSON.parse(readFileSync(join(appleEvidence, "dmg-notary.json"), "utf8"));
  const appleVerification = readFileSync(join(appleEvidence, "apple-verification.log"), "utf8");
  if (
    hostNotary.status !== "Accepted" ||
    hostNotary.id !== manifest.signing.macos.hostNotarySubmissionId ||
    dmgNotary.status !== "Accepted" ||
    dmgNotary.id !== manifest.signing.macos.dmgNotarySubmissionId ||
    appleVerification.trim().length === 0
  ) {
    throw new Error("Apple notarization results or verification log do not match the manifest");
  }
  const consumerLogDirectory = join(platformEvidenceDirectory, "consumer-logs");
  const expectedConsumerLogs = [
    "dotnet-macos-arm64-net10.0-consumer.log",
    "dotnet-macos-arm64-net8.0-consumer.log",
    "dotnet-windows-x64-net10.0-consumer.log",
    "dotnet-windows-x64-net8.0-consumer.log",
    "macos-c-cpp-consumer.log",
    "macos-swift-consumer.log",
    "windows-c-cpp-consumer.log",
  ];
  if (
    JSON.stringify(readdirSync(consumerLogDirectory).sort()) !==
      JSON.stringify(expectedConsumerLogs) ||
    expectedConsumerLogs.some(
      (name) => readFileSync(join(consumerLogDirectory, name), "utf8").trim().length === 0,
    ) ||
    [
      "windows-signing.log",
      "macos-signing.log",
      "windows-binary-inspection.log",
      "macos-binary-inspection.log",
      "nuget-package-listing.log",
    ].some(
      (name) => readFileSync(join(platformEvidenceDirectory, name), "utf8").trim().length === 0,
    )
  ) {
    throw new Error("final signing or C/C++/C#/Swift consumer logs are incomplete");
  }

  mkdirSync(outputDirectory, { recursive: true });
  const manifestPath = join(releaseDirectory, "release-manifest.json");
  const sumsPath = join(releaseDirectory, "SHA256SUMS");
  copyFileSync(manifestPath, join(outputDirectory, "release-manifest.json"));
  copyFileSync(sumsPath, join(outputDirectory, "SHA256SUMS"));
  copyFileSync(windowsMetadataPath, join(outputDirectory, "windows-package-metadata.json"));
  copyFileSync(macosMetadataPath, join(outputDirectory, "macos-package-metadata.json"));
  cpSync(platformEvidenceDirectory, join(outputDirectory, "platform-evidence"), {
    recursive: true,
  });
  cpSync(qualificationDirectory, join(outputDirectory, "qualification"), { recursive: true });
  writeFileSync(join(outputDirectory, "ci-run.json"), `${JSON.stringify(ciRun, null, 2)}\n`);
  writeFileSync(
    join(outputDirectory, "qualification-run.json"),
    `${JSON.stringify(qualificationRun, null, 2)}\n`,
  );
  writeFileSync(
    join(outputDirectory, "legal-rc-run.json"),
    `${JSON.stringify(legalRcRun, null, 2)}\n`,
  );
  copyFileSync(ciRunLogPath, join(outputDirectory, "ci-run.log"));
  copyFileSync(qualificationRunLogPath, join(outputDirectory, "qualification-run.log"));

  const subject = {
    schemaVersion: 1,
    approvalIssue: 33,
    legalStatus: "owner-risk-acceptance-required",
    stablePublishAllowed: false,
    release: {
      id: release.id,
      url: release.html_url,
      draft: true,
      tag: manifest.tag,
      commit,
      version,
      manifestSha256: sha256(manifestPath),
      sha256SumsSha256: sha256(sumsPath),
      payloadChecksums: Object.fromEntries(
        manifest.assets.map((asset) => [asset.name, asset.sha256]),
      ),
    },
    signing: manifest.signing,
    evidence: {
      ciRun: { id: ciRun.databaseId, url: ciRun.url },
      qualificationRun: { id: qualificationRun.databaseId, url: qualificationRun.url },
      legalRcRun: { id: legalRcRun.databaseId, url: legalRcRun.url },
      qualificationPlatforms: qualification.map(({ platform }) => platform),
      signedConsumerJobs,
    },
    requiredHumanFields: [
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
    ],
  };
  writeFileSync(
    join(outputDirectory, "review-subject.json"),
    `${JSON.stringify(subject, null, 2)}\n`,
    "utf8",
  );
  writeFileSync(
    join(outputDirectory, "README.md"),
    [
      `# Snaploom ${version} 所有者发布审批包`,
      "",
      `本复核包精确对应未公开 draft：${release.html_url}`,
      `tag/commit：${manifest.tag} / ${commit}`,
      "",
      "该包不是法律意见。仓库所有者必须核对 review-subject.json、全部 payload checksum、签名/公证记录、真机资格证据和实际 draft，并明确接受未经过外部法律复核即发布的风险。",
      "platform-evidence/ 保存 builder identity、原始 notarization JSON、签名/二进制检查日志及最终 C/C++/C#/Swift consumer 输出；legal-rc-run.json 指向完整 Actions 日志。",
      "在所有者审批记录完整且精确覆盖本 draft 前，stable publish 保持禁止；不得重建、替换或覆盖已审批资产。",
      "",
    ].join("\n"),
    "utf8",
  );
  return subject;
}

if (process.argv[1] && basename(process.argv[1]) === "create-owner-approval-bundle.mjs") {
  const required = [
    "release-directory",
    "release-json",
    "qualification-directory",
    "ci-run-json",
    "qualification-run-json",
    "legal-rc-run-json",
    "ci-run-log",
    "qualification-run-log",
    "windows-metadata",
    "macos-metadata",
    "platform-evidence-directory",
    "output",
    "version",
    "commit",
  ];
  const args = Object.fromEntries(required.map((name) => [name, argument(name)]));
  if (required.some((name) => !args[name])) {
    throw new Error(`required arguments: ${required.map((name) => `--${name}`).join(", ")}`);
  }
  const subject = createOwnerApprovalBundle({
    releaseDirectory: resolve(args["release-directory"]),
    release: JSON.parse(readFileSync(resolve(args["release-json"]), "utf8")),
    qualificationDirectory: resolve(args["qualification-directory"]),
    ciRun: JSON.parse(readFileSync(resolve(args["ci-run-json"]), "utf8")),
    qualificationRun: JSON.parse(
      readFileSync(resolve(args["qualification-run-json"]), "utf8"),
    ),
    legalRcRun: JSON.parse(readFileSync(resolve(args["legal-rc-run-json"]), "utf8")),
    ciRunLogPath: resolve(args["ci-run-log"]),
    qualificationRunLogPath: resolve(args["qualification-run-log"]),
    windowsMetadataPath: resolve(args["windows-metadata"]),
    macosMetadataPath: resolve(args["macos-metadata"]),
    platformEvidenceDirectory: resolve(args["platform-evidence-directory"]),
    outputDirectory: resolve(args.output),
    version: args.version,
    commit: args.commit,
  });
  process.stdout.write(
    `created owner release approval bundle for ${subject.release.tag} at ${subject.release.commit}\n`,
  );
}
