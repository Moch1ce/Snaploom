// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { isDeepStrictEqual } from "node:util";
import {
  MACOS_UNSIGNED_RISK_LANGUAGE,
  WINDOWS_UNSIGNED_RISK_LANGUAGE,
} from "./release-contract.mjs";
import { verifyDraftRelease } from "./verify-draft-release.mjs";

const reviewMarker = "<!-- snaploom-owner-release-approval:v1 -->";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function nonEmpty(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function validIsoDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? "")) return false;
  return new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
}

function validInstant(value) {
  return nonEmpty(value) && Number.isFinite(Date.parse(value));
}

export function extractOwnerReleaseApprovalRecord(body) {
  if (typeof body !== "string" || !body.includes(reviewMarker)) {
    throw new Error("owner approval comment is missing the versioned record marker");
  }
  if (body.split(reviewMarker).length !== 2) {
    throw new Error("owner approval comment must contain exactly one versioned record marker");
  }
  const match = body
    .trim()
    .match(/^<!-- snaploom-owner-release-approval:v1 -->\r?\n```json\r?\n([\s\S]*?)\r?\n```$/);
  if (!match) {
    throw new Error("owner approval comment must contain only the versioned marker and one JSON record");
  }
  try {
    return JSON.parse(match[1]);
  } catch {
    throw new Error("owner approval comment contains invalid JSON");
  }
}

export function verifyStablePublish({
  release,
  directory,
  version,
  commit,
  issue,
  issueComments,
  ownerApprovalCommentId,
  ownerPermission,
  repositoryOwner,
  skipArchiveBoundaries = false,
}) {
  const { manifest } = verifyDraftRelease({
    release,
    directory,
    version,
    commit,
    skipArchiveBoundaries,
  });
  if (!Array.isArray(issueComments)) {
    throw new Error("Issue #33 comments must be provided for owner approval uniqueness checks");
  }
  const ownerApprovalComments = issueComments.filter((comment) =>
    comment?.body?.includes(reviewMarker),
  );
  if (
    ownerApprovalComments.length !== 1 ||
    ownerApprovalComments[0]?.id !== ownerApprovalCommentId
  ) {
    throw new Error(
      "Issue #33 must contain exactly one owner approval record matching the requested comment ID",
    );
  }
  const ownerApprovalComment = ownerApprovalComments[0];
  const approval = extractOwnerReleaseApprovalRecord(ownerApprovalComment.body);
  const approvalLogin = ownerApprovalComment?.user?.login;
  const permissionLogin = ownerPermission?.user?.login;
  const ownerLogin = repositoryOwner?.toLowerCase();
  const isOwnerApproval =
    nonEmpty(ownerLogin) &&
    approvalLogin?.toLowerCase() === ownerLogin &&
    ownerApprovalComment?.author_association === "OWNER" &&
    ownerPermission?.permission === "admin" &&
    ownerPermission?.role_name === "admin";

  if (
    !validInstant(release?.updated_at) ||
    !validInstant(ownerApprovalComment?.created_at) ||
    !validInstant(ownerApprovalComment?.updated_at) ||
    !validInstant(issue?.closed_at)
  ) {
    throw new Error("release, owner-approval comment, and Issue #33 must have valid GitHub timestamps");
  }

  if (
    issue?.number !== 33 ||
    issue.state !== "closed" ||
    issue.state_reason !== "completed" ||
    issue.pull_request !== undefined ||
    !nonEmpty(issue.closed_at)
  ) {
    throw new Error("Issue #33 must be closed as completed after owner approval");
  }
  if (
    !Number.isInteger(ownerApprovalComment?.id) ||
    !ownerApprovalComment.issue_url?.endsWith("/issues/33") ||
    !nonEmpty(approvalLogin) ||
    ownerApprovalComment.user?.type !== "User" ||
    ownerPermission?.user?.type !== "User" ||
    permissionLogin?.toLowerCase() !== approvalLogin.toLowerCase() ||
    !isOwnerApproval
  ) {
    throw new Error("release approval must be authored by the repository owner with admin permission");
  }
  if (
    Date.parse(ownerApprovalComment.created_at) < Date.parse(release.updated_at) ||
    Date.parse(ownerApprovalComment.updated_at) < Date.parse(ownerApprovalComment.created_at)
  ) {
    throw new Error("the final owner-approval comment must occur after the final draft subject");
  }
  if (Date.parse(issue.closed_at) < Date.parse(ownerApprovalComment.updated_at)) {
    throw new Error("Issue #33 closure must occur after the final owner-approval edit");
  }
  if (approval?.acknowledgedWithoutExternalLegalReview !== true) {
    throw new Error("repository owner must acknowledge publishing without external legal review");
  }
  if (
    approval?.schemaVersion !== 1 ||
    approval.kind !== "snaploom-owner-release-approval" ||
    approval.approvalIssue !== 33 ||
    approval.owner?.githubLogin !== approvalLogin ||
    !nonEmpty(approval.owner?.name) ||
    !validIsoDate(approval.approvalDate) ||
    !nonEmpty(approval.riskAcknowledgement) ||
    approval.conclusion !== "accepted" ||
    !Array.isArray(approval.requiredChanges) ||
    approval.requiredChanges.length !== 0 ||
    !nonEmpty(approval.approvedPublicRiskLanguage) ||
    !/^[0-9a-f]{64}$/.test(approval.decisionRecord?.sha256 ?? "") ||
    !nonEmpty(approval.decisionRecord?.controlledLocation) ||
    !nonEmpty(approval.signature)
  ) {
    throw new Error("owner release approval record is incomplete or does not authorize publishing");
  }
  if (approval.approvalDate > ownerApprovalComment.updated_at.slice(0, 10)) {
    throw new Error("owner approval date cannot be later than its GitHub comment");
  }
  if (approval.approvalDate < release.updated_at.slice(0, 10)) {
    throw new Error("owner approval date cannot precede the final draft subject");
  }
  if (
    release.name !== `Snaploom ${version}` ||
    !nonEmpty(release.body) ||
    !release.body.includes(approval.approvedPublicRiskLanguage)
  ) {
    throw new Error("reviewed draft must contain the owner-approved public risk language");
  }
  if (!release.body.includes(WINDOWS_UNSIGNED_RISK_LANGUAGE)) {
    throw new Error("reviewed draft must contain the unsigned Windows risk disclosure");
  }
  if (!release.body.includes(MACOS_UNSIGNED_RISK_LANGUAGE)) {
    throw new Error("reviewed draft must contain the unsigned macOS risk disclosure");
  }

  const expectedRelease = {
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
  };
  if (!isDeepStrictEqual(approval.release, expectedRelease)) {
    throw new Error("owner release approval record does not exactly match the draft subject");
  }
  return { release, manifest, approval, approvalCommentId: ownerApprovalComment.id };
}

