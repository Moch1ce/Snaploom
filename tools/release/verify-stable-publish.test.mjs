// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { lstatSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { assembleRelease } from "./assemble-release.mjs";
import { assetsForVersion, expectedReleaseFiles } from "./release-contract.mjs";
import { verifyPublishedTransition, verifyStablePublish } from "./verify-stable-publish.mjs";

const version = "1.2.3";
const commit = "3".repeat(40);
const approvedPublicRiskLanguage =
  "The Apache SDK communicates with a separately distributed GPL Host; IPC does not automatically eliminate GPL compliance obligations.";
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
  },
};

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "snaploom-stable-publish-test-"));
  const payloads = join(root, "payloads");
  const directory = join(root, "release");
  mkdirSync(payloads);
  for (const { name } of assetsForVersion(version)) {
    writeFileSync(join(payloads, name), `payload:${name}\n`);
  }
  const manifest = assembleRelease({
    payloadDirectory: payloads,
    outputDirectory: directory,
    version,
    commit,
    mode: "stable-signed",
    signingEvidence,
  });
  const assets = expectedReleaseFiles(version).map((name, index) => {
    const path = join(directory, name);
    return {
      id: index + 1,
      name,
      size: lstatSync(path).size,
      digest: `sha256:${sha256(path)}`,
      state: "uploaded",
    };
  });
  const release = {
    id: 42,
    draft: true,
    prerelease: false,
    published_at: null,
    created_at: "2026-07-20T00:00:00Z",
    updated_at: "2026-07-20T01:00:00Z",
    tag_name: `v${version}`,
    target_commitish: commit,
    resolved_tag_commit: commit,
    name: `Snaploom ${version}`,
    body: `Snaploom ${version}\n\n${approvedPublicRiskLanguage}`,
    html_url: "https://github.com/Moch1ce/Snaploom/releases/tag/untagged-test",
    assets,
  };
  const review = {
    schemaVersion: 1,
    kind: "snaploom-external-legal-review",
    legalReviewIssue: 33,
    reviewer: {
      name: "External Counsel",
      organizationOrLicenseIdentity: "Example Bar 12345",
      githubLogin: "external-counsel",
    },
    reviewDate: "2026-07-21",
    jurisdictionAndLimitations: "Example jurisdiction; limited to the attached release subject.",
    conclusion: "accepted",
    requiredChanges: [],
    approvedPublicRiskLanguage,
    opinionDocument: {
      sha256: "b".repeat(64),
      controlledLocation: "counsel://matter/snaploom/1.2.3",
    },
    signature: "External Counsel / 2026-07-21",
    release: {
      id: release.id,
      url: release.html_url,
      draft: true,
      tag: manifest.tag,
      commit,
      version,
      manifestSha256: sha256(join(directory, "release-manifest.json")),
      sha256SumsSha256: sha256(join(directory, "SHA256SUMS")),
      payloadChecksums: Object.fromEntries(
        manifest.assets.map((asset) => [asset.name, asset.sha256]),
      ),
    },
  };
  const reviewComment = {
    id: 9001,
    issue_url: "https://api.github.com/repos/Moch1ce/Snaploom/issues/33",
    created_at: "2026-07-21T10:00:00Z",
    updated_at: "2026-07-21T10:00:00Z",
    author_association: "COLLABORATOR",
    user: { login: "external-counsel", type: "User" },
    body: `<!-- snaploom-legal-review:v1 -->\n\`\`\`json\n${JSON.stringify(review)}\n\`\`\``,
  };
  const issue = {
    number: 33,
    state: "closed",
    state_reason: "completed",
    closed_at: "2026-07-21T11:00:00Z",
    pull_request: undefined,
  };
  const reviewerPermission = {
    permission: "write",
    role_name: "write",
    user: { login: "external-counsel", type: "User" },
  };
  return { root, directory, release, reviewComment, reviewerPermission, issue };
}

