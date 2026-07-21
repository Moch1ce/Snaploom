import {
  projectFloatingUi,
  screenshotUiTheme,
  type FloatingUiProjection,
  type Point,
  type Rect,
} from "@snaploom/screenshot-ui";
import {
  AnnotationSession,
  renderAnnotations,
  type AnnotationState,
  type AnnotationStyle,
  type AnnotationTool,
} from "./annotations";

export const MAX_CAPTURE_BINARY_BYTES = 256 * 1024 * 1024;

export interface Size {
  readonly width: number;
  readonly height: number;
}

export interface WindowCandidate {
  readonly stableId: string;
  readonly zOrder: number;
  readonly bounds: Rect;
}

export interface CaptureSnapshot {
  readonly sessionId: string;
  readonly physicalSize: Size;
  readonly logicalSize: Size;
  readonly globalOrigin: Point;
  readonly pointerPhysical: Point;
  readonly workAreaLogical: Rect;
  readonly stride: number;
  readonly windows: readonly WindowCandidate[];
}

export interface SnapshotScale {
  readonly scaleX: number;
  readonly scaleY: number;
}

export interface SelectionHandle {
  readonly kind: "nw" | "n" | "ne" | "e" | "se" | "s" | "sw" | "w";
  readonly point: Point;
}

export interface OverlayEditorState {
  readonly selection: Rect | null;
  readonly previewSelection: Rect | null;
  readonly snapCandidate: WindowCandidate | null;
  readonly snapEnabled: boolean;
  readonly phase: "idle" | "selecting" | "moving" | "resizing" | "disposed";
}

type DragState =
  | { readonly kind: "select"; readonly origin: Point; current: Point }
  | {
      readonly kind: "move";
      readonly origin: Point;
      readonly initial: Rect;
      current: Point;
    }
  | {
      readonly kind: "resize";
      readonly origin: Point;
      readonly initial: Rect;
      readonly handle: SelectionHandle["kind"];
      current: Point;
    };

function isFinitePositive(value: number): boolean {
  return Number.isFinite(value) && value > 0;
}

function assertInteger(value: number, field: string): void {
  if (!Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`invalid ${field}`);
  }
}

export function validateSnapshot(
  snapshot: CaptureSnapshot,
  binaryLength: number,
): SnapshotScale {
  if (binaryLength > MAX_CAPTURE_BINARY_BYTES) {
    throw new Error("capture binary exceeds limit");
  }
  assertInteger(snapshot.physicalSize.width, "physical width");
  assertInteger(snapshot.physicalSize.height, "physical height");
  if (
    !isFinitePositive(snapshot.logicalSize.width) ||
    !isFinitePositive(snapshot.logicalSize.height)
  ) {
    throw new Error("invalid logical size");
  }
  const minimumStride = snapshot.physicalSize.width * 4;
  if (!Number.isSafeInteger(snapshot.stride) || snapshot.stride < minimumStride) {
    throw new Error("invalid capture stride");
  }
  const expectedLength = snapshot.stride * snapshot.physicalSize.height;
  if (!Number.isSafeInteger(expectedLength) || binaryLength !== expectedLength) {
    throw new Error("capture binary length mismatch");
  }
  return {
    scaleX: snapshot.physicalSize.width / snapshot.logicalSize.width,
    scaleY: snapshot.physicalSize.height / snapshot.logicalSize.height,
  };
}

function contains(rect: Rect, point: Point): boolean {
  return (
    point.x >= rect.x &&
    point.y >= rect.y &&
    point.x < rect.x + rect.width &&
    point.y < rect.y + rect.height
  );
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}

function physicalRectFromGesture(
  start: Point,
  end: Point,
  scale: SnapshotScale,
): Rect {
  return {
    x: Math.floor(Math.min(start.x, end.x) * scale.scaleX),
    y: Math.floor(Math.min(start.y, end.y) * scale.scaleY),
    width: Math.ceil(Math.abs(end.x - start.x) * scale.scaleX),
    height: Math.ceil(Math.abs(end.y - start.y) * scale.scaleY),
  };
}

