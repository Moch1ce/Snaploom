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
import {
  verifyPublishedTransition,
  verifyStablePublish as verifyStablePublishContract,
} from "./verify-stable-publish.mjs";

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
    kind: "snaploom-owner-release-approval",
    approvalIssue: 33,
    owner: {
      name: "Repository Owner",
      githubLogin: "Moch1ce",
    },
    approvalDate: "2026-07-21",
    riskAcknowledgement: "The repository owner accepts publishing without external legal review.",
    acknowledgedWithoutExternalLegalReview: true,
    acknowledgedWithoutRealMachineQualification: true,
    conclusion: "accepted",
    requiredChanges: [],
    approvedPublicRiskLanguage,
    decisionRecord: {
      sha256: "b".repeat(64),
      controlledLocation: "owner://decision/snaploom/1.2.3",
    },
    signature: "Repository Owner / 2026-07-21",
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
  const ownerApprovalComment = {
    id: 9001,
    issue_url: "https://api.github.com/repos/Moch1ce/Snaploom/issues/33",
    created_at: "2026-07-21T10:00:00Z",
    updated_at: "2026-07-21T10:00:00Z",
    author_association: "OWNER",
    user: { login: "Moch1ce", type: "User" },
    body: `<!-- snaploom-owner-release-approval:v1 -->\n\`\`\`json\n${JSON.stringify(review)}\n\`\`\``,
  };
  const issue = {
    number: 33,
    state: "closed",
    state_reason: "completed",
    closed_at: "2026-07-21T11:00:00Z",
    pull_request: undefined,
  };
  const ownerPermission = {
    permission: "admin",
    role_name: "admin",
    user: { login: "Moch1ce", type: "User" },
  };
  return { root, directory, release, ownerApprovalComment, ownerPermission, issue };
}

function verifyStablePublish({ ownerApprovalComment, ownerPermission, ...options }) {
  return verifyStablePublishContract({
    ...options,
    issueComments: [ownerApprovalComment],
    ownerApprovalCommentId: ownerApprovalComment.id,
    ownerPermission,
  });
}

