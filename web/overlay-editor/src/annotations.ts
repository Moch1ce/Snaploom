import {
  screenshotUiTheme,
  type Point,
  type Rect,
} from "@snaploom/screenshot-ui";
import type { SnapshotScale } from "./app";

export const ANNOTATION_COLORS = screenshotUiTheme.annotation.colors;
export const ANNOTATION_STROKE_WIDTHS = screenshotUiTheme.annotation.strokeWidths;
export const ANNOTATION_FONT_SIZES = screenshotUiTheme.annotation.fontSizes;
export const MOSAIC_BRUSH_SIZES = screenshotUiTheme.annotation.mosaicBrushSizes;
export const MOSAIC_BLOCK_SIZES = screenshotUiTheme.annotation.mosaicBlockSizes;
export const MOSAIC_TILE_SIZE = screenshotUiTheme.annotation.mosaicTileSize;

export type AnnotationColor = (typeof ANNOTATION_COLORS)[number];
export type AnnotationStrokeWidth = (typeof ANNOTATION_STROKE_WIDTHS)[number];
export type AnnotationFontSize = (typeof ANNOTATION_FONT_SIZES)[number];
export type MosaicBrushSize = (typeof MOSAIC_BRUSH_SIZES)[number];
export type MosaicBlockSize = (typeof MOSAIC_BLOCK_SIZES)[number];
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
  readonly fontSize: AnnotationFontSize;
  readonly mosaicBrushSize: MosaicBrushSize;
  readonly mosaicBlockSize: MosaicBlockSize;
}

export const DEFAULT_ANNOTATION_STYLE: AnnotationStyle = {
  color: ANNOTATION_COLORS[0],
  strokeWidth: ANNOTATION_STROKE_WIDTHS[1],
  fontSize: ANNOTATION_FONT_SIZES[1],
  mosaicBrushSize: MOSAIC_BRUSH_SIZES[1],
  mosaicBlockSize: MOSAIC_BLOCK_SIZES[1],
};

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

export interface TextAnnotation {
  readonly id: string;
  readonly kind: "text";
  readonly origin: Point;
  readonly text: string;
  readonly maxWidth: number;
  readonly style: AnnotationStyle;
}

export interface MosaicAnnotation {
  readonly id: string;
  readonly kind: "mosaic";
  readonly points: readonly Point[];
  readonly style: AnnotationStyle;
}

export type AnnotationObject =
  | RectangleAnnotation
  | ArrowAnnotation
  | TextAnnotation
  | MosaicAnnotation;

export interface TextDraft {
  readonly editingId: string | null;
  readonly origin: Point;
  readonly value: string;
  readonly preedit: string;
  readonly isComposing: boolean;
  readonly style: AnnotationStyle;
}

export interface TextLayout {
  readonly lines: readonly string[];
  readonly bounds: Rect;
  readonly fontSizePhysical: number;
  readonly lineHeight: number;
}

export interface AnnotationRenderPlan {
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
}

export interface AnnotationState {
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
  readonly tool: AnnotationTool;
  readonly style: AnnotationStyle;
  readonly settingsOpen: "rectangle" | "arrow" | "text" | "mosaic" | null;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
  readonly everEdited: boolean;
  readonly gestureActive: boolean;
  readonly textDraft: TextDraft | null;
}

interface AnnotationCheckpointHistory {
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
}

