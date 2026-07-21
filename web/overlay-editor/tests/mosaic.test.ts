import { describe, expect, it } from "vitest";
import {
  AnnotationSession,
  interpolateMosaicPoints,
  mosaicDamageTiles,
  mosaicTileKey,
} from "../src/annotations";

const selection = { x: 0, y: 0, width: 3840, height: 2160 };
const scale = { scaleX: 2, scaleY: 2 };

describe("non-destructive tiled mosaic", () => {
  it("creates a non-empty point-click stroke and makes it undoable", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("mosaic", false);
    session.pointerDown({ x: 100, y: 100 });
    session.pointerUp({ x: 100, y: 100 });
    expect(session.snapshotState().objects[0]).toMatchObject({
      kind: "mosaic",
      points: [{ x: 100, y: 100 }],
      style: { mosaicBrushSize: 32, mosaicBlockSize: 12 },
    });
    expect(session.undo()).toBe(true);
    expect(session.snapshotState().objects).toHaveLength(0);
    expect(session.redo()).toBe(true);
    expect(session.snapshotState().objects).toHaveLength(1);
  });

  it("interpolates a long trajectory without gaps larger than one quarter brush", () => {
    const points = interpolateMosaicPoints(
      { x: 10, y: 10 },
      { x: 510, y: 210 },
      64,
    );
    expect(points.length).toBeGreaterThan(20);
    for (let index = 1; index < points.length; index += 1) {
      const previous = points[index - 1]!;
      const current = points[index]!;
      expect(Math.hypot(current.x - previous.x, current.y - previous.y)).toBeLessThanOrEqual(16.01);
    }
  });

  it("damages only 128px tiles touched by the stroke instead of its bounding box", () => {
    const points = interpolateMosaicPoints(
      { x: 64, y: 64 },
      { x: 1984, y: 1984 },
      32,
    );
    const damaged = mosaicDamageTiles(points, 64, { width: 3840, height: 2160 });
    expect(damaged).toContain("0:0");
    expect(damaged).toContain("15:15");
    expect(damaged).not.toContain("0:10");
    expect(damaged.size).toBeLessThan(80);
    expect(mosaicTileKey(4, 7, 12)).toBe("12:4:7");
  });

  it("moves, changes intensity, deletes, and restores mosaic through history", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("mosaic", false);
    session.pointerDown({ x: 100, y: 100 });
    session.pointerMove({ x: 300, y: 100 });
    session.pointerUp({ x: 300, y: 100 });
    const id = session.snapshotState().objects[0]?.id;

    session.setTool("select", false);
    session.pointerDown({ x: 200, y: 100 });
    session.pointerMove({ x: 220, y: 140 });
    session.pointerUp({ x: 220, y: 140 });
    const moved = session.snapshotState().objects[0];
    expect(moved).toMatchObject({ id, kind: "mosaic" });
    if (!moved || moved.kind !== "mosaic") throw new Error("mosaic expected");
    expect(moved.points[0]).toEqual({ x: 120, y: 140 });

    session.setStyle({ mosaicBrushSize: 64, mosaicBlockSize: 16 });
    expect(session.snapshotState().objects[0]).toMatchObject({
      style: { mosaicBrushSize: 64, mosaicBlockSize: 16 },
    });
    expect(session.deleteSelected()).toBe(true);
    expect(session.undo()).toBe(true);
    expect(session.snapshotState().objects[0]).toMatchObject({ id });
  });

  it("keeps 4K trajectory damage planning within the frame-time pre-gate", () => {
    const samples: number[] = [];
    for (let run = 0; run < 40; run += 1) {
      const started = performance.now();
      const points = interpolateMosaicPoints(
        { x: 32, y: 32 + run },
        { x: 3800, y: 2100 - run },
        64,
      );
      mosaicDamageTiles(points, 64, { width: 3840, height: 2160 });
      samples.push(performance.now() - started);
    }
    samples.sort((left, right) => left - right);
    const p95 = samples[Math.ceil(samples.length * 0.95) - 1] ?? Number.POSITIVE_INFINITY;
    expect(p95).toBeLessThanOrEqual(16.667);
  });
});
