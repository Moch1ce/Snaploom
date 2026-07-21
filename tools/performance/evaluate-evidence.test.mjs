// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import test from "node:test";
import { evaluateEvidence, nearestRank } from "./evaluate-evidence.mjs";

function evidence() {
  const frames = Array.from({ length: 30 }, (_, index) => 10 + index / 100);
  return {
    schemaVersion: 1,
    contract: "PERF-01",
    platform: "macos-arm64",
    version: "1.2.3",
    commit: "a".repeat(40),
    dedicatedRunner: true,
    realDesktopSession: true,
    realCaptureBackend: true,
    realWebViewCompositor: true,
    display: { width: 3840, height: 2160, stable60Fps: true },
    idleMemoryBytes: 80_000_000,
    samples: {
      hotkeyToInteractiveMs: Array(30).fill(140),
      objectDragFrameMs: frames,
      mosaicDrawFrameMs: frames,
      mosaicMoveFrameMs: frames,
      undoRedoFrameMs: frames,
      pngWriteMs: Array(20).fill(900),
      successfulCycleMemoryBytes: Array.from(
        { length: 20 },
        (_, index) => 80_000_000 + index * 10_000,
      ),
    },
  };
}

test("uses nearest-rank P95 and accepts complete real-machine evidence", () => {
  assert.equal(nearestRank(Array.from({ length: 30 }, (_, index) => index + 1), 95), 29);
  const result = evaluateEvidence(evidence(), {
    platform: "macos-arm64",
    version: "1.2.3",
    commit: "a".repeat(40),
  });
  assert.equal(result.passed, true);
});

test("headless or synthetic evidence cannot satisfy PERF-01", () => {
  const fixture = evidence();
  fixture.realWebViewCompositor = false;
  assert.throws(() => evaluateEvidence(fixture), /dedicated real 4K desktop session/);
});

test("reports a metric over the hard limit", () => {
  const fixture = evidence();
  fixture.samples.pngWriteMs[18] = 1_500;
  fixture.samples.pngWriteMs[19] = 1_500;
  const result = evaluateEvidence(fixture);
  assert.equal(result.passed, false);
  assert.match(result.failures.join("\n"), /pngWriteP95Ms/);
});
