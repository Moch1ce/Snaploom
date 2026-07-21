import { describe, expect, it } from "vitest";
import {
  ANNOTATION_COLORS,
  AnnotationSession,
  annotationShortcut,
  buildAnnotationRenderPlan,
} from "../src/annotations";

const selection = { x: 0, y: 0, width: 800, height: 600 };
const scale = { scaleX: 2, scaleY: 2 };

describe("rectangle, arrow, and annotation history", () => {
  it("freezes the six-color and three-width style contract", () => {
    expect(ANNOTATION_COLORS).toEqual([
      "#FF4D4F",
      "#FADB14",
      "#07C977",
      "#1677FF",
      "#202124",
      "#FFFFFF",
    ]);
    expect(new AnnotationSession({ selection, scale }).snapshotState().style).toEqual({
      color: "#FF4D4F",
      strokeWidth: 4,
      fontSize: 24,
      mosaicBrushSize: 32,
      mosaicBlockSize: 12,
    });
  });

  it("starts a new capture session with validated persisted annotation styles", () => {
    const style = {
      color: "#1677FF" as const,
      strokeWidth: 8 as const,
      fontSize: 32 as const,
      mosaicBrushSize: 64 as const,
      mosaicBlockSize: 16 as const,
    };
    expect(
      new AnnotationSession({ selection, scale, initialStyle: style })
        .snapshotState()
        .style,
    ).toEqual(style);
  });

  it("maps unmodified R/A/V shortcuts and leaves modified keys alone", () => {
    expect(annotationShortcut({ key: "r" })).toBe("rectangle");
    expect(annotationShortcut({ key: "A" })).toBe("arrow");
    expect(annotationShortcut({ key: "v" })).toBe("select");
    expect(annotationShortcut({ key: "r", metaKey: true })).toBeNull();
    expect(annotationShortcut({ key: "a", controlKey: true })).toBeNull();
    expect(annotationShortcut({ key: "v", altKey: true })).toBeNull();
  });

  it("creates non-destructive objects in visual order and ignores zero length", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("rectangle", true);
    session.pointerDown({ x: 20, y: 20 });
    session.pointerUp({ x: 20, y: 20 });
    expect(session.snapshotState().objects).toEqual([]);

    session.pointerDown({ x: 20, y: 20 });
    session.pointerMove({ x: 220, y: 120 });
    session.pointerUp({ x: 220, y: 120 });
    session.setTool("arrow", true);
    session.pointerDown({ x: 20, y: 20 });
    session.pointerMove({ x: 220, y: 120 });
    session.pointerUp({ x: 220, y: 120 });

    const state = session.snapshotState();
    expect(state.objects.map((object) => object.kind)).toEqual(["rectangle", "arrow"]);
    expect(state.everEdited).toBe(true);
    expect(session.hitTest({ x: 120, y: 70 })?.kind).toBe("arrow");
    expect(buildAnnotationRenderPlan(state).objects.map((object) => object.kind)).toEqual([
      "rectangle",
      "arrow",
    ]);
  });

  it("moves, resizes, styles, deletes, undoes, and redoes selected objects", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("rectangle", false);
    session.pointerDown({ x: 100, y: 100 });
    session.pointerMove({ x: 300, y: 200 });
    session.pointerUp({ x: 300, y: 200 });
    const id = session.snapshotState().objects[0]?.id;
    expect(id).toBeTruthy();

    session.setTool("select", false);
    session.pointerDown({ x: 100, y: 100 });
    session.pointerMove({ x: 80, y: 80 });
    session.pointerUp({ x: 80, y: 80 });
    expect(session.snapshotState().objects[0]).toMatchObject({
      rect: { x: 80, y: 80, width: 220, height: 120 },
    });

    session.pointerDown({ x: 190, y: 80 });
    session.pointerMove({ x: 190, y: 100 });
    session.pointerUp({ x: 190, y: 100 });
    expect(session.snapshotState().objects[0]).toMatchObject({
      rect: { x: 80, y: 100, width: 220, height: 100 },
    });

    session.setStyle({ color: "#1677FF", strokeWidth: 8 });
    expect(session.snapshotState().objects[0]).toMatchObject({
      id,
      style: { color: "#1677FF", strokeWidth: 8 },
    });
    expect(session.deleteSelected()).toBe(true);
    expect(session.snapshotState().objects).toHaveLength(0);
    expect(session.undo()).toBe(true);
    expect(session.snapshotState().objects[0]).toMatchObject({ id, style: { color: "#1677FF" } });
    expect(session.redo()).toBe(true);
    expect(session.snapshotState().objects).toHaveLength(0);
  });

  it("selects the visual topmost same-type object while a draw tool is active", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("rectangle", false);
    for (const offset of [0, 10]) {
      session.pointerDown({ x: 100 + offset, y: 100 + offset });
      session.pointerMove({ x: 300 + offset, y: 220 + offset });
      session.pointerUp({ x: 300 + offset, y: 220 + offset });
    }
    const top = session.snapshotState().objects[1];
    expect(session.cursorAt({ x: 110, y: 150 })).toBe("move");
    session.pointerDown({ x: 110, y: 150 });
    session.pointerUp({ x: 110, y: 150 });
    expect(session.snapshotState()).toMatchObject({
      tool: "select",
      selectedId: top?.id,
      settingsOpen: "rectangle",
    });
  });

  it("does not jump history while a transform preview is active", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("arrow", false);
    session.pointerDown({ x: 10, y: 10 });
    session.pointerMove({ x: 100, y: 100 });
    expect(session.undo()).toBe(false);
    session.pointerUp({ x: 100, y: 100 });
    expect(session.undo()).toBe(true);
    expect(session.snapshotState().objects).toHaveLength(0);
  });
});
