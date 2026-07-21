import {
  screenshotUiTheme,
  type Point,
  type Rect,
} from "@snaploom/screenshot-ui";
import type { SnapshotScale } from "./app";

export const ANNOTATION_COLORS = [
  "#FF4D4F",
  "#FADB14",
  "#07C977",
  "#1677FF",
  "#202124",
  "#FFFFFF",
] as const;

export const ANNOTATION_STROKE_WIDTHS = [2, 4, 8] as const;

export type AnnotationColor = (typeof ANNOTATION_COLORS)[number];
export type AnnotationStrokeWidth = (typeof ANNOTATION_STROKE_WIDTHS)[number];
export type AnnotationTool = "select" | "rectangle" | "arrow" | "text" | "mosaic";
export type AnnotationCursor =
  | "default"
  | "crosshair"
  | "text"
  | "move"
  | "ew-resize"
  | "ns-resize"
  | "nwse-resize"
  | "nesw-resize"
  | "grab";

export interface AnnotationStyle {
  readonly color: AnnotationColor;
  readonly strokeWidth: AnnotationStrokeWidth;
}

export interface RectangleAnnotation {
  readonly id: string;
  readonly kind: "rectangle";
  readonly rect: Rect;
  readonly style: AnnotationStyle;
}

export interface ArrowAnnotation {
  readonly id: string;
  readonly kind: "arrow";
  readonly start: Point;
  readonly end: Point;
  readonly style: AnnotationStyle;
}

export type AnnotationObject = RectangleAnnotation | ArrowAnnotation;

export interface AnnotationRenderPlan {
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
}

export interface AnnotationState {
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
  readonly tool: AnnotationTool;
  readonly style: AnnotationStyle;
  readonly settingsOpen: "rectangle" | "arrow" | null;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
  readonly everEdited: boolean;
  readonly gestureActive: boolean;
}

export interface ShortcutInput {
  readonly key: string;
  readonly metaKey?: boolean;
  readonly controlKey?: boolean;
  readonly ctrlKey?: boolean;
  readonly altKey?: boolean;
  readonly isComposing?: boolean;
}

interface HistoryEntry {
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
}

type ObjectHandle =
  | "nw"
  | "n"
  | "ne"
  | "e"
  | "se"
  | "s"
  | "sw"
  | "w"
  | "start"
  | "end";

type AnnotationGesture =
  | {
      readonly kind: "draw-rectangle" | "draw-arrow";
      readonly origin: Point;
      current: Point;
      readonly before: HistoryEntry;
    }
  | {
      readonly kind: "move";
      readonly origin: Point;
      current: Point;
      readonly initial: AnnotationObject;
      readonly before: HistoryEntry;
    }
  | {
      readonly kind: "resize";
      readonly origin: Point;
      current: Point;
      readonly initial: AnnotationObject;
      readonly handle: ObjectHandle;
      readonly before: HistoryEntry;
    }
  | { readonly kind: "select-existing" };

function clonePoint(point: Point): Point {
  return { x: point.x, y: point.y };
}

function cloneStyle(style: AnnotationStyle): AnnotationStyle {
  return { color: style.color, strokeWidth: style.strokeWidth };
}

function cloneObject(object: AnnotationObject): AnnotationObject {
  if (object.kind === "rectangle") {
    return {
      id: object.id,
      kind: "rectangle",
      rect: { ...object.rect },
      style: cloneStyle(object.style),
    };
  }
  return {
    id: object.id,
    kind: "arrow",
    start: clonePoint(object.start),
    end: clonePoint(object.end),
    style: cloneStyle(object.style),
  };
}

function cloneObjects(objects: readonly AnnotationObject[]): AnnotationObject[] {
  return objects.map(cloneObject);
}

