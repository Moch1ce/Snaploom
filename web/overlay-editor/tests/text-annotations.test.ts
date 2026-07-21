import { describe, expect, it } from "vitest";
import {
  AnnotationSession,
  layoutTextAnnotation,
  projectTextDraft,
} from "../src/annotations";

const selection = { x: 40, y: 20, width: 600, height: 400 };
const scale = { scaleX: 2, scaleY: 2 };

function createText(session: AnnotationSession, text = "中文\nEnglish"): string {
  session.setTool("text", false);
  session.pointerDown({ x: 100, y: 80 });
  session.pointerUp({ x: 100, y: 80 });
  session.updateTextDraft(text);
  expect(session.commitTextDraft()).toBe(true);
  return session.snapshotState().objects[0]?.id ?? "";
}

describe("text annotation and reusable IME draft", () => {
  it("tracks committed and pre-edit text without creating a permanent DOM object", () => {
    const session = new AnnotationSession({ selection, scale });
    session.setTool("text", true);
    session.pointerDown({ x: 100, y: 80 });
    session.pointerUp({ x: 100, y: 80 });
    session.updateTextDraft("中");
    session.compositionStart();
    session.compositionUpdate("中文");
    expect(session.snapshotState().textDraft).toMatchObject({
      value: "中",
      preedit: "中文",
      isComposing: true,
    });
    expect(session.undo()).toBe(false);
    session.compositionEnd("中文");
    session.updateTextDraft("中文\nEnglish");
    expect(session.commitTextDraft()).toBe(true);

    const state = session.snapshotState();
    expect(state.objects).toHaveLength(1);
    expect(state.objects[0]).toMatchObject({
      kind: "text",
      text: "中文\nEnglish",
      origin: { x: 100, y: 80 },
      style: { color: "#FF4D4F", fontSize: 24 },
    });
    expect(state.textDraft).toBeNull();
    expect(state.canUndo).toBe(true);
  });

  it("hides an edited object from the render plan and restores it as one history command", () => {
    const session = new AnnotationSession({ selection, scale });
    const id = createText(session, "before");
    expect(session.beginTextEditAt({ x: 110, y: 90 })).toBe(true);
    expect(session.renderPlan().objects).toHaveLength(0);
    session.updateTextDraft("after");
    expect(session.commitTextDraft()).toBe(true);
    expect(session.snapshotState().objects[0]).toMatchObject({ id, text: "after" });
    expect(session.undo()).toBe(true);
    expect(session.snapshotState().objects[0]).toMatchObject({ id, text: "before" });
  });

  it("uses the first blank canvas click only to commit active text", () => {
    const session = new AnnotationSession({ selection, scale });
    createText(session, "one");
    session.setTool("text", false);
    session.pointerDown({ x: 300, y: 200 });
    session.pointerUp({ x: 300, y: 200 });
    expect(session.snapshotState()).toMatchObject({
      textDraft: { origin: { x: 300, y: 200 } },
    });
    session.updateTextDraft("two");

    session.pointerDown({ x: 500, y: 300 });
    session.pointerUp({ x: 500, y: 300 });
    expect(session.snapshotState().objects).toHaveLength(2);
    expect(session.snapshotState().textDraft).toBeNull();

    session.pointerDown({ x: 500, y: 300 });
    session.pointerUp({ x: 500, y: 300 });
    expect(session.snapshotState().textDraft).toMatchObject({ origin: { x: 500, y: 300 } });
  });

  it("distinguishes click-to-edit from a text-object drag after four logical pixels", () => {
    const session = new AnnotationSession({ selection, scale });
    createText(session, "drag me");
    session.setTool("text", false);
    session.pointerDown({ x: 110, y: 90 });
    session.pointerMove({ x: 114, y: 94 });
    session.pointerUp({ x: 114, y: 94 });
    expect(session.snapshotState().textDraft).not.toBeNull();
    session.cancelTextDraft();

    session.pointerDown({ x: 110, y: 90 });
    session.pointerMove({ x: 130, y: 110 });
    session.pointerUp({ x: 130, y: 110 });
    expect(session.snapshotState().textDraft).toBeNull();
    expect(session.snapshotState().objects[0]).toMatchObject({
      origin: { x: 120, y: 100 },
    });
  });

  it("wraps all lines into one hit box and clamps the textarea to the selection", () => {
    const session = new AnnotationSession({ selection, scale });
    createText(session, `${"abcdefghij".repeat(5)}中文`);
    const object = session.snapshotState().objects[0];
    if (!object || object.kind !== "text") throw new Error("text object expected");
    const layout = layoutTextAnnotation(object, scale);
    expect(layout.lines.length).toBeGreaterThan(1);
    expect(layout.bounds.x + layout.bounds.width).toBeLessThanOrEqual(selection.x + selection.width);
    expect(session.hitTest({ x: layout.bounds.x + 2, y: layout.bounds.y + layout.bounds.height - 2 })?.id)
      .toBe(object.id);

    session.setTool("text", false);
    session.pointerDown({ x: 620, y: 400 });
    session.pointerUp({ x: 620, y: 400 });
    session.updateTextDraft("near edge");
    const projection = projectTextDraft(
      session.snapshotState().textDraft!,
      selection,
      scale,
    );
    expect(projection.x + projection.width).toBeLessThanOrEqual((selection.x + selection.width) / 2);
    expect(projection.y + projection.height).toBeLessThanOrEqual((selection.y + selection.height) / 2);
  });
});