export function verifyPublishedTransition({ draftRelease, publishedRelease }) {
  if (
    draftRelease?.draft !== true ||
    publishedRelease?.draft !== false ||
    publishedRelease.prerelease !== false ||
    !nonEmpty(publishedRelease.published_at) ||
    publishedRelease.immutable !== true
  ) {
    throw new Error("stable transition did not produce an immutable published release");
  }
  const subject = (release) => ({
    id: release.id,
    tag_name: release.tag_name,
    target_commitish: release.target_commitish,
    name: release.name,
    body: release.body,
    created_at: release.created_at,
    assets: (release.assets ?? [])
      .map(({ id, name, size, digest, state }) => ({ id, name, size, digest, state }))
      .sort((left, right) => left.name.localeCompare(right.name)),
  });
  if (!isDeepStrictEqual(subject(draftRelease), subject(publishedRelease))) {
    throw new Error("stable transition changed the reviewed release subject");
  }
  return publishedRelease;
}

if (process.argv[1] && basename(process.argv[1]) === "verify-stable-publish.mjs") {
  const releasePath = argument("release-json");
  const publishedReleasePath = argument("published-release-json");
  const issuePath = argument("issue-json");
  const issueCommentsPath = argument("issue-comments-json");
  const ownerApprovalCommentId = Number(argument("owner-approval-comment-id"));
  const ownerPermissionPath = argument("owner-permission-json");
  const directory = resolve(argument("directory") ?? "release-download");
  const version = argument("version");
  const commit = argument("commit");
  const repositoryOwner = argument("repository-owner");
  if (
    !releasePath ||
    !issuePath ||
    !issueCommentsPath ||
    !Number.isInteger(ownerApprovalCommentId) ||
    !ownerPermissionPath ||
    !version ||
    !commit ||
    !repositoryOwner
  ) {
    throw new Error(
      "--release-json, --issue-json, --issue-comments-json, --owner-approval-comment-id, --owner-permission-json, --directory, --version, --commit, and --repository-owner are required",
    );
  }
  const result = verifyStablePublish({
    release: JSON.parse(readFileSync(resolve(releasePath), "utf8")),
    directory,
    version,
    commit,
    issue: JSON.parse(readFileSync(resolve(issuePath), "utf8")),
    issueComments: JSON.parse(readFileSync(resolve(issueCommentsPath), "utf8")),
    ownerApprovalCommentId,
    ownerPermission: JSON.parse(readFileSync(resolve(ownerPermissionPath), "utf8")),
    repositoryOwner,
  });
  if (publishedReleasePath) {
    verifyPublishedTransition({
      draftRelease: result.release,
      publishedRelease: JSON.parse(readFileSync(resolve(publishedReleasePath), "utf8")),
    });
  }
  process.stdout.write(
    `verified owner release approval by ${result.approval.owner.githubLogin} for release ${result.release.id}\n`,
  );
}