function logicalPointToPhysical(point: Point, scale: SnapshotScale): Point {
  return {
    x: Math.floor(point.x * scale.scaleX),
    y: Math.floor(point.y * scale.scaleY),
  };
}

function logicalRect(rect: Rect, scale: SnapshotScale): Rect {
  return {
    x: rect.x / scale.scaleX,
    y: rect.y / scale.scaleY,
    width: rect.width / scale.scaleX,
    height: rect.height / scale.scaleY,
  };
}

function movedEnough(start: Point, end: Point): boolean {
  return (
    Math.hypot(end.x - start.x, end.y - start.y) >=
    screenshotUiTheme.selection.gestureThreshold
  );
}

function validSelection(rect: Rect): boolean {
  return (
    rect.width >= screenshotUiTheme.selection.minimumPhysicalSize &&
    rect.height >= screenshotUiTheme.selection.minimumPhysicalSize
  );
}

export function selectionHandles(rect: Rect): readonly SelectionHandle[] {
  const centerX = rect.x + rect.width / 2;
  const centerY = rect.y + rect.height / 2;
  const right = rect.x + rect.width;
  const bottom = rect.y + rect.height;
  return [
    { kind: "nw", point: { x: rect.x, y: rect.y } },
    { kind: "n", point: { x: centerX, y: rect.y } },
    { kind: "ne", point: { x: right, y: rect.y } },
    { kind: "e", point: { x: right, y: centerY } },
    { kind: "se", point: { x: right, y: bottom } },
    { kind: "s", point: { x: centerX, y: bottom } },
    { kind: "sw", point: { x: rect.x, y: bottom } },
    { kind: "w", point: { x: rect.x, y: centerY } },
  ];
}

function resizeRect(
  initial: Rect,
  handle: SelectionHandle["kind"],
  delta: Point,
  bounds: Size,
): Rect {
  let left = initial.x;
  let top = initial.y;
  let right = initial.x + initial.width;
  let bottom = initial.y + initial.height;
  if (handle.includes("w")) left += delta.x;
  if (handle.includes("e")) right += delta.x;
  if (handle.includes("n")) top += delta.y;
  if (handle.includes("s")) bottom += delta.y;
  left = clamp(left, 0, bounds.width);
  right = clamp(right, 0, bounds.width);
  top = clamp(top, 0, bounds.height);
  bottom = clamp(bottom, 0, bounds.height);
  return {
    x: Math.min(left, right),
    y: Math.min(top, bottom),
    width: Math.abs(right - left),
    height: Math.abs(bottom - top),
  };
}

export class OverlayEditorModel {
  readonly #snapshot: CaptureSnapshot;
  readonly #scale: SnapshotScale;
  #selection: Rect | null = null;
  #snapCandidate: WindowCandidate | null = null;
  #snapEnabled = false;
  #drag: DragState | null = null;
  #disposed = false;

