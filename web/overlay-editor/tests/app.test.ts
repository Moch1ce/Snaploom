import { describe, expect, it } from "vitest";
import {
  MAX_CAPTURE_BINARY_BYTES,
  OverlayEditorModel,
  bgraToRgba,
  cropRgba,
  selectionHandles,
  validateSnapshot,
} from "../src/app";

const snapshot = {
  sessionId: "session-1",
  physicalSize: { width: 1000, height: 800 },
  logicalSize: { width: 800, height: 400 },
  globalOrigin: { x: -1920, y: -200 },
  pointerPhysical: { x: 100, y: 120 },
  workAreaLogical: { x: 0, y: 24, width: 800, height: 376 },
  stride: 4000,
  windows: [
    { stableId: "back", zOrder: 2, bounds: { x: 80, y: 80, width: 300, height: 200 } },
    { stableId: "front", zOrder: 1, bounds: { x: 90, y: 90, width: 200, height: 100 } },
  ],
} as const;

describe("OverlayEditor physical selection contract", () => {
  it("validates bounded binary without using a data URL", () => {
    expect(validateSnapshot(snapshot, snapshot.stride * snapshot.physicalSize.height)).toEqual({
      scaleX: 1.25,
      scaleY: 2,
    });
    expect(() => validateSnapshot(snapshot, MAX_CAPTURE_BINARY_BYTES + 1)).toThrow(
      "capture binary exceeds limit",
    );
    expect(() => validateSnapshot(snapshot, 10)).toThrow("capture binary length mismatch");
  });

  it.each([1, 1.25, 1.5, 1.75, 2])(
    "maps logical gestures to exact physical pixels at %sx scale",
    (scale) => {
      const model = new OverlayEditorModel({
        ...snapshot,
        physicalSize: { width: Math.round(800 * scale), height: Math.round(400 * scale) },
        stride: Math.round(800 * scale) * 4,
        windows: [],
      });
      model.pointerDown({ x: 10.25, y: 20.5 });
      model.pointerMove({ x: 110.25, y: 70.5 });
      model.pointerUp({ x: 110.25, y: 70.5 });
      expect(model.snapshotState().selection).toEqual({
        x: Math.floor(10.25 * scale),
        y: Math.floor(20.5 * scale),
        width: Math.ceil(100 * scale),
        height: Math.ceil(50 * scale),
      });
    },
  );

  it("suppresses initial snap until movement, then picks visual frontmost window", () => {
    const model = new OverlayEditorModel(snapshot);
    expect(model.snapshotState().snapCandidate).toBeNull();
    model.pointerDown({ x: 80, y: 60 });
    model.pointerUp({ x: 80, y: 60 });
    expect(model.snapshotState().selection).toBeNull();

    model.pointerMove({ x: 81, y: 61 });
    expect(model.snapshotState().snapCandidate?.stableId).toBe("front");
    model.pointerDown({ x: 81, y: 61 });
    model.pointerUp({ x: 81, y: 61 });
    expect(model.snapshotState().selection).toEqual({ x: 90, y: 90, width: 200, height: 100 });
  });

  it("rejects sub-threshold and sub-8px selections", () => {
    const model = new OverlayEditorModel({ ...snapshot, windows: [] });
    model.pointerDown({ x: 10, y: 10 });
    model.pointerMove({ x: 13.9, y: 13.9 });
    model.pointerUp({ x: 13.9, y: 13.9 });
    expect(model.snapshotState().selection).toBeNull();

    model.pointerDown({ x: 10, y: 10 });
    model.pointerMove({ x: 15, y: 15 });
    model.pointerUp({ x: 15, y: 15 });
    expect(model.snapshotState().selection).toBeNull();
  });

  it("selects the entire display when a post-movement click misses every window", () => {
    const model = new OverlayEditorModel({ ...snapshot, windows: [] });
    model.pointerMove({ x: 81, y: 61 });
    model.pointerDown({ x: 700, y: 300 });
    model.pointerUp({ x: 700, y: 300 });
    expect(model.snapshotState().selection).toEqual({
      x: 0,
      y: 0,
      width: 1000,
      height: 800,
    });
  });

  it("moves, clamps, resizes, and replaces an existing selection", () => {
    const model = new OverlayEditorModel({ ...snapshot, windows: [] });
    model.pointerDown({ x: 20, y: 20 });
    model.pointerMove({ x: 120, y: 70 });
    model.pointerUp({ x: 120, y: 70 });
    expect(model.snapshotState().selection).toEqual({
      x: 25,
      y: 40,
      width: 125,
      height: 100,
    });

    model.pointerDown({ x: 40, y: 30 });
    model.pointerMove({ x: -100, y: -100 });
    model.pointerUp({ x: -100, y: -100 });
    expect(model.snapshotState().selection).toEqual({ x: 0, y: 0, width: 125, height: 100 });

    model.pointerDown({ x: 100, y: 25 });
    model.pointerMove({ x: 120, y: 25 });
    model.pointerUp({ x: 120, y: 25 });
    expect(model.snapshotState().selection).toEqual({ x: 0, y: 0, width: 150, height: 100 });

    model.pointerDown({ x: 200, y: 150 });
    model.pointerMove({ x: 240, y: 190 });
    model.pointerUp({ x: 240, y: 190 });
    expect(model.snapshotState().selection).toEqual({ x: 250, y: 300, width: 50, height: 80 });
  });

  it("clips a captured pointer gesture to the physical frame", () => {
    const model = new OverlayEditorModel({ ...snapshot, windows: [] });
    model.pointerDown({ x: -100, y: -100 });
    model.pointerMove({ x: 900, y: 500 });
    model.pointerUp({ x: 900, y: 500 });
    expect(model.snapshotState().selection).toEqual({
      x: 0,
      y: 0,
      width: 1000,
      height: 800,
    });
  });

  it("projects exactly eight deterministic resize handles", () => {
    const handles = selectionHandles({ x: 100, y: 200, width: 300, height: 160 });
    expect(handles.map((handle) => handle.kind)).toEqual([
      "nw",
      "n",
      "ne",
      "e",
      "se",
      "s",
      "sw",
      "w",
    ]);
    expect(handles[3]?.point).toEqual({ x: 400, y: 280 });
  });

  it("converts premultiplied BGRA and crops decoded RGBA without DOM capture", () => {
    const rgba = bgraToRgba(
      new Uint8Array([
        0, 0, 255, 255,
        0, 128, 0, 128,
        255, 0, 0, 255,
        255, 255, 255, 255,
      ]),
      8,
      2,
      2,
    );
    expect([...rgba]).toEqual([
      255, 0, 0, 255,
      0, 255, 0, 128,
      0, 0, 255, 255,
      255, 255, 255, 255,
    ]);
    expect([...cropRgba(rgba, 2, { x: 1, y: 0, width: 1, height: 2 })]).toEqual([
      0, 255, 0, 128,
      255, 255, 255, 255,
    ]);
  });
});
