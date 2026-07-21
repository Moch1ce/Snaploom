import {
  createScreenshotToolbar,
  screenshotUiTheme,
  type ScreenshotToolbarAction,
} from "@snaploom/screenshot-ui";
import {
  ANNOTATION_COLORS,
  ANNOTATION_FONT_SIZES,
  ANNOTATION_STROKE_WIDTHS,
  annotationShortcut,
  type AnnotationState,
} from "./annotations";
import {
  OverlayEditor,
  routePointerPath,
  type CaptureSnapshot,
  type OverlayEditorState,
} from "./app";
import "./style.css";

declare global {
  interface Window {
    __SNAPLOOM_OVERLAY__: {
      readonly editor: OverlayEditor;
      readonly snapshot: CaptureSnapshot;
      lastPng: Uint8Array | null;
      readonly cancel: () => void;
      readonly complete: () => Promise<Uint8Array>;
    };
  }
}

const root = document.querySelector<HTMLElement>("#app") ?? (() => {
  throw new Error("missing #app");
})();

root.style.setProperty("--brand", screenshotUiTheme.colors.brand);
root.style.setProperty("--danger", screenshotUiTheme.colors.danger);
root.style.setProperty("--text", screenshotUiTheme.colors.text);
root.style.setProperty("--surface", screenshotUiTheme.colors.surface);
root.style.setProperty("--border", screenshotUiTheme.colors.border);
root.style.setProperty("--hover", screenshotUiTheme.colors.hover);
root.style.setProperty("--selected", screenshotUiTheme.colors.selected);

const canvas = document.createElement("canvas");
canvas.id = "capture-surface";
canvas.dataset.overlayRole = "canvas";
canvas.setAttribute("aria-label", "截图编辑画布");

const sizeLabel = document.createElement("output");
sizeLabel.className = "screenshot-size-label";
sizeLabel.dataset.overlayUi = "size-label";
sizeLabel.setAttribute("aria-live", "polite");

const textEditorFrame = document.createElement("div");
textEditorFrame.className = "text-editor";
textEditorFrame.dataset.overlayUi = "textarea";
textEditorFrame.hidden = true;
const textEditor = document.createElement("textarea");
textEditor.dataset.overlayUi = "textarea";
textEditor.setAttribute("aria-label", "文字标注输入");
textEditor.setAttribute("wrap", "soft");
textEditor.spellcheck = false;
for (const corner of ["nw", "ne", "se", "sw"] as const) {
  const handle = document.createElement("span");
  handle.className = `text-editor__handle text-editor__handle--${corner}`;
  handle.dataset.overlayUi = "textarea";
  textEditorFrame.append(handle);
}
textEditorFrame.append(textEditor);

let editor: OverlayEditor;
let lastPng: Uint8Array | null = null;

function cancel(): void {
  editor.dispose();
  root.dataset.status = "cancelled";
  root.hidden = true;
}

async function complete(): Promise<Uint8Array> {
  const selection = editor.model.snapshotState().selection;
  const png = await editor.composePng();
  lastPng?.fill(0);
  lastPng = png;
  window.__SNAPLOOM_OVERLAY__.lastPng = png;
  root.dataset.pngBytes = String(png.byteLength);
  root.dataset.pngWidth = String(selection?.width ?? 0);
  root.dataset.pngHeight = String(selection?.height ?? 0);
  root.dataset.status = "completed";
  return png;
}

function onToolbarAction(action: ScreenshotToolbarAction): void {
  if (action === "rectangle" || action === "arrow" || action === "text") {
    editor.setTool(action, true);
  }
  if (action === "undo") editor.undoAnnotation();
  if (action === "redo") editor.redoAnnotation();
  if (action === "cancel") cancel();
  if (action === "complete") void complete();
}

const toolbar = createScreenshotToolbar({
  enabledActions: new Set(["rectangle", "arrow", "text", "cancel", "complete"]),
  onAction: onToolbarAction,
});

