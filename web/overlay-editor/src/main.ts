import {
  createScreenshotToolbar,
  screenshotUiTheme,
  type ScreenshotToolbarAction,
} from "@snaploom/screenshot-ui";
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
  if (action === "cancel") cancel();
  if (action === "complete") void complete();
}

const toolbar = createScreenshotToolbar({
  enabledActions: new Set(["cancel", "complete"]),
  onAction: onToolbarAction,
});
root.append(canvas, sizeLabel, toolbar);

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
  },
);
new Uint8Array(capture.binary).fill(0);
capture.binary = new ArrayBuffer(0);

window.__SNAPLOOM_OVERLAY__ = {
  editor,
  snapshot: capture.snapshot,
  lastPng,
  cancel,
  complete,
};

function localPoint(event: PointerEvent): { x: number; y: number } {
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
canvas.addEventListener("pointermove", (event) => editor.pointerMove(localPoint(event)));
canvas.addEventListener("pointerup", (event) => {
  if (event.button !== 0) return;
  editor.pointerUp(localPoint(event));
  if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
});
canvas.addEventListener("dblclick", () => {
  if (editor.model.snapshotState().selection) void complete();
});
root.addEventListener("contextmenu", (event) => event.preventDefault());
window.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    event.preventDefault();
    cancel();
  } else if (event.key === "Enter" && editor.model.snapshotState().selection) {
    event.preventDefault();
    void complete();
  }
});
