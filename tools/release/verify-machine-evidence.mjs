// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, writeFileSync } from "node:fs";
import { basename, resolve } from "node:path";
import { evaluateEvidence } from "../performance/evaluate-evidence.mjs";

const COMMON_CHECKS = [
  "capture-copy",
  "capture-save",
  "shortcut-conflict",
  "repeated-launch",
  "cancel-save",
  "save-failure-recovery",
  "clipboard-failure-recovery",
  "single-display-1080p",
  "dual-display",
  "display-hotplug",
  "sleep-wake",
  "install-ordinary-user",
  "overwrite-upgrade",
  "uninstall-preserves-user-data",
];

const PLATFORM_CHECKS = {
  "windows-10-x64": ["mixed-dpi-100-200", "protected-desktop-not-captured", "no-elevation"],
  "windows-11-x64": ["mixed-dpi-100-200", "protected-desktop-not-captured", "no-elevation"],
  "macos-14-arm64": ["retina-5k", "mixed-dpi-retina", "permission-denied-recovery", "gatekeeper"],
};

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

export function verifyMachineEvidence(evidence, { platform, version, commit }) {
  if (
    evidence.schemaVersion !== 1 ||
    evidence.realMachine !== true ||
    evidence.interactiveDesktopSession !== true ||
    evidence.ordinaryUser !== true ||
    evidence.platform !== platform ||
    evidence.version !== version ||
    evidence.commit !== commit
  ) {
    throw new Error("qualification evidence identity or true-machine claims are incomplete");
  }
  const requiredChecks = [...COMMON_CHECKS, ...(PLATFORM_CHECKS[platform] ?? [])];
  if (!PLATFORM_CHECKS[platform]) throw new Error(`unsupported qualification platform: ${platform}`);
  const missing = requiredChecks.filter((name) => evidence.checks?.[name] !== true);
  if (missing.length) throw new Error(`true-machine qualification is missing: ${missing.join(", ")}`);
  const performance = evaluateEvidence(
    { ...evidence.performance, platform, version, commit },
    { platform, version, commit },
  );
  if (!performance.passed) throw new Error(performance.failures.join("\n"));
  return {
    schemaVersion: 1,
    contracts: ["PERF-01", "QA-03", platform.startsWith("windows") ? "DIST-02" : "DIST-03"],
    platform,
    version,
    commit,
    passed: true,
    checks: requiredChecks,
    performance: performance.metrics,
  };
}

if (process.argv[1] && basename(process.argv[1]) === "verify-machine-evidence.mjs") {
  const input = argument("input");
  const output = argument("output");
  const platform = argument("platform");
  const version = argument("version");
  const commit = argument("commit");
  if (!input || !output || !platform || !version || !commit) {
    throw new Error("--input, --output, --platform, --version, and --commit are required");
  }
  const result = verifyMachineEvidence(JSON.parse(readFileSync(resolve(input), "utf8")), {
    platform,
    version,
    commit,
  });
  writeFileSync(resolve(output), `${JSON.stringify(result, null, 2)}\n`, "utf8");
  process.stdout.write(`true-machine qualification passed for ${platform}\n`);
}
