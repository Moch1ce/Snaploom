// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  verifyLegalRcRunEvidence,
  verifyRunEvidence,
} from "./create-legal-review-bundle.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

const ciPath = argument("ci-run");
const qualificationPath = argument("qualification-run");
const legalRcPath = argument("legal-rc-run");
const commit = argument("commit");
if (!ciPath || !qualificationPath || !commit) {
  throw new Error("--ci-run, --qualification-run, and --commit are required");
}
verifyRunEvidence(
  JSON.parse(readFileSync(resolve(ciPath), "utf8")),
  JSON.parse(readFileSync(resolve(qualificationPath), "utf8")),
  commit,
);
if (legalRcPath) {
  verifyLegalRcRunEvidence(JSON.parse(readFileSync(resolve(legalRcPath), "utf8")));
}
process.stdout.write(`verified CI and true-machine runs for ${commit}\n`);
