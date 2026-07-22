// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { isDeepStrictEqual } from "node:util";
import { verifyDraftRelease } from "./verify-draft-release.mjs";

const reviewMarker = "<!-- snaploom-legal-review:v1 -->";

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

export function extractLegalReviewRecord(body) {
  if (typeof body !== "string" || !body.includes(reviewMarker)) {
    throw new Error("legal review comment is missing the versioned record marker");
  }
  if (body.split(reviewMarker).length !== 2) {
    throw new Error("legal review comment must contain exactly one versioned record marker");
  }
  const match = body
    .trim()
    .match(/^<!-- snaploom-legal-review:v1 -->\r?\n```json\r?\n([\s\S]*?)\r?\n```$/);
  if (!match) {
    throw new Error("legal review comment must contain only the versioned marker and one JSON record");
  }
  try {
    return JSON.parse(match[1]);
  } catch {
    throw new Error("legal review comment contains invalid JSON");
  }
}

export function verifyStablePublish({
  release,
  directory,
  version,
  commit,
  issue,
  reviewComment,
  reviewerPermission,
  repositoryOwner,
  allowedReviewerLogins,
  skipArchiveBoundaries = false,
}) {
  const { manifest } = verifyDraftRelease({
    release,
    directory,
    version,
    commit,
    skipArchiveBoundaries,
  });
  const review = extractLegalReviewRecord(reviewComment?.body);
  const reviewerLogin = reviewComment?.user?.login;
  const permissionLogin = reviewerPermission?.user?.login;
  const allowed = new Set(
    (allowedReviewerLogins ?? []).map((login) => login.toLowerCase()),
  );

  if (
    !validInstant(release?.updated_at) ||
    !validInstant(reviewComment?.created_at) ||
    !validInstant(reviewComment?.updated_at) ||
    !validInstant(issue?.closed_at)
  ) {
    throw new Error("release, legal-review comment, and Issue #33 must have valid GitHub timestamps");
  }

  if (
    issue?.number !== 33 ||
    issue.state !== "closed" ||
    issue.state_reason !== "completed" ||
    issue.pull_request !== undefined ||
    !nonEmpty(issue.closed_at)
  ) {
    throw new Error("Issue #33 must be closed as completed after legal review");
  }
  if (
    !Number.isInteger(reviewComment?.id) ||
    !reviewComment.issue_url?.endsWith("/issues/33") ||
    !nonEmpty(reviewerLogin) ||
    reviewComment.user?.type !== "User" ||
    reviewerLogin.toLowerCase() === repositoryOwner?.toLowerCase() ||
    !allowed.has(reviewerLogin.toLowerCase()) ||
    reviewComment.author_association !== "COLLABORATOR" ||
    reviewerPermission?.user?.type !== "User" ||
    permissionLogin?.toLowerCase() !== reviewerLogin.toLowerCase() ||
    reviewerPermission?.permission !== "write" ||
    reviewerPermission?.role_name !== "write"
  ) {
    throw new Error("legal review must be authored by an allowlisted external non-maintainer user");
  }
  if (
    Date.parse(reviewComment.created_at) < Date.parse(release.updated_at) ||
    Date.parse(reviewComment.updated_at) < Date.parse(reviewComment.created_at)
  ) {
    throw new Error("the final legal-review comment must occur after the final draft subject");
  }
  if (Date.parse(issue.closed_at) < Date.parse(reviewComment.updated_at)) {
    throw new Error("Issue #33 closure must occur after the final legal-review edit");
  }
  if (
    review?.schemaVersion !== 1 ||
    review.kind !== "snaploom-external-legal-review" ||
    review.legalReviewIssue !== 33 ||
    review.reviewer?.githubLogin !== reviewerLogin ||
    !nonEmpty(review.reviewer?.name) ||
    !nonEmpty(review.reviewer?.organizationOrLicenseIdentity) ||
    !validIsoDate(review.reviewDate) ||
    !nonEmpty(review.jurisdictionAndLimitations) ||
    review.conclusion !== "accepted" ||
    !Array.isArray(review.requiredChanges) ||
    review.requiredChanges.length !== 0 ||
    !nonEmpty(review.approvedPublicRiskLanguage) ||
    !/^[0-9a-f]{64}$/.test(review.opinionDocument?.sha256 ?? "") ||
    !nonEmpty(review.opinionDocument?.controlledLocation) ||
    !nonEmpty(review.signature)
  ) {
    throw new Error("external legal review record is incomplete or does not authorize publishing");
  }
  if (review.reviewDate > reviewComment.updated_at.slice(0, 10)) {
    throw new Error("legal review date cannot be later than its GitHub comment");
  }
  if (review.reviewDate < release.updated_at.slice(0, 10)) {
    throw new Error("legal review date cannot precede the final draft subject");
  }
  if (
    release.name !== `Snaploom ${version}` ||
    !nonEmpty(release.body) ||
    !release.body.includes(review.approvedPublicRiskLanguage)
  ) {
    throw new Error("reviewed draft must contain the lawyer-approved public risk language");
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
  if (!isDeepStrictEqual(review.release, expectedRelease)) {
    throw new Error("external legal review record does not exactly match the draft subject");
  }
  return { release, manifest, review, reviewCommentId: reviewComment.id };
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
  const reviewCommentPath = argument("review-comment-json");
  const reviewerPermissionPath = argument("reviewer-permission-json");
  const directory = resolve(argument("directory") ?? "release-download");
  const version = argument("version");
  const commit = argument("commit");
  const repositoryOwner = argument("repository-owner");
  const allowedReviewerLogins = (argument("allowed-reviewers") ?? "")
    .split(",")
    .map((login) => login.trim())
    .filter(Boolean);
  if (
    !releasePath ||
    !issuePath ||
    !reviewCommentPath ||
    !reviewerPermissionPath ||
    !version ||
    !commit ||
    !repositoryOwner ||
    allowedReviewerLogins.length === 0
  ) {
    throw new Error(
      "--release-json, --issue-json, --review-comment-json, --reviewer-permission-json, --directory, --version, --commit, --repository-owner, and --allowed-reviewers are required",
    );
  }
  const result = verifyStablePublish({
    release: JSON.parse(readFileSync(resolve(releasePath), "utf8")),
    directory,
    version,
    commit,
    issue: JSON.parse(readFileSync(resolve(issuePath), "utf8")),
    reviewComment: JSON.parse(readFileSync(resolve(reviewCommentPath), "utf8")),
    reviewerPermission: JSON.parse(readFileSync(resolve(reviewerPermissionPath), "utf8")),
    repositoryOwner,
    allowedReviewerLogins,
  });
  if (publishedReleasePath) {
    verifyPublishedTransition({
      draftRelease: result.release,
      publishedRelease: JSON.parse(readFileSync(resolve(publishedReleasePath), "utf8")),
    });
  }
  process.stdout.write(
    `verified external legal approval by ${result.review.reviewer.githubLogin} for release ${result.release.id}\n`,
  );
}