const settingsFlyout = document.createElement("section");
settingsFlyout.className = "annotation-settings";
settingsFlyout.dataset.overlayUi = "settings";
settingsFlyout.setAttribute("aria-label", "标注样式");
settingsFlyout.hidden = true;
const colorGroup = document.createElement("div");
colorGroup.className = "annotation-settings__group";
colorGroup.setAttribute("aria-label", "颜色");
for (const color of ANNOTATION_COLORS) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "annotation-settings__color";
  button.dataset.color = color;
  button.dataset.overlayUi = "settings";
  button.setAttribute("aria-label", `颜色 ${color}`);
  button.style.setProperty("--swatch", color);
  button.addEventListener("click", () => editor.setAnnotationStyle({ color }));
  colorGroup.append(button);
}
const widthGroup = document.createElement("div");
widthGroup.className = "annotation-settings__group";
widthGroup.setAttribute("aria-label", "线宽");
for (const strokeWidth of ANNOTATION_STROKE_WIDTHS) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "annotation-settings__width";
  button.dataset.strokeWidth = String(strokeWidth);
  button.dataset.overlayUi = "settings";
  button.setAttribute("aria-label", `线宽 ${strokeWidth}`);
  const sample = document.createElement("span");
  sample.style.height = `${Math.min(strokeWidth, 6)}px`;
  button.append(sample);
  button.addEventListener("click", () => editor.setAnnotationStyle({ strokeWidth }));
  widthGroup.append(button);
}
const fontGroup = document.createElement("div");
fontGroup.className = "annotation-settings__group";
fontGroup.setAttribute("aria-label", "字号");
for (const fontSize of ANNOTATION_FONT_SIZES) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "annotation-settings__font";
  button.dataset.fontSize = String(fontSize);
  button.dataset.overlayUi = "settings";
  button.setAttribute("aria-label", `字号 ${fontSize}`);
  button.textContent = String(fontSize);
  button.addEventListener("click", () => editor.setAnnotationStyle({ fontSize }));
  fontGroup.append(button);
}
settingsFlyout.append(colorGroup, widthGroup, fontGroup);
root.append(canvas, sizeLabel, toolbar, settingsFlyout, textEditorFrame);

function updateAnnotationUi(state: AnnotationState): void {
  root.dataset.annotationTool = state.tool;
  root.dataset.annotationCount = String(state.objects.length);
  for (const action of ["rectangle", "arrow", "text"] as const) {
    const button = toolbar.querySelector<HTMLButtonElement>(`[data-action="${action}"]`);
    button?.setAttribute("aria-pressed", String(state.tool === action));
  }
  const undo = toolbar.querySelector<HTMLButtonElement>('[data-action="undo"]');
  const redo = toolbar.querySelector<HTMLButtonElement>('[data-action="redo"]');
  if (undo) undo.disabled = !state.canUndo;
  if (redo) redo.disabled = !state.canRedo;
  settingsFlyout.hidden = state.settingsOpen === null;
  widthGroup.hidden = state.settingsOpen === "text";
  fontGroup.hidden = state.settingsOpen !== "text";
  if (state.settingsOpen) {
    const anchor = toolbar.querySelector<HTMLElement>(
      `[data-action="${state.settingsOpen}"]`,
    );
    const toolbarLeft = Number.parseFloat(toolbar.style.left || "0");
    const toolbarTop = Number.parseFloat(toolbar.style.top || "0");
    settingsFlyout.style.left = `${toolbarLeft + (anchor?.offsetLeft ?? 14)}px`;
    settingsFlyout.style.top = `${toolbarTop + 52}px`;
  }
  for (const button of settingsFlyout.querySelectorAll<HTMLButtonElement>("[data-color]")) {
    button.setAttribute("aria-pressed", String(button.dataset.color === state.style.color));
  }
  for (const button of settingsFlyout.querySelectorAll<HTMLButtonElement>("[data-stroke-width]")) {
    button.setAttribute(
      "aria-pressed",
      String(Number(button.dataset.strokeWidth) === state.style.strokeWidth),
    );
  }
  for (const button of settingsFlyout.querySelectorAll<HTMLButtonElement>("[data-font-size]")) {
    button.setAttribute(
      "aria-pressed",
      String(Number(button.dataset.fontSize) === state.style.fontSize),
    );
  }
  const projection = editor.textDraftProjection();
  if (state.textDraft && projection) {
    textEditorFrame.hidden = false;
    textEditorFrame.style.left = `${projection.x}px`;
    textEditorFrame.style.top = `${projection.y}px`;
    textEditorFrame.style.width = `${projection.width}px`;
    textEditorFrame.style.height = `${projection.height}px`;
    textEditor.style.fontSize = `${state.textDraft.style.fontSize}px`;
    textEditor.style.color = state.textDraft.style.color;
    if (!state.textDraft.isComposing && textEditor.value !== state.textDraft.value) {
      textEditor.value = state.textDraft.value;
    }
    queueMicrotask(() => {
      if (!textEditorFrame.hidden && document.activeElement !== textEditor) {
        textEditor.focus({ preventScroll: true });
        textEditor.setSelectionRange(textEditor.value.length, textEditor.value.length);
      }
    });
  } else {
    textEditorFrame.hidden = true;
    if (document.activeElement === textEditor) textEditor.blur();
  }
}