function distanceToSegment(point: Point, start: Point, end: Point): number {
  const deltaX = end.x - start.x;
  const deltaY = end.y - start.y;
  const lengthSquared = deltaX * deltaX + deltaY * deltaY;
  if (lengthSquared === 0) return Math.hypot(point.x - start.x, point.y - start.y);
  const projection = Math.min(
    1,
    Math.max(
      0,
      ((point.x - start.x) * deltaX + (point.y - start.y) * deltaY) /
        lengthSquared,
    ),
  );
  return Math.hypot(
    point.x - (start.x + projection * deltaX),
    point.y - (start.y + projection * deltaY),
  );
}

function contains(rect: Rect, point: Point): boolean {
  return (
    point.x >= rect.x &&
    point.y >= rect.y &&
    point.x <= rect.x + rect.width &&
    point.y <= rect.y + rect.height
  );
}

function normalizedRect(start: Point, end: Point): Rect {
  return {
    x: Math.min(start.x, end.x),
    y: Math.min(start.y, end.y),
    width: Math.abs(end.x - start.x),
    height: Math.abs(end.y - start.y),
  };
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}

function clipPoint(point: Point, selection: Rect): Point {
  return {
    x: clamp(point.x, selection.x, selection.x + selection.width),
    y: clamp(point.y, selection.y, selection.y + selection.height),
  };
}

export function annotationShortcut(input: ShortcutInput): AnnotationTool | null {
  if (
    input.metaKey ||
    input.controlKey ||
    input.ctrlKey ||
    input.altKey ||
    input.isComposing
  ) {
    return null;
  }
  switch (input.key.toLowerCase()) {
    case "r":
      return "rectangle";
    case "a":
      return "arrow";
    case "t":
      return "text";
    case "m":
      return "mosaic";
    case "v":
      return "select";
    default:
      return null;
  }
}

export function rectangleAnnotationHandles(rect: Rect): Readonly<Record<ObjectHandle, Point>> {
  const centerX = rect.x + rect.width / 2;
  const centerY = rect.y + rect.height / 2;
  return {
    nw: { x: rect.x, y: rect.y },
    n: { x: centerX, y: rect.y },
    ne: { x: rect.x + rect.width, y: rect.y },
    e: { x: rect.x + rect.width, y: centerY },
    se: { x: rect.x + rect.width, y: rect.y + rect.height },
    s: { x: centerX, y: rect.y + rect.height },
    sw: { x: rect.x, y: rect.y + rect.height },
    w: { x: rect.x, y: centerY },
    start: { x: rect.x, y: rect.y },
    end: { x: rect.x + rect.width, y: rect.y + rect.height },
  };
}

function objectBounds(object: AnnotationObject): Rect {
  return object.kind === "rectangle"
    ? object.rect
    : normalizedRect(object.start, object.end);
}

export class AnnotationSession {
  #selection: Rect;
  readonly #scale: SnapshotScale;
  #objects: AnnotationObject[] = [];
  #selectedId: string | null = null;
  #tool: AnnotationTool = "select";
  #style: AnnotationStyle = { color: "#FF4D4F", strokeWidth: 4 };
  #settingsOpen: "rectangle" | "arrow" | null = null;
  #undo: HistoryEntry[] = [];
  #redo: HistoryEntry[] = [];
  #gesture: AnnotationGesture | null = null;
  #everEdited = false;
  #nextId = 1;

  constructor(options: { readonly selection: Rect; readonly scale: SnapshotScale }) {
    this.#selection = { ...options.selection };
    this.#scale = options.scale;
  }

  setTool(tool: AnnotationTool, openSettings: boolean): void {
    this.#tool = tool;
    if (tool !== "select") this.#selectedId = null;
    this.#settingsOpen =
      openSettings && (tool === "rectangle" || tool === "arrow") ? tool : null;
  }

  setSelection(selection: Rect, replace = false): void {
    this.#selection = { ...selection };
    if (replace) this.clear();
  }

  closeSettings(): void {
    this.#settingsOpen = null;
  }