  constructor(snapshot: CaptureSnapshot) {
    this.#snapshot = snapshot;
    this.#scale = {
      scaleX: snapshot.physicalSize.width / snapshot.logicalSize.width,
      scaleY: snapshot.physicalSize.height / snapshot.logicalSize.height,
    };
  }

  get scale(): SnapshotScale {
    return this.#scale;
  }

  selectionHandleAt(point: Point): SelectionHandle["kind"] | null {
    return this.#hitHandle(
      logicalPointToPhysical(point, this.#scale),
      6 * Math.max(this.#scale.scaleX, this.#scale.scaleY),
    );
  }

  pointerDown(point: Point): void {
    this.#assertActive();
    const physical = logicalPointToPhysical(point, this.#scale);
    const handle = this.#selection
      ? this.#hitHandle(physical, 6 * Math.max(this.#scale.scaleX, this.#scale.scaleY))
      : null;
    if (this.#selection && handle) {
      this.#drag = {
        kind: "resize",
        origin: point,
        current: point,
        initial: this.#selection,
        handle,
      };
    } else if (this.#selection && contains(this.#selection, physical)) {
      this.#drag = {
        kind: "move",
        origin: point,
        current: point,
        initial: this.#selection,
      };
    } else {
      this.#drag = { kind: "select", origin: point, current: point };
    }
  }

  pointerMove(point: Point): void {
    this.#assertActive();
    const initialPointerLogical = {
      x: this.#snapshot.pointerPhysical.x / this.#scale.scaleX,
      y: this.#snapshot.pointerPhysical.y / this.#scale.scaleY,
    };
    if (
      !this.#snapEnabled &&
      (point.x !== initialPointerLogical.x || point.y !== initialPointerLogical.y)
    ) {
      this.#snapEnabled = true;
    }
    if (this.#drag) {
      this.#drag.current = point;
      if (this.#drag.kind === "select" && movedEnough(this.#drag.origin, point)) {
        this.#snapCandidate = null;
        return;
      }
    }
    if (this.#snapEnabled && (!this.#drag || this.#drag.kind === "select")) {
      this.#snapCandidate = this.#candidateAt(logicalPointToPhysical(point, this.#scale));
    }
  }

  pointerUp(point: Point): void {
    this.#assertActive();
    if (!this.#drag) return;
    this.#drag.current = point;
    if (this.#drag.kind === "select") {
      if (movedEnough(this.#drag.origin, point)) {
        const rect = this.#clipRect(
          physicalRectFromGesture(this.#drag.origin, point, this.#scale),
        );
        this.#selection = validSelection(rect) ? rect : null;
      } else if (this.#snapEnabled) {
        this.#selection = this.#snapCandidate
          ? this.#clipRect(this.#snapCandidate.bounds)
          : {
              x: 0,
              y: 0,
              width: this.#snapshot.physicalSize.width,
              height: this.#snapshot.physicalSize.height,
            };
      }
    } else if (this.#drag.kind === "move") {
      this.#selection = this.#movePreview(this.#drag);
    } else {
      const resized = this.#resizePreview(this.#drag);
      this.#selection = validSelection(resized) ? resized : this.#drag.initial;
    }
    this.#drag = null;
    this.#snapCandidate = null;
  }

  clearSelection(): void {
    this.#assertActive();
    this.#selection = null;
    this.#drag = null;
    this.#snapCandidate = null;
  }

  snapshotState(): OverlayEditorState {
    const previewSelection = this.#previewSelection();
    return {
      selection: this.#selection,
      previewSelection,
      snapCandidate: this.#snapCandidate,
      snapEnabled: this.#snapEnabled,
      phase: this.#disposed
        ? "disposed"
        : this.#drag?.kind === "select"
          ? "selecting"
          : this.#drag?.kind === "move"
            ? "moving"
            : this.#drag?.kind === "resize"
              ? "resizing"
              : "idle",
    };
  }

  dispose(): void {
    this.#selection = null;
    this.#snapCandidate = null;
    this.#drag = null;
    this.#disposed = true;
  }

  #previewSelection(): Rect | null {
    if (!this.#drag) return this.#selection ?? this.#snapCandidate?.bounds ?? null;
    if (this.#drag.kind === "select") {
      return movedEnough(this.#drag.origin, this.#drag.current)
        ? this.#clipRect(
            physicalRectFromGesture(
              this.#drag.origin,
              this.#drag.current,
              this.#scale,
            ),
          )
        : this.#snapCandidate?.bounds ?? null;
    }
    return this.#drag.kind === "move"
      ? this.#movePreview(this.#drag)
      : this.#resizePreview(this.#drag);
  }

  #movePreview(drag: Extract<DragState, { kind: "move" }>): Rect {
    const deltaX = Math.round((drag.current.x - drag.origin.x) * this.#scale.scaleX);
    const deltaY = Math.round((drag.current.y - drag.origin.y) * this.#scale.scaleY);
    return {
      x: clamp(drag.initial.x + deltaX, 0, this.#snapshot.physicalSize.width - drag.initial.width),
      y: clamp(drag.initial.y + deltaY, 0, this.#snapshot.physicalSize.height - drag.initial.height),
      width: drag.initial.width,
      height: drag.initial.height,
    };
  }

  #resizePreview(drag: Extract<DragState, { kind: "resize" }>): Rect {
    return resizeRect(
      drag.initial,
      drag.handle,
      {
        x: Math.round((drag.current.x - drag.origin.x) * this.#scale.scaleX),
        y: Math.round((drag.current.y - drag.origin.y) * this.#scale.scaleY),
      },
      this.#snapshot.physicalSize,
    );
  }

  #candidateAt(point: Point): WindowCandidate | null {
    return (
      [...this.#snapshot.windows]
        .sort((left, right) => left.zOrder - right.zOrder)
        .find((candidate) => contains(candidate.bounds, point)) ?? null
    );
  }

  #clipRect(rect: Rect): Rect {
    const left = clamp(rect.x, 0, this.#snapshot.physicalSize.width);
    const top = clamp(rect.y, 0, this.#snapshot.physicalSize.height);
    const right = clamp(
      rect.x + rect.width,
      0,
      this.#snapshot.physicalSize.width,
    );
    const bottom = clamp(
      rect.y + rect.height,
      0,
      this.#snapshot.physicalSize.height,
    );
    return {
      x: left,
      y: top,
      width: Math.max(0, right - left),
      height: Math.max(0, bottom - top),
    };
  }

  #hitHandle(point: Point, radius: number): SelectionHandle["kind"] | null {
    if (!this.#selection) return null;
    return (
      selectionHandles(this.#selection).find(
        (handle) =>
          Math.abs(handle.point.x - point.x) <= radius &&
          Math.abs(handle.point.y - point.y) <= radius,
      )?.kind ?? null
    );
  }

  #assertActive(): void {
    if (this.#disposed) throw new Error("overlay editor is disposed");
  }
}

export function bgraToRgba(
  bgra: Uint8Array,
  stride: number,
  width: number,
  height: number,
): Uint8ClampedArray {
  if (bgra.byteLength !== stride * height || stride < width * 4) {
    throw new Error("invalid BGRA frame");
  }
  const rgba = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const source = y * stride + x * 4;
      const target = (y * width + x) * 4;
      const alpha = bgra[source + 3] ?? 0;
      const unpremultiply = (value: number): number =>
        alpha === 0 ? 0 : Math.min(255, Math.round((value * 255) / alpha));
      rgba[target] = unpremultiply(bgra[source + 2] ?? 0);
      rgba[target + 1] = unpremultiply(bgra[source + 1] ?? 0);
      rgba[target + 2] = unpremultiply(bgra[source] ?? 0);
      rgba[target + 3] = alpha;
    }
  }
  return rgba;
}

export function cropRgba(
  rgba: Uint8ClampedArray,
  sourceWidth: number,
  selection: Rect,
): Uint8ClampedArray {
  if (
    !Number.isSafeInteger(sourceWidth) ||
    sourceWidth <= 0 ||
    rgba.byteLength % (sourceWidth * 4) !== 0
  ) {
    throw new Error("invalid RGBA frame");
  }
  const sourceHeight = rgba.byteLength / (sourceWidth * 4);
  if (
    ![selection.x, selection.y, selection.width, selection.height].every(Number.isSafeInteger) ||
    selection.x < 0 ||
    selection.y < 0 ||
    selection.width <= 0 ||
    selection.height <= 0 ||
    selection.x + selection.width > sourceWidth ||
    selection.y + selection.height > sourceHeight
  ) {
    throw new Error("crop outside RGBA frame");
  }
  const cropped = new Uint8ClampedArray(selection.width * selection.height * 4);
  for (let y = 0; y < selection.height; y += 1) {
    const sourceStart = ((selection.y + y) * sourceWidth + selection.x) * 4;
    const targetStart = y * selection.width * 4;
    cropped.set(
      rgba.subarray(sourceStart, sourceStart + selection.width * 4),
      targetStart,
    );
  }
  return cropped;
}

const uiPointerRoles = new Set(["toolbar", "separator", "settings", "textarea", "size-label"]);

export type PointerRoute = "surface" | "ui" | "cancel";

export function routePointerPath(
  button: number,
  roles: readonly string[],
): PointerRoute {
  if (button === 2) return "cancel";
  return roles.some((role) => uiPointerRoles.has(role)) ? "ui" : "surface";
}

export interface OverlayEditorElements {
  readonly canvas: HTMLCanvasElement;
  readonly toolbar: HTMLElement;
  readonly sizeLabel: HTMLElement;
}

export interface OverlayEditorOptions {
  readonly onReady?: () => void;
  readonly onStateChange?: (state: OverlayEditorState) => void;
  readonly onAnnotationStateChange?: (state: AnnotationState) => void;
}

export class OverlayEditor {
  readonly #snapshot: CaptureSnapshot;
  readonly #elements: OverlayEditorElements;
  readonly #options: OverlayEditorOptions;
  readonly #model: OverlayEditorModel;
  readonly #annotations: AnnotationSession;
  #rgba: Uint8ClampedArray;
  #frameRequest: number | null = null;
  #disposed = false;
  #gestureOwner: "selection" | "annotation" | null = null;
  #frozenFloatingUi: FloatingUiProjection | null = null;

  constructor(
    snapshot: CaptureSnapshot,
    binary: ArrayBuffer,
    elements: OverlayEditorElements,
    options: OverlayEditorOptions = {},
  ) {
    const scale = validateSnapshot(snapshot, binary.byteLength);
    this.#snapshot = snapshot;
    this.#elements = elements;
    this.#options = options;
    this.#model = new OverlayEditorModel(snapshot);
    this.#annotations = new AnnotationSession({
      selection: {
        x: 0,
        y: 0,
        width: snapshot.physicalSize.width,
        height: snapshot.physicalSize.height,
      },
      scale,
    });
    const source = new Uint8Array(binary);
    this.#rgba = bgraToRgba(
      source,
      snapshot.stride,
      snapshot.physicalSize.width,
      snapshot.physicalSize.height,
    );
    source.fill(0);
    elements.canvas.width = snapshot.physicalSize.width;
    elements.canvas.height = snapshot.physicalSize.height;
    elements.canvas.style.width = `${snapshot.logicalSize.width}px`;
    elements.canvas.style.height = `${snapshot.logicalSize.height}px`;
    elements.canvas.dataset.scaleX = String(scale.scaleX);
    elements.canvas.dataset.scaleY = String(scale.scaleY);
    this.render();
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        if (!this.#disposed) this.#options.onReady?.();
      });
    });
  }

  get model(): OverlayEditorModel {
    return this.#model;
  }

  get annotations(): AnnotationSession {
    return this.#annotations;
  }

  setTool(tool: AnnotationTool, openSettings = false): void {
    this.#annotations.setTool(tool, openSettings);
    this.#annotationStateChanged();
  }

  setAnnotationStyle(style: Partial<AnnotationStyle>): void {
    this.#annotations.setStyle(style);
    this.#annotationStateChanged();
  }

  undoAnnotation(): boolean {
    const changed = this.#annotations.undo();
    if (changed) this.#annotationStateChanged();
    return changed;
  }

  redoAnnotation(): boolean {
    const changed = this.#annotations.redo();
    if (changed) this.#annotationStateChanged();
    return changed;
  }

  deleteSelectedAnnotation(): boolean {
    const changed = this.#annotations.deleteSelected();
    if (changed) this.#annotationStateChanged();
    return changed;
  }

  cursorAt(point: Point): string {
    return this.#annotations.cursorAt(
      logicalPointToPhysical(point, this.#model.scale),
    );
  }

  pointerDown(point: Point): void {
    const state = this.#model.snapshotState();
    const selection = state.selection;
    const physical = logicalPointToPhysical(point, this.#model.scale);
    const annotationState = this.#annotations.snapshotState();
    if (selection && contains(selection, physical)) {
      const selectionHandle = this.#model.selectionHandleAt(point);
      if (!selectionHandle && this.#annotations.pointerDown(physical)) {
        this.#gestureOwner = "annotation";
        this.#annotationStateChanged();
        return;
      }
      if (!selectionHandle && annotationState.everEdited) {
        this.#gestureOwner = null;
        this.#annotationStateChanged();
        return;
      }
    } else if (selection) {
      this.#annotations.clear();
      this.#frozenFloatingUi = null;
    }
    this.#gestureOwner = "selection";
    this.#model.pointerDown(point);
    this.#stateChanged();
  }

  pointerMove(point: Point): void {
    if (this.#gestureOwner === "annotation") {
      this.#annotations.pointerMove(logicalPointToPhysical(point, this.#model.scale));
      this.#annotationStateChanged();
    } else {
      this.#model.pointerMove(point);
      this.#stateChanged();
    }
  }

  pointerUp(point: Point): void {
    if (this.#gestureOwner === "annotation") {
      this.#annotations.pointerUp(logicalPointToPhysical(point, this.#model.scale));
      this.#gestureOwner = null;
      this.#annotationStateChanged();
      return;
    }
    if (this.#gestureOwner === "selection") {
      this.#model.pointerUp(point);
      const selection = this.#model.snapshotState().selection;
      if (selection) this.#annotations.setSelection(selection);
    }
    this.#gestureOwner = null;
    this.#stateChanged();
  }

  render(): void {
    if (this.#disposed) return;
    const context = this.#elements.canvas.getContext("2d", { alpha: false });
    if (!context) throw new Error("2D canvas unavailable");
    const width = this.#snapshot.physicalSize.width;
    const height = this.#snapshot.physicalSize.height;
    context.setTransform(1, 0, 0, 1, 0, 0);
    context.putImageData(new ImageData(this.#rgba, width, height), 0, 0);

    const state = this.#model.snapshotState();
    const selection = state.previewSelection;
    if (!selection) {
      this.#elements.toolbar.style.visibility = "hidden";
      this.#elements.sizeLabel.style.visibility = "hidden";
      return;
    }
    context.fillStyle = screenshotUiTheme.colors.overlayMask;
    context.fillRect(0, 0, width, selection.y);
    context.fillRect(0, selection.y, selection.x, selection.height);
    context.fillRect(
      selection.x + selection.width,
      selection.y,
      width - selection.x - selection.width,
      selection.height,
    );
    context.fillRect(
      0,
      selection.y + selection.height,
      width,
      height - selection.y - selection.height,
    );
    if (state.selection) {
      context.save();
      context.beginPath();
      context.rect(
        state.selection.x,
        state.selection.y,
        state.selection.width,
        state.selection.height,
      );
      context.clip();
      renderAnnotations(context, this.#annotations.renderPlan(), {
        scale: this.#model.scale,
        showSelection: true,
      });
      context.restore();
    }
    context.save();
    context.strokeStyle = screenshotUiTheme.colors.brand;
    context.lineWidth =
      screenshotUiTheme.selection.outlineWidth *
      Math.min(this.#model.scale.scaleX, this.#model.scale.scaleY);
    context.strokeRect(
      selection.x,
      selection.y,
      selection.width,
      selection.height,
    );
    context.fillStyle = screenshotUiTheme.colors.surface;
    const handleSize =
      screenshotUiTheme.selection.handleSize *
      Math.min(this.#model.scale.scaleX, this.#model.scale.scaleY);
    for (const handle of selectionHandles(selection)) {
      context.fillRect(
        handle.point.x - handleSize / 2,
        handle.point.y - handleSize / 2,
        handleSize,
        handleSize,
      );
      context.strokeRect(
        handle.point.x - handleSize / 2,
        handle.point.y - handleSize / 2,
        handleSize,
        handleSize,
      );
    }
    context.restore();
    this.#projectFloatingUi(selection);
  }

  async composePng(): Promise<Uint8Array> {
    this.#assertActive();
    const selection = this.#model.snapshotState().selection;
    if (!selection) throw new Error("selection required");
    const pixels = cropRgba(
      this.#rgba,
      this.#snapshot.physicalSize.width,
      selection,
    );
    const output = document.createElement("canvas");
    output.width = selection.width;
    output.height = selection.height;
    const context = output.getContext("2d", { alpha: true });
    if (!context) throw new Error("2D canvas unavailable");
    context.putImageData(new ImageData(pixels, selection.width, selection.height), 0, 0);
    renderAnnotations(context, this.#annotations.renderPlan(), {
      scale: this.#model.scale,
      offset: { x: selection.x, y: selection.y },
      showSelection: false,
    });
    const blob = await new Promise<Blob>((resolve, reject) => {
      output.toBlob(
        (value) => (value ? resolve(value) : reject(new Error("PNG encoding failed"))),
        "image/png",
      );
    });
    pixels.fill(0);
    return new Uint8Array(await blob.arrayBuffer());
  }

  dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;
    if (this.#frameRequest !== null) cancelAnimationFrame(this.#frameRequest);
    this.#frameRequest = null;
    this.#rgba.fill(0);
    this.#rgba = new Uint8ClampedArray();
    this.#elements.canvas.width = 0;
    this.#elements.canvas.height = 0;
    this.#model.dispose();
    this.#annotations.clear();
    this.#gestureOwner = null;
    this.#frozenFloatingUi = null;
  }

  #stateChanged(): void {
    const state = this.#model.snapshotState();
    this.#options.onStateChange?.(state);
    if (this.#frameRequest === null) {
      this.#frameRequest = requestAnimationFrame(() => {
        this.#frameRequest = null;
        this.render();
      });
    }
  }

  #annotationStateChanged(): void {
    const state = this.#annotations.snapshotState();
    this.#options.onAnnotationStateChange?.(state);
    if (this.#frameRequest === null) {
      this.#frameRequest = requestAnimationFrame(() => {
        this.#frameRequest = null;
        this.render();
      });
    }
  }

  #projectFloatingUi(selection: Rect): FloatingUiProjection {
    const logicalSelection = logicalRect(selection, this.#model.scale);
    const annotationState = this.#annotations.snapshotState();
    const projection =
      annotationState.everEdited && this.#frozenFloatingUi
        ? this.#frozenFloatingUi
        : projectFloatingUi(
            logicalSelection,
            this.#snapshot.workAreaLogical,
            this.#elements.sizeLabel.offsetWidth || 92,
          );
    if (annotationState.everEdited && !this.#frozenFloatingUi) {
      this.#frozenFloatingUi = projection;
    }
    const toolbar = this.#elements.toolbar.style;
    toolbar.left = `${projection.toolbar.x}px`;
    toolbar.top = `${projection.toolbar.y}px`;
    toolbar.visibility = "visible";
    this.#elements.toolbar.dataset.placement = projection.toolbar.placement;
    this.#elements.sizeLabel.textContent = `${selection.width} × ${selection.height}`;
    const label = this.#elements.sizeLabel.style;
    label.left = `${projection.sizeLabel.x}px`;
    label.top = `${projection.sizeLabel.y}px`;
    label.visibility = "visible";
    this.#elements.sizeLabel.dataset.placement = projection.sizeLabel.placement;
    return projection;
  }

  #assertActive(): void {
    if (this.#disposed) throw new Error("overlay editor is disposed");
  }
}