function fakeCaptureSnapshot(): { snapshot: CaptureSnapshot; binary: ArrayBuffer } {
  const logicalWidth = 800;
  const logicalHeight = 450;
  const scaleX = 1.6;
  const scaleY = 1.6;
  const width = Math.round(logicalWidth * scaleX);
  const height = Math.round(logicalHeight * scaleY);
  const stride = width * 4;
  const binary = new ArrayBuffer(stride * height);
  const pixels = new Uint8Array(binary);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const offset = y * stride + x * 4;
      const grid = (Math.floor(x / 96) + Math.floor(y / 96)) % 2;
      pixels[offset] = 222 - grid * 12;
      pixels[offset + 1] = 232 - grid * 8;
      pixels[offset + 2] = 244 - grid * 6;
      pixels[offset + 3] = 255;
    }
  }
  return {
    snapshot: {
      sessionId: "fake-capture-snapshot",
      physicalSize: { width, height },
      logicalSize: { width: logicalWidth, height: logicalHeight },
      globalOrigin: { x: -1280, y: -180 },
      pointerPhysical: { x: 320, y: 240 },
      workAreaLogical: { x: 0, y: 24, width: logicalWidth, height: 426 },
      stride,
      windows: [
        {
          stableId: "fake-window-back",
          zOrder: 2,
          bounds: { x: 96, y: 80, width: 800, height: 520 },
        },
        {
          stableId: "fake-window-front",
          zOrder: 1,
          bounds: { x: 288, y: 176, width: 640, height: 360 },
        },
      ],
    },
    binary,
  };
}

const capture = fakeCaptureSnapshot();
editor = new OverlayEditor(
  capture.snapshot,
  capture.binary,
  { canvas, toolbar, sizeLabel },
  {
    onReady: () => {
      root.dataset.status = "ready";
      canvas.dataset.status = "ready";
    },
    onStateChange: (state: OverlayEditorState) => {
      root.dataset.phase = state.phase;
      root.dataset.hasSelection = String(state.selection !== null);
    },
    onAnnotationStateChange: updateAnnotationUi,
  },
);
updateAnnotationUi(editor.annotations.snapshotState());
new Uint8Array(capture.binary).fill(0);
capture.binary = new ArrayBuffer(0);

window.__SNAPLOOM_OVERLAY__ = {
  editor,
  snapshot: capture.snapshot,
  lastPng,
  cancel,
  complete,
};

function localPoint(
  event: Pick<MouseEvent, "clientX" | "clientY">,
): { x: number; y: number } {
  const rect = canvas.getBoundingClientRect();
  return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}

function pathRoles(event: Event): string[] {
  return event.composedPath().map((target) => {
    if (!(target instanceof HTMLElement)) return "";
    return target.dataset.overlayUi ?? target.dataset.overlayRole ?? target.tagName.toLowerCase();
  });
}

root.addEventListener(
  "pointerdown",
  (event) => {
    const route = routePointerPath(event.button, pathRoles(event));
    if (route === "cancel") {
      event.preventDefault();
      event.stopImmediatePropagation();
      cancel();
      return;
    }
    // UI nodes are siblings of the canvas, so their event path cannot reach the
    // screenshot surface. Keeping the event alive also preserves button clicks.
  },
  { capture: true },
);

canvas.addEventListener("pointerdown", (event) => {
  if (event.button !== 0) return;
  canvas.setPointerCapture(event.pointerId);
  editor.pointerDown(localPoint(event));
});
canvas.addEventListener("pointermove", (event) => {
  const point = localPoint(event);
  canvas.style.cursor = editor.cursorAt(point);
  editor.pointerMove(point);
});
canvas.addEventListener("pointerup", (event) => {
  if (event.button !== 0) return;
  editor.pointerUp(localPoint(event));
  if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
});
canvas.addEventListener("dblclick", (event) => {
  if (editor.beginTextEditAt(localPoint(event))) return;
  if (editor.model.snapshotState().selection) void complete();
});
textEditor.addEventListener("input", () => editor.updateTextDraft(textEditor.value));
textEditor.addEventListener("compositionstart", () => editor.compositionStart());
textEditor.addEventListener("compositionupdate", (event) => {
  editor.compositionUpdate(event.data);
});
textEditor.addEventListener("compositionend", (event) => {
  editor.compositionEnd(event.data);
  editor.updateTextDraft(textEditor.value);
});
root.addEventListener("contextmenu", (event) => event.preventDefault());
window.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    event.preventDefault();
    cancel();
    return;
  }
  const textDraft = editor.annotations.snapshotState().textDraft;
  if (textDraft) {
    if (
      (event.metaKey || event.ctrlKey) &&
      event.key === "Enter" &&
      !event.isComposing &&
      !textDraft.isComposing
    ) {
      event.preventDefault();
      editor.commitTextDraft();
    }
    return;
  }
  if (event.key === "Enter" && editor.model.snapshotState().selection) {
    event.preventDefault();
    void complete();
  } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "z") {
    event.preventDefault();
    if (event.shiftKey) editor.redoAnnotation();
    else editor.undoAnnotation();
  } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "y") {
    event.preventDefault();
    editor.redoAnnotation();
  } else if (event.key === "Delete" || event.key === "Backspace") {
    if (editor.deleteSelectedAnnotation()) event.preventDefault();
  } else {
    const tool = annotationShortcut({
      key: event.key,
      metaKey: event.metaKey,
      ctrlKey: event.ctrlKey,
      altKey: event.altKey,
      isComposing: event.isComposing,
    });
    if (
      tool === "rectangle" ||
      tool === "arrow" ||
      tool === "text" ||
      tool === "select"
    ) {
      event.preventDefault();
      editor.setTool(tool, false);
    }
  }
});