  cursorAt(point: Point): AnnotationCursor {
    const handle = this.#hitSelectedHandle(point);
    if (handle === "e" || handle === "w") return "ew-resize";
    if (handle === "n" || handle === "s") return "ns-resize";
    if (handle === "nw" || handle === "se") return "nwse-resize";
    if (handle === "ne" || handle === "sw") return "nesw-resize";
    if (handle === "start" || handle === "end") return "grab";
    if (this.hitTest(point, this.#tool === "rectangle" || this.#tool === "arrow" ? this.#tool : undefined)) {
      return "move";
    }
    if (this.#tool === "rectangle" || this.#tool === "arrow" || this.#tool === "mosaic") {
      return "crosshair";
    }
    return this.#tool === "text" ? "text" : "default";
  }

  setStyle(style: Partial<AnnotationStyle>): void {
    const next: AnnotationStyle = {
      color: style.color ?? this.#style.color,
      strokeWidth: style.strokeWidth ?? this.#style.strokeWidth,
    };
    if (!ANNOTATION_COLORS.includes(next.color)) throw new Error("invalid annotation color");
    if (!ANNOTATION_STROKE_WIDTHS.includes(next.strokeWidth)) {
      throw new Error("invalid annotation stroke width");
    }
    this.#style = next;
    const index = this.#selectedIndex();
    if (index < 0) return;
    const selected = this.#objects[index];
    if (!selected || selected.style.color === next.color && selected.style.strokeWidth === next.strokeWidth) {
      return;
    }
    const before = this.#historyEntry();
    this.#objects[index] = { ...selected, style: cloneStyle(next) };
    this.#commit(before);
  }

  pointerDown(point: Point): boolean {
    const clipped = clipPoint(point, this.#selection);
    if (this.#tool === "rectangle" || this.#tool === "arrow") {
      const existing = this.hitTest(clipped, this.#tool);
      if (existing) {
        this.#selectedId = existing.id;
        this.#tool = "select";
        this.#settingsOpen = existing.kind;
        this.#gesture = { kind: "select-existing" };
        return true;
      }
      this.#selectedId = null;
      this.#gesture = {
        kind: this.#tool === "rectangle" ? "draw-rectangle" : "draw-arrow",
        origin: clipped,
        current: clipped,
        before: this.#historyEntry(),
      };
      return true;
    }
    if (this.#tool !== "select") return false;

    const handle = this.#hitSelectedHandle(clipped);
    const selected = this.#selectedObject();
    if (handle && selected) {
      this.#gesture = {
        kind: "resize",
        origin: clipped,
        current: clipped,
        initial: cloneObject(selected),
        handle,
        before: this.#historyEntry(),
      };
      return true;
    }

    const hit = this.hitTest(clipped);
    if (!hit) {
      this.#selectedId = null;
      this.#settingsOpen = null;
      return false;
    }
    this.#selectedId = hit.id;
    this.#settingsOpen = hit.kind;
    this.#gesture = {
      kind: "move",
      origin: clipped,
      current: clipped,
      initial: cloneObject(hit),
      before: this.#historyEntry(),
    };
    return true;
  }

  pointerMove(point: Point): void {
    if (!this.#gesture || this.#gesture.kind === "select-existing") return;
    this.#gesture.current = clipPoint(point, this.#selection);
    if (this.#gesture.kind === "move") {
      this.#replaceObject(this.#movePreview(this.#gesture));
    } else if (this.#gesture.kind === "resize") {
      this.#replaceObject(this.#resizePreview(this.#gesture));
    }
  }

  pointerUp(point: Point): void {
    if (!this.#gesture) return;
    if (this.#gesture.kind === "select-existing") {
      this.#gesture = null;
      return;
    }
    this.pointerMove(point);
    const gesture = this.#gesture;
    if (gesture.kind === "draw-rectangle") {
      const rect = normalizedRect(gesture.origin, gesture.current);
      if (rect.width > 0 && rect.height > 0) {
        const object: RectangleAnnotation = {
          id: this.#createId(),
          kind: "rectangle",
          rect,
          style: cloneStyle(this.#style),
        };
        this.#objects.push(object);
        this.#selectedId = object.id;
        this.#commit(gesture.before);
      }
    } else if (gesture.kind === "draw-arrow") {
      if (
        gesture.origin.x !== gesture.current.x ||
        gesture.origin.y !== gesture.current.y
      ) {
        const object: ArrowAnnotation = {
          id: this.#createId(),
          kind: "arrow",
          start: clonePoint(gesture.origin),
          end: clonePoint(gesture.current),
          style: cloneStyle(this.#style),
        };
        this.#objects.push(object);
        this.#selectedId = object.id;
        this.#commit(gesture.before);
      }
    } else if (this.#objectsChanged(gesture.before.objects)) {
      this.#commit(gesture.before);
    }
    this.#gesture = null;
  }

  cancelGesture(): void {
    if (
      this.#gesture &&
      (this.#gesture.kind === "move" || this.#gesture.kind === "resize")
    ) {
      this.#restore(this.#gesture.before);
    }
    this.#gesture = null;
  }

  hitTest(point: Point, kind?: AnnotationObject["kind"]): AnnotationObject | null {
    const tolerance = 5 * Math.max(this.#scale.scaleX, this.#scale.scaleY);
    for (let index = this.#objects.length - 1; index >= 0; index -= 1) {
      const object = this.#objects[index];
      if (!object || kind && object.kind !== kind) continue;
      const stroke = object.style.strokeWidth * Math.max(this.#scale.scaleX, this.#scale.scaleY) / 2;
      if (object.kind === "arrow") {
        if (distanceToSegment(point, object.start, object.end) <= Math.max(tolerance, stroke)) {
          return object;
        }
      } else {
        const outer = {
          x: object.rect.x - Math.max(tolerance, stroke),
          y: object.rect.y - Math.max(tolerance, stroke),
          width: object.rect.width + Math.max(tolerance, stroke) * 2,
          height: object.rect.height + Math.max(tolerance, stroke) * 2,
        };
        const inset = Math.max(tolerance, stroke);
        const inner = {
          x: object.rect.x + inset,
          y: object.rect.y + inset,
          width: Math.max(0, object.rect.width - inset * 2),
          height: Math.max(0, object.rect.height - inset * 2),
        };
        if (contains(outer, point) && (!contains(inner, point) || inner.width === 0 || inner.height === 0)) {
          return object;
        }
      }
    }
    return null;
  }

  deleteSelected(): boolean {
    if (this.#gesture) return false;
    const index = this.#selectedIndex();
    if (index < 0) return false;
    const before = this.#historyEntry();
    this.#objects.splice(index, 1);
    this.#selectedId = null;
    this.#settingsOpen = null;
    this.#commit(before);
    return true;
  }

  undo(): boolean {
    if (this.#gesture) return false;
    const previous = this.#undo.pop();
    if (!previous) return false;
    this.#redo.push(this.#historyEntry());
    this.#restore(previous);
    return true;
  }

  redo(): boolean {
    if (this.#gesture) return false;
    const next = this.#redo.pop();
    if (!next) return false;
    this.#undo.push(this.#historyEntry());
    this.#restore(next);
    return true;
  }

  clear(): void {
    this.#objects = [];
    this.#selectedId = null;
    this.#undo = [];
    this.#redo = [];
    this.#gesture = null;
    this.#everEdited = false;
    this.#settingsOpen = null;
  }

  snapshotState(): AnnotationState {
    return {
      objects: cloneObjects(this.#objects),
      selectedId: this.#selectedId,
      tool: this.#tool,
      style: cloneStyle(this.#style),
      settingsOpen: this.#settingsOpen,
      canUndo: this.#undo.length > 0,
      canRedo: this.#redo.length > 0,
      everEdited: this.#everEdited,
      gestureActive: this.#gesture !== null,
    };
  }

  renderPlan(): AnnotationRenderPlan {
    return buildAnnotationRenderPlan(this.snapshotState(), this.#gesture, this.#style, this.#nextId);
  }

  #createId(): string {
    const id = `annotation-${this.#nextId}`;
    this.#nextId += 1;
    return id;
  }

  #historyEntry(): HistoryEntry {
    return { objects: cloneObjects(this.#objects), selectedId: this.#selectedId };
  }

  #commit(before: HistoryEntry): void {
    this.#undo.push(before);
    this.#redo = [];
    this.#everEdited = true;
  }

  #restore(entry: HistoryEntry): void {
    this.#objects = cloneObjects(entry.objects);
    this.#selectedId = entry.selectedId;
  }

  #objectsChanged(before: readonly AnnotationObject[]): boolean {
    return JSON.stringify(before) !== JSON.stringify(this.#objects);
  }

  #selectedIndex(): number {
    return this.#selectedId
      ? this.#objects.findIndex((object) => object.id === this.#selectedId)
      : -1;
  }

  #selectedObject(): AnnotationObject | null {
    const index = this.#selectedIndex();
    return index < 0 ? null : this.#objects[index] ?? null;
  }

  #replaceObject(object: AnnotationObject): void {
    const index = this.#objects.findIndex((candidate) => candidate.id === object.id);
    if (index >= 0) this.#objects[index] = object;
  }

  #hitSelectedHandle(point: Point): ObjectHandle | null {
    const selected = this.#selectedObject();
    if (!selected) return null;
    const radius = 6 * Math.max(this.#scale.scaleX, this.#scale.scaleY);
    const handles: readonly [ObjectHandle, Point][] =
      selected.kind === "rectangle"
        ? (Object.entries(rectangleAnnotationHandles(selected.rect)).filter(
            ([kind]) => kind !== "start" && kind !== "end",
          ) as [ObjectHandle, Point][])
        : [
            ["start", selected.start],
            ["end", selected.end],
          ];
    return (
      handles.find(
        ([, handle]) =>
          Math.abs(handle.x - point.x) <= radius &&
          Math.abs(handle.y - point.y) <= radius,
      )?.[0] ?? null
    );
  }

  #movePreview(gesture: Extract<AnnotationGesture, { kind: "move" }>): AnnotationObject {
    const delta = {
      x: gesture.current.x - gesture.origin.x,
      y: gesture.current.y - gesture.origin.y,
    };
    const bounds = objectBounds(gesture.initial);
    const boundedDelta = {
      x: clamp(
        delta.x,
        this.#selection.x - bounds.x,
        this.#selection.x + this.#selection.width - bounds.x - bounds.width,
      ),
      y: clamp(
        delta.y,
        this.#selection.y - bounds.y,
        this.#selection.y + this.#selection.height - bounds.y - bounds.height,
      ),
    };
    if (gesture.initial.kind === "rectangle") {
      return {
        ...gesture.initial,
        rect: {
          ...gesture.initial.rect,
          x: gesture.initial.rect.x + boundedDelta.x,
          y: gesture.initial.rect.y + boundedDelta.y,
        },
      };
    }
    return {
      ...gesture.initial,
      start: {
        x: gesture.initial.start.x + boundedDelta.x,
        y: gesture.initial.start.y + boundedDelta.y,
      },
      end: {
        x: gesture.initial.end.x + boundedDelta.x,
        y: gesture.initial.end.y + boundedDelta.y,
      },
    };
  }

  #resizePreview(
    gesture: Extract<AnnotationGesture, { kind: "resize" }>,
  ): AnnotationObject {
    if (gesture.initial.kind === "arrow") {
      return gesture.handle === "start"
        ? { ...gesture.initial, start: clonePoint(gesture.current) }
        : { ...gesture.initial, end: clonePoint(gesture.current) };
    }
    let left = gesture.initial.rect.x;
    let top = gesture.initial.rect.y;
    let right = left + gesture.initial.rect.width;
    let bottom = top + gesture.initial.rect.height;
    const deltaX = gesture.current.x - gesture.origin.x;
    const deltaY = gesture.current.y - gesture.origin.y;
    if (gesture.handle.includes("w")) left += deltaX;
    if (gesture.handle.includes("e")) right += deltaX;
    if (gesture.handle.includes("n")) top += deltaY;
    if (gesture.handle.includes("s")) bottom += deltaY;
    const rect = normalizedRect(
      clipPoint({ x: left, y: top }, this.#selection),
      clipPoint({ x: right, y: bottom }, this.#selection),
    );
    if (
      rect.width < screenshotUiTheme.selection.minimumPhysicalSize ||
      rect.height < screenshotUiTheme.selection.minimumPhysicalSize
    ) {
      return gesture.initial;
    }
    return { ...gesture.initial, rect };
  }
}

export function buildAnnotationRenderPlan(
  state: AnnotationState,
  gesture: AnnotationGesture | null = null,
  style: AnnotationStyle = state.style,
  previewId = Number.MAX_SAFE_INTEGER,
): AnnotationRenderPlan {
  const objects = cloneObjects(state.objects);
  if (gesture?.kind === "draw-rectangle") {
    const rect = normalizedRect(gesture.origin, gesture.current);
    if (rect.width > 0 && rect.height > 0) {
      objects.push({
        id: `preview-${previewId}`,
        kind: "rectangle",
        rect,
        style: cloneStyle(style),
      });
    }
  } else if (
    gesture?.kind === "draw-arrow" &&
    (gesture.origin.x !== gesture.current.x || gesture.origin.y !== gesture.current.y)
  ) {
    objects.push({
      id: `preview-${previewId}`,
      kind: "arrow",
      start: clonePoint(gesture.origin),
      end: clonePoint(gesture.current),
      style: cloneStyle(style),
    });
  }
  return { objects, selectedId: state.selectedId };
}

export interface RenderAnnotationsOptions {
  readonly scale: SnapshotScale;
  readonly offset?: Point;
  readonly showSelection?: boolean;
}

export function renderAnnotations(
  context: CanvasRenderingContext2D,
  plan: AnnotationRenderPlan,
  options: RenderAnnotationsOptions,
): void {
  const offset = options.offset ?? { x: 0, y: 0 };
  const scale = Math.min(options.scale.scaleX, options.scale.scaleY);
  for (const object of plan.objects) {
    context.save();
    context.translate(-offset.x, -offset.y);
    context.strokeStyle = object.style.color;
    context.fillStyle = object.style.color;
    context.lineWidth = object.style.strokeWidth * scale;
    context.lineCap = "round";
    context.lineJoin = "round";
    if (object.kind === "rectangle") {
      context.strokeRect(object.rect.x, object.rect.y, object.rect.width, object.rect.height);
    } else {
      const angle = Math.atan2(
        object.end.y - object.start.y,
        object.end.x - object.start.x,
      );
      const head = Math.max(10 * scale, context.lineWidth * 2.5);
      context.beginPath();
      context.moveTo(object.start.x, object.start.y);
      context.lineTo(object.end.x, object.end.y);
      context.lineTo(
        object.end.x - head * Math.cos(angle - Math.PI / 6),
        object.end.y - head * Math.sin(angle - Math.PI / 6),
      );
      context.moveTo(object.end.x, object.end.y);
      context.lineTo(
        object.end.x - head * Math.cos(angle + Math.PI / 6),
        object.end.y - head * Math.sin(angle + Math.PI / 6),
      );
      context.stroke();
    }
    context.restore();
  }

  if (!options.showSelection || !plan.selectedId) return;
  const selected = plan.objects.find((object) => object.id === plan.selectedId);
  if (!selected) return;
  const handleSize = 7 * scale;
  const handles =
    selected.kind === "rectangle"
      ? Object.entries(rectangleAnnotationHandles(selected.rect))
          .filter(([kind]) => kind !== "start" && kind !== "end")
          .map(([, point]) => point)
      : [selected.start, selected.end];
  context.save();
  context.translate(-offset.x, -offset.y);
  context.fillStyle = screenshotUiTheme.colors.surface;
  context.strokeStyle = screenshotUiTheme.colors.brand;
  context.lineWidth = Math.max(1, scale);
  for (const handle of handles) {
    context.fillRect(
      handle.x - handleSize / 2,
      handle.y - handleSize / 2,
      handleSize,
      handleSize,
    );
    context.strokeRect(
      handle.x - handleSize / 2,
      handle.y - handleSize / 2,
      handleSize,
      handleSize,
    );
  }
  context.restore();
}