test("rejects approval from anyone other than the repository owner", () => {
  const paths = fixture();
  try {
    const externalComment = {
      ...paths.ownerApprovalComment,
      author_association: "COLLABORATOR",
      user: { login: "external-counsel", type: "User" },
      body: paths.ownerApprovalComment.body.replaceAll("Moch1ce", "external-counsel"),
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          ownerPermission: {
            permission: "write",
            role_name: "write",
            user: { login: "external-counsel", type: "User" },
          },
          ownerApprovalComment: externalComment,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /repository owner with admin permission/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("accepts the repository owner's approval for the immutable draft subject", () => {
  const paths = fixture();
  try {
    const ownerComment = {
      ...paths.ownerApprovalComment,
      author_association: "OWNER",
      user: { login: "Moch1ce", type: "User" },
      body: paths.ownerApprovalComment.body.replaceAll("external-counsel", "Moch1ce"),
    };
    assert.doesNotThrow(() =>
      verifyStablePublish({
        release: paths.release,
        directory: paths.directory,
        version,
        commit,
        issue: paths.issue,
        ownerPermission: {
          permission: "admin",
          role_name: "admin",
          user: { login: "Moch1ce", type: "User" },
        },
        ownerApprovalComment: ownerComment,
        repositoryOwner: "MOCH1CE",
        skipArchiveBoundaries: true,
      }),
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("requires the owner to acknowledge publishing without external legal review", () => {
  const paths = fixture();
  try {
    const record = JSON.parse(paths.ownerApprovalComment.body.match(/```json\n([\s\S]+)\n```/)[1]);
    record.acknowledgedWithoutExternalLegalReview = false;
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            body: `<!-- snaploom-owner-release-approval:v1 -->\n\`\`\`json\n${JSON.stringify(record)}\n\`\`\``,
          },
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /acknowledge publishing without external legal review/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("requires the owner to acknowledge publishing without real-machine qualification", () => {
  const paths = fixture();
  try {
    const record = JSON.parse(
      paths.ownerApprovalComment.body.match(/```json\n([\s\S]+)\n```/)[1],
    );
    record.acknowledgedWithoutRealMachineQualification = false;
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            body: `<!-- snaploom-owner-release-approval:v1 -->\n\`\`\`json\n${JSON.stringify(record)}\n\`\`\``,
          },
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /acknowledge publishing without real-machine qualification/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects the legacy external legal-review marker", () => {
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
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            body: paths.ownerApprovalComment.body.replace(
              "snaploom-owner-release-approval:v1",
              "snaploom-legal-review:v1",
            ),
          },
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /exactly one owner approval record/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects duplicate or conflicting owner approval records across Issue 33 comments", () => {
  const paths = fixture();
  try {
    const duplicate = {
      ...paths.ownerApprovalComment,
      id: 9002,
      body: paths.ownerApprovalComment.body.replace('"conclusion":"accepted"', '"conclusion":"rejected"'),
    };
    assert.throws(
      () =>
        verifyStablePublishContract({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          issueComments: [paths.ownerApprovalComment, duplicate],
          ownerApprovalCommentId: paths.ownerApprovalComment.id,
          ownerPermission: paths.ownerPermission,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /exactly one owner approval record/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("requires the requested owner approval comment ID to match the unique record", () => {
  const paths = fixture();
  try {
    assert.throws(
      () =>
        verifyStablePublishContract({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          issueComments: [paths.ownerApprovalComment],
          ownerApprovalCommentId: 9002,
          ownerPermission: paths.ownerPermission,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /matching the requested comment ID/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects incomplete owner permission, association, and user identities", () => {
  const paths = fixture();
  try {
    const options = {
      release: paths.release,
      directory: paths.directory,
      version,
      commit,
      issue: paths.issue,
      ownerPermission: paths.ownerPermission,
      ownerApprovalComment: paths.ownerApprovalComment,
      repositoryOwner: "Moch1ce",
      skipArchiveBoundaries: true,
    };
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            user: { login: "Moch1ce", type: "User" },
            body: paths.ownerApprovalComment.body.replaceAll("external-counsel", "Moch1ce"),
          },
          ownerPermission: { permission: "admin", user: { login: "Moch1ce", type: "User" } },
        }),
      /repository owner with admin permission/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          ownerPermission: {
            ...paths.ownerPermission,
            role_name: "legal-reviewer-custom-role",
          },
        }),
      /repository owner with admin permission/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          ownerApprovalComment: { ...paths.ownerApprovalComment, author_association: "NONE" },
          ownerPermission: { ...paths.ownerPermission, permission: "none" },
        }),
      /repository owner with admin permission/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          ownerPermission: {
            ...paths.ownerPermission,
            permission: "write",
            role_name: "maintain",
          },
        }),
      /repository owner with admin permission/,
    );
    assert.throws(
      () =>
        verifyStablePublish({
          ...options,
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            user: { ...paths.ownerApprovalComment.user, type: "Bot" },
          },
          ownerPermission: {
            ...paths.ownerPermission,
            user: { ...paths.ownerPermission.user, type: "Bot" },
          },
        }),
      /repository owner with admin permission/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects an owner approval dated after its authenticated GitHub comment", () => {
  const paths = fixture();
  try {
    const ownerApprovalComment = {
      ...paths.ownerApprovalComment,
      body: paths.ownerApprovalComment.body.replace('"approvalDate":"2026-07-21"', '"approvalDate":"2026-07-22"'),
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /owner approval date cannot be later than its GitHub comment/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects ambiguous comments with duplicate owner-approval markers", () => {
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
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            body: `<!-- snaploom-owner-release-approval:v1 -->\n${paths.ownerApprovalComment.body}`,
          },
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /exactly one versioned record marker/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects owner-approval comments with prose outside the authenticated JSON record", () => {
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
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            body: `${paths.ownerApprovalComment.body}\nConflicting legal qualification.`,
          },
          ownerPermission: paths.ownerPermission,
          repositoryOwner: "Moch1ce",
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
    const record = JSON.parse(paths.ownerApprovalComment.body.match(/```json\n([\s\S]+)\n```/)[1]);
    record.release.payloadChecksums = Object.fromEntries(
      Object.entries(record.release.payloadChecksums).reverse(),
    );
    const ownerApprovalComment = {
      ...paths.ownerApprovalComment,
      body: `<!-- snaploom-owner-release-approval:v1 -->\n\`\`\`json\n${JSON.stringify(record)}\n\`\`\``,
    };
    assert.doesNotThrow(() =>
      verifyStablePublish({
        release: paths.release,
        directory: paths.directory,
        version,
        commit,
        issue: paths.issue,
        ownerPermission: paths.ownerPermission,
        ownerApprovalComment,
        repositoryOwner: "Moch1ce",
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

test("matches the GitHub repository owner login case-insensitively", () => {
  const paths = fixture();
  try {
    assert.doesNotThrow(() =>
      verifyStablePublish({
        release: paths.release,
        directory: paths.directory,
        version,
        commit,
        issue: paths.issue,
        ownerPermission: paths.ownerPermission,
        ownerApprovalComment: paths.ownerApprovalComment,
        repositoryOwner: "MOCH1CE",
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
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment: paths.ownerApprovalComment,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /closed as completed/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects an owner approval dated before the final draft subject", () => {
  const paths = fixture();
  try {
    const ownerApprovalComment = {
      ...paths.ownerApprovalComment,
      body: paths.ownerApprovalComment.body.replace('"approvalDate":"2026-07-21"', '"approvalDate":"2026-07-19"'),
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /owner approval date cannot precede the final draft subject/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects a draft whose public notes omit the owner-approved risk language", () => {
  const paths = fixture();
  try {
    const record = JSON.parse(paths.ownerApprovalComment.body.match(/```json\n([\s\S]+)\n```/)[1]);
    record.approvedPublicRiskLanguage = "Different language that is absent from the reviewed draft.";
    const ownerApprovalComment = {
      ...paths.ownerApprovalComment,
      body: `<!-- snaploom-owner-release-approval:v1 -->\n\`\`\`json\n${JSON.stringify(record)}\n\`\`\``,
    };
    assert.throws(
      () =>
        verifyStablePublish({
          release: paths.release,
          directory: paths.directory,
          version,
          commit,
          issue: paths.issue,
          ownerPermission: paths.ownerPermission,
          ownerApprovalComment,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /approved public risk language/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});

test("rejects an owner approval edited after Issue 33 was closed", () => {
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
          ownerApprovalComment: {
            ...paths.ownerApprovalComment,
            updated_at: "2026-07-21T12:00:00Z",
          },
          ownerPermission: paths.ownerPermission,
          repositoryOwner: "Moch1ce",
          skipArchiveBoundaries: true,
        }),
      /closure must occur after the final owner-approval edit/,
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
      ownerApprovalComment: paths.ownerApprovalComment,
      ownerPermission: paths.ownerPermission,
      repositoryOwner: "Moch1ce",
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
          ownerApprovalComment: { ...paths.ownerApprovalComment, updated_at: "not-a-date" },
        }),
      /valid GitHub timestamps/,
    );
  } finally {
    rmSync(paths.root, { recursive: true, force: true });
  }
});