test("accepts one external legal record that exactly matches the immutable draft subject", () => {
  const paths = fixture();
  try {
    const result = verifyStablePublish({
      release: paths.release,
      directory: paths.directory,
      version,
      commit,
      issue: paths.issue,
      reviewerPermission: paths.reviewerPermission,
      reviewComment: paths.reviewComment,
      repositoryOwner: "Moch1ce",
      allowedReviewerLogins: ["external-counsel"],
      skipArchiveBoundaries: true,
    });
    assert.equal(result.release.id, 42);
    assert.equal(result.review.reviewer.githubLogin, "external-counsel");
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects owner, non-allowlisted, non-collaborator, maintainer, and bot authors", () => {
  const paths = fixture();
  try {
    const options = {
      release: paths.release,
      directory: paths.directory,
      version,
      commit,
      issue: paths.issue,
      reviewerPermission: paths.reviewerPermission,
      reviewComment: paths.reviewComment,
      repositoryOwner: "Moch1ce",
      allowedReviewerLogins: ["external-counsel"],
      skipArchiveBoundaries: true,
    };
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          reviewComment: {
            ...paths.reviewComment,
            user: { login: "Moch1ce", type: "User" },
            body: paths.reviewComment.body.replaceAll("external-counsel", "Moch1ce"),
          },
          reviewerPermission: { permission: "admin", user: { login: "Moch1ce", type: "User" } },
          allowedReviewerLogins: ["Moch1ce"],
        }),
      /allowlisted external non-maintainer user/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          reviewerPermission: {
            ...paths.reviewerPermission,
            role_name: "legal-reviewer-custom-role",
          },
        }),
      /allowlisted external non-maintainer user/,
    );
    assert.throws(
      () => verifyStablePublish({ ...options, allowedReviewerLogins: [], reviewComment: paths.reviewComment }),
      /allowlisted external non-maintainer user/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          reviewComment: { ...paths.reviewComment, author_association: "NONE" },
          reviewerPermission: { ...paths.reviewerPermission, permission: "none" },
        }),
      /allowlisted external non-maintainer user/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          reviewerPermission: {
            ...paths.reviewerPermission,
            permission: "write",
            role_name: "maintain",
          },
        }),
      /allowlisted external non-maintainer user/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          reviewComment: {
            ...paths.reviewComment,
            user: { ...paths.reviewComment.user, type: "Bot" },
          },
          reviewerPermission: {
            ...paths.reviewerPermission,
            user: { ...paths.reviewerPermission.user, type: "Bot" },
          },
        }),
      /allowlisted external non-maintainer user/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects a legal record dated after its authenticated GitHub comment", () => {
  const paths = fixture();
  try {
    const reviewComment = {
      ...paths.reviewComment,
      body: paths.reviewComment.body.replace('"reviewDate":"2026-07-21"', '"reviewDate":"2026-07-22"'),
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          reviewerPermission: paths.reviewerPermission,
          reviewComment,
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /review date cannot be later than its GitHub comment/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects ambiguous comments with duplicate legal-review markers", () => {
  const paths = fixture();
  try {
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          reviewerPermission: paths.reviewerPermission,
          reviewComment: {
            ...paths.reviewComment,
            body: `<!-- snaploom-legal-review:v1 -->\n${paths.reviewComment.body}`,
          },
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /exactly one versioned record marker/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects legal-review comments with prose outside the authenticated JSON record", () => {
  const paths = fixture();
  try {
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          reviewComment: {
            ...paths.reviewComment,
            body: `${paths.reviewComment.body}\nConflicting legal qualification.`,
          },
          reviewerPermission: paths.reviewerPermission,
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /only the versioned marker and one JSON record/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("accepts a semantically identical checksum map regardless of JSON key order", () => {
  const paths = fixture();
  try {
    const record = JSON.parse(paths.reviewComment.body.match(/```json\n([\s\S]+)\n```/)[1]);
    record.release.payloadChecksums = Object.fromEntries(
      Object.entries(record.release.payloadChecksums).reverse(),
    );
    const reviewComment = {
      ...paths.reviewComment,
      body: `<!-- snaploom-legal-review:v1 -->\n\`\`\`json\n${JSON.stringify(record)}\n\`\`\``,
    };
    assert.doesNotThrow(() =>
      verifyStablePublish({
        release: paths.release,
        directory: paths.directory,
        version,
        commit,
        issue: paths.issue,
        reviewerPermission: paths.reviewerPermission,
        reviewComment,
        repositoryOwner: "Moch1ce",
        allowedReviewerLogins: ["external-counsel"],
        skipArchiveBoundaries: true,
      }),
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("accepts only an immutable publication that preserves the reviewed draft identity and assets", () => {
  const paths = fixture();
  try {
    const draft = {
      ...paths.release,
      assets: paths.release.assets.map((asset) => ({ ...asset, download_count: 0 })),
    };
    const published = {
      ...draft,
      draft: false,
      published_at: "2026-07-21T12:00:00Z",
      immutable: true,
      assets: draft.assets.map((asset) => ({ ...asset, download_count: 1 })),
    };
    assert.equal(
      verifyPublishedTransition({ draftRelease: draft, publishedRelease: published }).id,
      42,
    );
    assert.throws(
      () =>
        verifyPublishedTransition({
          draftRelease: draft,
          publishedRelease: { ...published, immutable: false },
        }),
      /immutable published release/,
    );
    assert.throws(
      () =>
        verifyPublishedTransition({
          draftRelease: draft,
          publishedRelease: { ...published, assets: published.assets.slice(1) },
        }),
      /changed the reviewed release subject/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("matches GitHub reviewer logins case-insensitively", () => {
  const paths = fixture();
  try {
    assert.doesNotThrow(() =>
      verifyStablePublish({
        release: paths.release,
        directory: paths.directory,
        version,
        commit,
        issue: paths.issue,
        reviewerPermission: paths.reviewerPermission,
        reviewComment: paths.reviewComment,
        repositoryOwner: "Moch1ce",
        allowedReviewerLogins: ["EXTERNAL-COUNSEL"],
        skipArchiveBoundaries: true,
      }),
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects Issue 33 when it was closed as not planned", () => {
  const paths = fixture();
  try {
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: { ...paths.issue, state_reason: "not_planned" },
          reviewerPermission: paths.reviewerPermission,
          reviewComment: paths.reviewComment,
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /closed as completed/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects a legal record dated before the final draft subject", () => {
  const paths = fixture();
  try {
    const reviewComment = {
      ...paths.reviewComment,
      body: paths.reviewComment.body.replace('"reviewDate":"2026-07-21"', '"reviewDate":"2026-07-19"'),
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          reviewerPermission: paths.reviewerPermission,
          reviewComment,
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /review date cannot precede the final draft subject/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects a draft whose public notes omit the lawyer-approved risk language", () => {
  const paths = fixture();
  try {
    const record = JSON.parse(paths.reviewComment.body.match(/```json\n([\s\S]+)\n```/)[1]);
    record.approvedPublicRiskLanguage = "Different language that is absent from the reviewed draft.";
    const reviewComment = {
      ...paths.reviewComment,
      body: `<!-- snaploom-legal-review:v1 -->\n\`\`\`json\n${JSON.stringify(record)}\n\`\`\``,
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          reviewerPermission: paths.reviewerPermission,
          reviewComment,
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /approved public risk language/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects a legal record edited after Issue 33 was closed", () => {
  const paths = fixture();
  try {
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          reviewComment: {
            ...paths.reviewComment,
            updated_at: "2026-07-21T12:00:00Z",
          },
          reviewerPermission: paths.reviewerPermission,
          repositoryOwner: "Moch1ce",
          allowedReviewerLogins: ["external-counsel"],
          skipArchiveBoundaries: true,
        }),
      /closure must occur after the final legal-review edit/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects malformed GitHub timestamps instead of bypassing chronology checks", () => {
  const paths = fixture();
  try {
    const options = {
      release: paths.release,
      directory: paths.directory,
      version,
      commit,
      issue: paths.issue,
      reviewComment: paths.reviewComment,
      reviewerPermission: paths.reviewerPermission,
      repositoryOwner: "Moch1ce",
      allowedReviewerLogins: ["external-counsel"],
      skipArchiveBoundaries: true,
    };
    assert.throws(
      () => verifyStablePublish({ ...options, issue: { ...paths.issue, closed_at: "not-a-date" } }),
      /valid GitHub timestamps/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          reviewComment: { ...paths.reviewComment, updated_at: "not-a-date" },
        }),
      /valid GitHub timestamps/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});