/** Editor transaction snapshot used only to roll back failed native output. */
export interface AnnotationCheckpoint {
  readonly selection: Rect;
  readonly objects: readonly AnnotationObject[];
  readonly selectedId: string | null;
  readonly tool: AnnotationTool;
  readonly style: AnnotationStyle;
  readonly settingsOpen: "rectangle" | "arrow" | "text" | "mosaic" | null;
  readonly undo: readonly AnnotationCheckpointHistory[];
  readonly redo: readonly AnnotationCheckpointHistory[];
  readonly textDraft: TextDraft | null;
  readonly textDraftBefore: AnnotationCheckpointHistory | null;
  readonly everEdited: boolean;
  readonly nextId: number;
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
      readonly kind: "draw-mosaic";
      readonly origin: Point;
      current: Point;
      readonly points: Point[];
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
  | {
      readonly kind: "text-press";
      readonly origin: Point;
      current: Point;
      readonly initial: TextAnnotation;
      readonly before: HistoryEntry;
    }
  | { readonly kind: "select-existing" };

function clonePoint(point: Point): Point {
  return { x: point.x, y: point.y };
}

function cloneStyle(style: AnnotationStyle): AnnotationStyle {
  return {
    color: style.color,
    strokeWidth: style.strokeWidth,
    fontSize: style.fontSize,
    mosaicBrushSize: style.mosaicBrushSize,
    mosaicBlockSize: style.mosaicBlockSize,
  };
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
  if (object.kind === "text") {
    return {
      id: object.id,
      kind: "text",
      origin: clonePoint(object.origin),
      text: object.text,
      maxWidth: object.maxWidth,
      style: cloneStyle(object.style),
    };
  }
  if (object.kind === "mosaic") {
    return {
      id: object.id,
      kind: "mosaic",
      points: object.points.map(clonePoint),
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

function cloneHistoryEntry(entry: HistoryEntry): HistoryEntry {
  return {
    objects: cloneObjects(entry.objects),
    selectedId: entry.selectedId,
  };
}

function cloneTextDraft(draft: TextDraft | null): TextDraft | null {
  return draft
    ? {
        ...draft,
        origin: clonePoint(draft.origin),
        style: cloneStyle(draft.style),
      }
    : null;
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

function glyphWidth(character: string, fontSize: number): number {
  return (/^[\u0000-\u00ff]$/.test(character) ? 0.6 : 1) * fontSize;
}

function wrapText(text: string, maximumWidth: number, fontSize: number): string[] {
  const lines: string[] = [];
  for (const paragraph of text.split("\n")) {
    let line = "";
    let width = 0;
    if (paragraph.length === 0) {
      lines.push("");
      continue;
    }
    for (const character of paragraph) {
      const characterWidth = glyphWidth(character, fontSize);
      if (line && width + characterWidth > maximumWidth) {
        lines.push(line);
        line = character;
        width = characterWidth;
      } else {
        line += character;
        width += characterWidth;
      }
    }
    lines.push(line);
  }
  return lines.length > 0 ? lines : [""];
}

export function layoutTextAnnotation(
  object: TextAnnotation,
  scale: SnapshotScale,
): TextLayout {
  const scaleValue = Math.min(scale.scaleX, scale.scaleY);
  const fontSizePhysical = object.style.fontSize * scaleValue;
  const lineHeight = fontSizePhysical * 1.25;
  const lines = wrapText(
    object.text,
    Math.max(fontSizePhysical, object.maxWidth),
    fontSizePhysical,
  );
  const measuredWidth = Math.max(
    fontSizePhysical,
    ...lines.map((line) =>
      [...line].reduce(
        (width, character) => width + glyphWidth(character, fontSizePhysical),
        0,
      ),
    ),
  );
  return {
    lines,
    bounds: {
      x: object.origin.x,
      y: object.origin.y,
      width: Math.min(object.maxWidth, measuredWidth),
      height: lines.length * lineHeight,
    },
    fontSizePhysical,
    lineHeight,
  };
}

export function projectTextDraft(
  draft: TextDraft,
  selection: Rect,
  scale: SnapshotScale,
): Rect {
  const logicalOrigin = {
    x: draft.origin.x / scale.scaleX,
    y: draft.origin.y / scale.scaleY,
  };
  const selectionRight = (selection.x + selection.width) / scale.scaleX;
  const selectionBottom = (selection.y + selection.height) / scale.scaleY;
  const maximumWidth = Math.max(1, selectionRight - logicalOrigin.x);
  const maximumHeight = Math.max(1, selectionBottom - logicalOrigin.y);
  const content = `${draft.value}${draft.preedit}`;
  const lines = wrapText(content, maximumWidth, draft.style.fontSize);
  const width = Math.min(
    maximumWidth,
    Math.max(
      64,
      ...lines.map((line) =>
        [...line].reduce(
          (total, character) => total + glyphWidth(character, draft.style.fontSize),
          0,
        ) + 12,
      ),
    ),
  );
  const height = Math.min(
    maximumHeight,
    Math.max(draft.style.fontSize * 1.5, lines.length * draft.style.fontSize * 1.25 + 8),
  );
  return { x: logicalOrigin.x, y: logicalOrigin.y, width, height };
}

export function interpolateMosaicPoints(
  start: Point,
  end: Point,
  brushSizePhysical: number,
): Point[] {
  const distance = Math.hypot(end.x - start.x, end.y - start.y);
  const maximumGap = Math.max(1, brushSizePhysical / 4);
  const segments = Math.max(1, Math.ceil(distance / maximumGap));
  return Array.from({ length: segments + 1 }, (_, index) => ({
    x: start.x + ((end.x - start.x) * index) / segments,
    y: start.y + ((end.y - start.y) * index) / segments,
  }));
}

export function mosaicTileKey(
  tileX: number,
  tileY: number,
  blockSize: number,
): string {
  return `${blockSize}:${tileX}:${tileY}`;
}

export function mosaicDamageTiles(
  points: readonly Point[],
  brushSizePhysical: number,
  frame: { readonly width: number; readonly height: number },
  tileSize = MOSAIC_TILE_SIZE,
): Set<string> {
  const damaged = new Set<string>();
  const radius = brushSizePhysical / 2;
  const maximumTileX = Math.max(0, Math.ceil(frame.width / tileSize) - 1);
  const maximumTileY = Math.max(0, Math.ceil(frame.height / tileSize) - 1);
  for (const point of points) {
    const left = clamp(Math.floor((point.x - radius) / tileSize), 0, maximumTileX);
    const right = clamp(Math.floor((point.x + radius) / tileSize), 0, maximumTileX);
    const top = clamp(Math.floor((point.y - radius) / tileSize), 0, maximumTileY);
    const bottom = clamp(Math.floor((point.y + radius) / tileSize), 0, maximumTileY);
    for (let tileY = top; tileY <= bottom; tileY += 1) {
      for (let tileX = left; tileX <= right; tileX += 1) {
        damaged.add(`${tileX}:${tileY}`);
      }
    }
  }
  return damaged;
}

function objectBounds(object: AnnotationObject, scale: SnapshotScale): Rect {
  if (object.kind === "rectangle") return object.rect;
  if (object.kind === "text") return layoutTextAnnotation(object, scale).bounds;
  if (object.kind === "mosaic") {
    const radius = object.style.mosaicBrushSize * Math.min(scale.scaleX, scale.scaleY) / 2;
    const xs = object.points.map((point) => point.x);
    const ys = object.points.map((point) => point.y);
    return {
      x: Math.min(...xs) - radius,
      y: Math.min(...ys) - radius,
      width: Math.max(...xs) - Math.min(...xs) + radius * 2,
      height: Math.max(...ys) - Math.min(...ys) + radius * 2,
    };
  }
  return normalizedRect(object.start, object.end);
}

export class AnnotationSession {
  #selection: Rect;
  readonly #scale: SnapshotScale;
  #objects: AnnotationObject[] = [];
  #selectedId: string | null = null;
  #tool: AnnotationTool = "select";
  #style: AnnotationStyle = cloneStyle(DEFAULT_ANNOTATION_STYLE);
  #settingsOpen: "rectangle" | "arrow" | "text" | "mosaic" | null = null;
  #undo: HistoryEntry[] = [];
  #redo: HistoryEntry[] = [];
  #gesture: AnnotationGesture | null = null;
  #textDraft: TextDraft | null = null;
  #textDraftBefore: HistoryEntry | null = null;
  #everEdited = false;
  #nextId = 1;

  constructor(options: {
    readonly selection: Rect;
    readonly scale: SnapshotScale;
    readonly initialStyle?: AnnotationStyle;
  }) {
    this.#selection = { ...options.selection };
    this.#scale = options.scale;
    if (options.initialStyle) this.setStyle(options.initialStyle);
  }

  setTool(tool: AnnotationTool, openSettings: boolean): void {
    this.#tool = tool;
    if (tool !== "select") this.#selectedId = null;
    this.#settingsOpen =
      openSettings &&
      (tool === "rectangle" ||
        tool === "arrow" ||
        tool === "text" ||
        tool === "mosaic")
        ? tool
        : null;
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
      fontSize: style.fontSize ?? this.#style.fontSize,
      mosaicBrushSize:
        style.mosaicBrushSize ?? this.#style.mosaicBrushSize,
      mosaicBlockSize:
        style.mosaicBlockSize ?? this.#style.mosaicBlockSize,
    };
    if (!ANNOTATION_COLORS.includes(next.color)) throw new Error("invalid annotation color");
    if (!ANNOTATION_STROKE_WIDTHS.includes(next.strokeWidth)) {
      throw new Error("invalid annotation stroke width");
    }
    if (!ANNOTATION_FONT_SIZES.includes(next.fontSize)) {
      throw new Error("invalid annotation font size");
    }
    if (!MOSAIC_BRUSH_SIZES.includes(next.mosaicBrushSize)) {
      throw new Error("invalid mosaic brush size");
    }
    if (!MOSAIC_BLOCK_SIZES.includes(next.mosaicBlockSize)) {
      throw new Error("invalid mosaic block size");
    }
    this.#style = next;
    if (this.#textDraft) {
      this.#textDraft = { ...this.#textDraft, style: cloneStyle(next) };
      return;
    }
    const index = this.#selectedIndex();
    if (index < 0) return;
    const selected = this.#objects[index];
    if (
      !selected ||
      (selected.style.color === next.color &&
        selected.style.strokeWidth === next.strokeWidth &&
        selected.style.fontSize === next.fontSize &&
        selected.style.mosaicBrushSize === next.mosaicBrushSize &&
        selected.style.mosaicBlockSize === next.mosaicBlockSize)
    ) {
      return;
    }
    const before = this.#historyEntry();
    this.#objects[index] = { ...selected, style: cloneStyle(next) };
    this.#commit(before);
  }

  pointerDown(point: Point): boolean {
    const clipped = clipPoint(point, this.#selection);
    if (this.#textDraft) {
      this.commitTextDraft();
      this.#gesture = { kind: "select-existing" };
      return true;
    }
    if (this.#tool === "text") {
      const existing = this.hitTest(clipped, "text");
      if (existing?.kind === "text") {
        this.#selectedId = existing.id;
        this.#gesture = {
          kind: "text-press",
          origin: clipped,
          current: clipped,
          initial: cloneObject(existing) as TextAnnotation,
          before: this.#historyEntry(),
        };
      } else {
        this.#selectedId = null;
        this.#startTextDraft(null, clipped);
        this.#gesture = { kind: "select-existing" };
      }
      return true;
    }
    if (this.#tool === "mosaic") {
      this.#selectedId = null;
      this.#gesture = {
        kind: "draw-mosaic",
        origin: clipped,
        current: clipped,
        points: [clipped],
        before: this.#historyEntry(),
      };
      return true;
    }
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
    if (this.#gesture.kind === "draw-mosaic") {
      const previous = this.#gesture.points[this.#gesture.points.length - 1];
      if (previous && (previous.x !== this.#gesture.current.x || previous.y !== this.#gesture.current.y)) {
        const brush =
          this.#style.mosaicBrushSize *
          Math.min(this.#scale.scaleX, this.#scale.scaleY);
        this.#gesture.points.push(
          ...interpolateMosaicPoints(previous, this.#gesture.current, brush).slice(1),
        );
      }
    } else if (this.#gesture.kind === "move") {
      this.#replaceObject(this.#movePreview(this.#gesture));
    } else if (this.#gesture.kind === "resize") {
      this.#replaceObject(this.#resizePreview(this.#gesture));
    } else if (
      this.#gesture.kind === "text-press" &&
      this.#movedLogical(this.#gesture.origin, this.#gesture.current)
    ) {
      const layout = layoutTextAnnotation(this.#gesture.initial, this.#scale);
      const delta = {
        x: clamp(
          this.#gesture.current.x - this.#gesture.origin.x,
          this.#selection.x - layout.bounds.x,
          this.#selection.x + this.#selection.width - layout.bounds.x - layout.bounds.width,
        ),
        y: clamp(
          this.#gesture.current.y - this.#gesture.origin.y,
          this.#selection.y - layout.bounds.y,
          this.#selection.y + this.#selection.height - layout.bounds.y - layout.bounds.height,
        ),
      };
      this.#replaceObject({
        ...this.#gesture.initial,
        origin: {
          x: this.#gesture.initial.origin.x + delta.x,
          y: this.#gesture.initial.origin.y + delta.y,
        },
      });
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
    if (gesture.kind === "text-press") {
      if (this.#objectsChanged(gesture.before.objects)) {
        this.#commit(gesture.before);
      } else {
        this.#startTextDraft(gesture.initial, gesture.initial.origin);
      }
      this.#gesture = null;
      return;
    }
    if (gesture.kind === "draw-mosaic") {
      const object: MosaicAnnotation = {
        id: this.#createId(),
        kind: "mosaic",
        points: gesture.points.map(clonePoint),
        style: cloneStyle(this.#style),
      };
      this.#objects.push(object);
      this.#selectedId = object.id;
      this.#commit(gesture.before);
    } else if (gesture.kind === "draw-rectangle") {
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
    this.#textDraft = null;
    this.#textDraftBefore = null;
  }

  cancelGesture(): void {
    if (
      this.#gesture &&
      (this.#gesture.kind === "move" ||
        this.#gesture.kind === "resize" ||
        this.#gesture.kind === "text-press")
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
      if (object.kind === "mosaic") {
        const radius =
          object.style.mosaicBrushSize *
          Math.min(this.#scale.scaleX, this.#scale.scaleY) /
          2;
        if (
          object.points.some((candidate) =>
            Math.hypot(candidate.x - point.x, candidate.y - point.y) <= radius,
          )
        ) {
          return object;
        }
      } else if (object.kind === "text") {
        if (contains(layoutTextAnnotation(object, this.#scale).bounds, point)) return object;
      } else if (object.kind === "arrow") {
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

  beginTextEditAt(point: Point): boolean {
    if (this.#textDraft || this.#gesture) return false;
    const object = this.hitTest(clipPoint(point, this.#selection), "text");
    if (!object || object.kind !== "text") return false;
    this.#selectedId = object.id;
    this.#tool = "text";
    this.#startTextDraft(object, object.origin);
    return true;
  }

  updateTextDraft(value: string): void {
    if (!this.#textDraft) return;
    this.#textDraft = { ...this.#textDraft, value };
  }

  compositionStart(): void {
    if (!this.#textDraft) return;
    this.#textDraft = { ...this.#textDraft, isComposing: true, preedit: "" };
  }

  compositionUpdate(preedit: string): void {
    if (!this.#textDraft) return;
    this.#textDraft = { ...this.#textDraft, isComposing: true, preedit };
  }

  compositionEnd(_committed: string): void {
    if (!this.#textDraft) return;
    this.#textDraft = { ...this.#textDraft, isComposing: false, preedit: "" };
  }

  commitTextDraft(): boolean {
    const draft = this.#textDraft;
    const before = this.#textDraftBefore;
    if (!draft || !before || draft.isComposing) return false;
    const index = draft.editingId
      ? this.#objects.findIndex((object) => object.id === draft.editingId)
      : -1;
    const text = draft.value;
    if (text.trim().length === 0) {
      if (index >= 0) this.#objects.splice(index, 1);
      this.#selectedId = null;
    } else {
      const object: TextAnnotation = {
        id: draft.editingId ?? this.#createId(),
        kind: "text",
        origin: clonePoint(draft.origin),
        text,
        maxWidth: Math.max(
          1,
          this.#selection.x + this.#selection.width - draft.origin.x,
        ),
        style: cloneStyle(draft.style),
      };
      if (index >= 0) this.#objects[index] = object;
      else this.#objects.push(object);
      this.#selectedId = object.id;
    }
    this.#textDraft = null;
    this.#textDraftBefore = null;
    if (this.#objectsChanged(before.objects)) {
      this.#commit(before);
      return true;
    }
    return false;
  }

  cancelTextDraft(): void {
    this.#textDraft = null;
    this.#textDraftBefore = null;
  }

  deleteSelected(): boolean {
    if (this.#gesture || this.#textDraft) return false;
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
    if (this.#gesture || this.#textDraft) return false;
    const previous = this.#undo.pop();
    if (!previous) return false;
    this.#redo.push(this.#historyEntry());
    this.#restore(previous);
    return true;
  }

  redo(): boolean {
    if (this.#gesture || this.#textDraft) return false;
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
      textDraft: this.#textDraft
        ? cloneTextDraft(this.#textDraft)
        : null,
    };
  }

  checkpoint(): AnnotationCheckpoint {
    if (this.#gesture) throw new Error("cannot checkpoint an active annotation gesture");
    return {
      selection: { ...this.#selection },
      objects: cloneObjects(this.#objects),
      selectedId: this.#selectedId,
      tool: this.#tool,
      style: cloneStyle(this.#style),
      settingsOpen: this.#settingsOpen,
      undo: this.#undo.map(cloneHistoryEntry),
      redo: this.#redo.map(cloneHistoryEntry),
      textDraft: cloneTextDraft(this.#textDraft),
      textDraftBefore: this.#textDraftBefore
        ? cloneHistoryEntry(this.#textDraftBefore)
        : null,
      everEdited: this.#everEdited,
      nextId: this.#nextId,
    };
  }

  restoreCheckpoint(checkpoint: AnnotationCheckpoint): void {
    this.#selection = { ...checkpoint.selection };
    this.#objects = cloneObjects(checkpoint.objects);
    this.#selectedId = checkpoint.selectedId;
    this.#tool = checkpoint.tool;
    this.#style = cloneStyle(checkpoint.style);
    this.#settingsOpen = checkpoint.settingsOpen;
    this.#undo = checkpoint.undo.map(cloneHistoryEntry);
    this.#redo = checkpoint.redo.map(cloneHistoryEntry);
    this.#gesture = null;
    this.#textDraft = cloneTextDraft(checkpoint.textDraft);
    this.#textDraftBefore = checkpoint.textDraftBefore
      ? cloneHistoryEntry(checkpoint.textDraftBefore)
      : null;
    this.#everEdited = checkpoint.everEdited;
    this.#nextId = checkpoint.nextId;
  }

  renderPlan(): AnnotationRenderPlan {
    const plan = buildAnnotationRenderPlan(
      this.snapshotState(),
      this.#gesture,
      this.#style,
      this.#nextId,
    );
    return this.#textDraft?.editingId
      ? {
          ...plan,
          objects: plan.objects.filter(
            (object) => object.id !== this.#textDraft?.editingId,
          ),
        }
      : plan;
  }

  #createId(): string {
    const id = `annotation-${this.#nextId}`;
    this.#nextId += 1;
    return id;
  }

  #startTextDraft(object: TextAnnotation | null, origin: Point): void {
    this.#textDraftBefore = this.#historyEntry();
    this.#textDraft = {
      editingId: object?.id ?? null,
      origin: clonePoint(origin),
      value: object?.text ?? "",
      preedit: "",
      isComposing: false,
      style: cloneStyle(object?.style ?? this.#style),
    };
    this.#selectedId = object?.id ?? null;
  }

  #movedLogical(start: Point, end: Point): boolean {
    return (
      Math.hypot(
        (end.x - start.x) / this.#scale.scaleX,
        (end.y - start.y) / this.#scale.scaleY,
      ) >= screenshotUiTheme.selection.gestureThreshold
    );
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
    if (selected.kind === "text" || selected.kind === "mosaic") return null;
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
    const bounds = objectBounds(gesture.initial, this.#scale);
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
    if (gesture.initial.kind === "text") {
      return {
        ...gesture.initial,
        origin: {
          x: gesture.initial.origin.x + boundedDelta.x,
          y: gesture.initial.origin.y + boundedDelta.y,
        },
      };
    }
    if (gesture.initial.kind === "mosaic") {
      return {
        ...gesture.initial,
        points: gesture.initial.points.map((point) => ({
          x: point.x + boundedDelta.x,
          y: point.y + boundedDelta.y,
        })),
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
    if (gesture.initial.kind === "text" || gesture.initial.kind === "mosaic") {
      return gesture.initial;
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
  } else if (gesture?.kind === "draw-mosaic") {
    objects.push({
      id: `preview-${previewId}`,
      kind: "mosaic",
      points: gesture.points.map(clonePoint),
      style: cloneStyle(style),
    });
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
  readonly mosaic?: {
    readonly source: CanvasImageSource;
    readonly cache: MosaicTileCache;
    readonly frame: { readonly width: number; readonly height: number };
  };
}

export class MosaicTileCache {
  readonly #tiles = new Map<string, HTMLCanvasElement>();
  #buildCount = 0;

  getTile(
    source: CanvasImageSource,
    tileX: number,
    tileY: number,
    blockSize: number,
    frame: { readonly width: number; readonly height: number },
  ): HTMLCanvasElement {
    const key = mosaicTileKey(tileX, tileY, blockSize);
    const cached = this.#tiles.get(key);
    if (cached) return cached;
    const x = tileX * MOSAIC_TILE_SIZE;
    const y = tileY * MOSAIC_TILE_SIZE;
    const width = Math.min(MOSAIC_TILE_SIZE, frame.width - x);
    const height = Math.min(MOSAIC_TILE_SIZE, frame.height - y);
    const reduced = document.createElement("canvas");
    reduced.width = Math.max(1, Math.ceil(width / blockSize));
    reduced.height = Math.max(1, Math.ceil(height / blockSize));
    const reducedContext = reduced.getContext("2d", { alpha: false });
    if (!reducedContext) throw new Error("2D canvas unavailable");
    reducedContext.imageSmoothingEnabled = false;
    reducedContext.drawImage(
      source,
      x,
      y,
      width,
      height,
      0,
      0,
      reduced.width,
      reduced.height,
    );
    const tile = document.createElement("canvas");
    tile.width = width;
    tile.height = height;
    const tileContext = tile.getContext("2d", { alpha: false });
    if (!tileContext) throw new Error("2D canvas unavailable");
    tileContext.imageSmoothingEnabled = false;
    tileContext.drawImage(
      reduced,
      0,
      0,
      reduced.width,
      reduced.height,
      0,
      0,
      width,
      height,
    );
    reduced.width = 0;
    reduced.height = 0;
    this.#tiles.set(key, tile);
    this.#buildCount += 1;
    return tile;
  }

  snapshotStats(): { readonly tileCount: number; readonly buildCount: number } {
    return { tileCount: this.#tiles.size, buildCount: this.#buildCount };
  }

  dispose(): void {
    for (const tile of this.#tiles.values()) {
      tile.width = 0;
      tile.height = 0;
    }
    this.#tiles.clear();
    this.#buildCount = 0;
  }
}

function renderMosaic(
  context: CanvasRenderingContext2D,
  object: MosaicAnnotation,
  options: RenderAnnotationsOptions,
): void {
  if (!options.mosaic || object.points.length === 0) return;
  const brush =
    object.style.mosaicBrushSize *
    Math.min(options.scale.scaleX, options.scale.scaleY);
  context.save();
  context.beginPath();
  for (const point of object.points) {
    context.moveTo(point.x + brush / 2, point.y);
    context.arc(point.x, point.y, brush / 2, 0, Math.PI * 2);
  }
  context.clip();
  for (const tileCoordinate of mosaicDamageTiles(
    object.points,
    brush,
    options.mosaic.frame,
  )) {
    const [tileXText, tileYText] = tileCoordinate.split(":");
    const tileX = Number(tileXText);
    const tileY = Number(tileYText);
    const tile = options.mosaic.cache.getTile(
      options.mosaic.source,
      tileX,
      tileY,
      object.style.mosaicBlockSize,
      options.mosaic.frame,
    );
    context.drawImage(
      tile,
      tileX * MOSAIC_TILE_SIZE,
      tileY * MOSAIC_TILE_SIZE,
    );
  }
  context.restore();
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
    } else if (object.kind === "arrow") {
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
    } else if (object.kind === "mosaic") {
      renderMosaic(context, object, options);
    } else {
      const layout = layoutTextAnnotation(object, options.scale);
      context.font = `${layout.fontSizePhysical}px ${screenshotUiTheme.typography.fontFamily}`;
      context.textBaseline = "top";
      for (let index = 0; index < layout.lines.length; index += 1) {
        context.fillText(
          layout.lines[index] ?? "",
          object.origin.x,
          object.origin.y + index * layout.lineHeight,
          object.maxWidth,
        );
      }
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
      : selected.kind === "arrow"
        ? [selected.start, selected.end]
        : [];
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
