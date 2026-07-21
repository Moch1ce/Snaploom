// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, writeFileSync } from "node:fs";
import { basename, resolve } from "node:path";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function numericSamples(evidence, name, minimum) {
  const values = evidence.samples?.[name];
  if (
    !Array.isArray(values) ||
    values.length < minimum ||
    values.some((value) => !Number.isFinite(value) || value < 0)
  ) {
    throw new Error(`${name} requires at least ${minimum} non-negative numeric samples`);
  }
  return values;
}

export function nearestRank(values, percentile) {
  const ordered = [...values].sort((left, right) => left - right);
  return ordered[Math.ceil((percentile / 100) * ordered.length) - 1];
}

function median(values) {
  const ordered = [...values].sort((left, right) => left - right);
  const middle = Math.floor(ordered.length / 2);
  return ordered.length % 2 === 0
    ? (ordered[middle - 1] + ordered[middle]) / 2
    : ordered[middle];
}

export function evaluateEvidence(evidence, expected = {}) {
  if (
    evidence.schemaVersion !== 1 ||
    evidence.contract !== "PERF-01" ||
    evidence.dedicatedRunner !== true ||
    evidence.realDesktopSession !== true ||
    evidence.realCaptureBackend !== true ||
    evidence.realWebViewCompositor !== true ||
    evidence.display?.width !== 3840 ||
    evidence.display?.height !== 2160 ||
    evidence.display?.stable60Fps !== true
  ) {
    throw new Error("PERF-01 evidence must come from a dedicated real 4K desktop session");
  }
  if (expected.platform && evidence.platform !== expected.platform) {
    throw new Error(`expected ${expected.platform} evidence, got ${evidence.platform}`);
  }
  if (expected.commit && evidence.commit !== expected.commit) {
    throw new Error("performance evidence does not cover the release commit");
  }
  if (expected.version && evidence.version !== expected.version) {
    throw new Error("performance evidence does not cover the release version");
  }
  if (!/^[0-9a-f]{40}$/.test(evidence.commit ?? "")) {
    throw new Error("performance evidence requires a full release commit SHA");
  }

  const hotkey = numericSamples(evidence, "hotkeyToInteractiveMs", 30);
  const drag = numericSamples(evidence, "objectDragFrameMs", 30);
  const mosaicDraw = numericSamples(evidence, "mosaicDrawFrameMs", 30);
  const mosaicMove = numericSamples(evidence, "mosaicMoveFrameMs", 30);
  const undoRedo = numericSamples(evidence, "undoRedoFrameMs", 30);
  const png = numericSamples(evidence, "pngWriteMs", 20);
  const memory = numericSamples(evidence, "successfulCycleMemoryBytes", 20);
  if (!Number.isFinite(evidence.idleMemoryBytes) || evidence.idleMemoryBytes < 0) {
    throw new Error("idleMemoryBytes is required");
  }
  const earlierMedian = median(memory.slice(-10, -5));
  const laterMedian = median(memory.slice(-5));
  const tailGrowthRatio = earlierMedian === 0 ? Infinity : (laterMedian - earlierMedian) / earlierMedian;
  const metrics = {
    hotkeyP95Ms: nearestRank(hotkey, 95),
    objectDragP95Ms: nearestRank(drag, 95),
    mosaicDrawP95Ms: nearestRank(mosaicDraw, 95),
    mosaicMoveP95Ms: nearestRank(mosaicMove, 95),
    undoRedoP95Ms: nearestRank(undoRedo, 95),
    pngWriteP95Ms: nearestRank(png, 95),
    idleMemoryBytes: evidence.idleMemoryBytes,
    tailGrowthRatio,
  };
  const limits = {
    hotkeyP95Ms: 150,
    objectDragP95Ms: 16.667,
    mosaicDrawP95Ms: 16.667,
    mosaicMoveP95Ms: 16.667,
    undoRedoP95Ms: 16.667,
    pngWriteP95Ms: 1000,
    idleMemoryBytes: 100_000_000,
    tailGrowthRatio: 0.01,
  };
  const failures = Object.entries(limits)
    .filter(([name, limit]) => metrics[name] > limit)
    .map(([name, limit]) => `${name}=${metrics[name]} exceeds ${limit}`);
  return {
    schemaVersion: 1,
    contract: "PERF-01",
    platform: evidence.platform,
    version: evidence.version,
    commit: evidence.commit,
    passed: failures.length === 0,
    metrics,
    limits,
    failures,
  };
}

if (process.argv[1] && basename(process.argv[1]) === "evaluate-evidence.mjs") {
  const input = argument("input");
  if (!input) throw new Error("--input is required");
  const result = evaluateEvidence(JSON.parse(readFileSync(resolve(input), "utf8")), {
    platform: argument("platform"),
    version: argument("version"),
    commit: argument("commit"),
  });
  const output = argument("output");
  if (output) writeFileSync(resolve(output), `${JSON.stringify(result, null, 2)}\n`, "utf8");
  if (!result.passed) throw new Error(result.failures.join("\n"));
  process.stdout.write(`PERF-01 passed for ${result.platform} at ${result.commit}\n`);
}
