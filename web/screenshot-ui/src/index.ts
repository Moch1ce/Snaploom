export interface Point {
  readonly x: number;
  readonly y: number;
}

export interface Rect extends Point {
  readonly width: number;
  readonly height: number;
}

export interface Size {
  readonly width: number;
  readonly height: number;
}

export type ToolbarPlacement = "below" | "inside" | "above";

export interface FloatingUiProjection {
  readonly toolbar: Rect & { readonly placement: ToolbarPlacement };
  readonly sizeLabel: Rect & { readonly placement: "above" | "inside" };
}

export type ScreenshotToolbarAction =
  | "rectangle"
  | "arrow"
  | "text"
  | "mosaic"
  | "undo"
  | "redo"
  | "save"
  | "cancel"
  | "complete";

export const screenshotUiTheme = {
  borderWidth: 1,
  typography: {
    fontFamily:
      '-apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
  },
  colors: {
    brand: "#07C977",
    danger: "#FF4D4F",
    text: "#202124",
    surface: "#FAFAFA",
    border: "#D8DADF",
    hover: "#F2F2F2",
    selected: "#EDEDED",
    disabledText: "rgba(32, 33, 36, 0.32)",
    overlayMask: "rgba(0, 0, 0, 0.45098)",
  },
  toolbar: {
    layer: 2,
    height: 44,
    horizontalPadding: 14,
    hitSize: 40,
    iconSize: 18,
    radius: 8,
    separatorWidth: 1,
    separatorMargin: 10,
    gap: 8,
    insideInset: 16,
    hoverSize: 28,
    selectedSize: 24,
    itemRadius: 4,
    separatorHeight: 20,
    focusOutlineWidth: 2,
    focusOutlineOffset: -4,
    shadow: "0 6px 20px rgba(0, 0, 0, 0.18)",
  },
  selection: {
    outlineWidth: 2,
    handleSize: 8,
    minimumPhysicalSize: 8,
    gestureThreshold: 4,
  },
  sizeLabel: {
    layer: 2,
    minimumWidth: 1,
    gap: 4,
    height: 24,
    horizontalPadding: 8,
    radius: 4,
    foreground: "#FFFFFF",
    background: "rgba(32, 33, 36, 0.92)",
    fontSize: 12,
  },
  annotation: {
    colors: ["#FF4D4F", "#FADB14", "#07C977", "#1677FF", "#202124", "#FFFFFF"],
    strokeWidths: [2, 4, 8],
    fontSizes: [16, 24, 32],
    mosaicBrushSizes: [16, 32, 64],
    mosaicBlockSizes: [8, 12, 16],
    mosaicTileSize: 128,
  },
  annotationSettings: {
    layer: 3,
    gap: 8,
    anchorOffset: 8,
    minimumHeight: 42,
    paddingBlock: 6,
    paddingInline: 8,
    radius: 8,
    pointerSize: 8,
    pointerTop: -5,
    pointerLeft: 16,
    groupGap: 4,
    groupDividerPadding: 8,
    buttonSize: 28,
    buttonRadius: 4,
    swatchSize: 14,
    swatchBorder: "rgba(32, 33, 36, 0.20)",
    selectedInset: 3,
    widthSample: 16,
    maximumWidthSampleThickness: 6,
    pillRadius: 999,
    numericFontSize: 11,
    shadow: "0 6px 20px rgba(0, 0, 0, 0.18)",
  },
  textEditor: {
    layer: 4,
    minimumWidth: 64,
    minimumHeight: 36,
    radius: 2,
    borderWidth: 1,
    lineHeight: 1.25,
    selectionBackground: "rgba(7, 201, 119, 0.22)",
    handleSize: 5,
    handleOffset: -3,
    handleRadius: "50%",
  },
  magnifier: {
    size: 132,
    samplePhysicalSize: 55,
    pointerGap: 16,
    viewportInset: 8,
    radius: 8,
    borderWidth: 1,
    crosshairSize: 16,
    crosshairWidth: 2,
    shadowColor: "rgba(0, 0, 0, 0.18)",
    shadowBlur: 20,
    shadowOffsetY: 6,
  },
  outputStatus: {
    layer: 5,
    top: 16,
    horizontalCenterPercent: 50,
    horizontalTranslatePercent: -50,
    viewportInset: 16,
    maxWidth: 480,
    paddingBlock: 8,
    paddingInline: 12,
    foreground: "#FFFFFF",
    background: "rgba(176, 36, 39, 0.94)",
    fontSize: 13,
    lineHeight: 20,
    radius: 6,
    shadow: "0 4px 16px rgba(0, 0, 0, 0.18)",
  },
  workspaceInset: 8,
} as const;

const TOOL_COUNT = 9;
const SEPARATOR_COUNT = 2;

export function toolbarLogicalWidth(): number {
  const toolbar = screenshotUiTheme.toolbar;
  return (
    TOOL_COUNT * toolbar.hitSize +
    SEPARATOR_COUNT *
      (toolbar.separatorWidth + toolbar.separatorMargin * 2) +
    toolbar.horizontalPadding * 2
  );
}

export function applyScreenshotUiTheme(element: HTMLElement): void {
  const {
    colors,
    typography,
    toolbar,
    sizeLabel,
    annotationSettings,
    textEditor,
    outputStatus,
  } = screenshotUiTheme;
  const values: Readonly<Record<string, string>> = {
    "--ui-border-width": `${screenshotUiTheme.borderWidth}px`,
    "--ui-font-family": typography.fontFamily,
    "--brand": colors.brand,
    "--danger": colors.danger,
    "--text": colors.text,
    "--surface": colors.surface,
    "--border": colors.border,
    "--hover": colors.hover,
    "--selected": colors.selected,
    "--disabled-text": colors.disabledText,
    "--toolbar-width": `${toolbarLogicalWidth()}px`,
    "--toolbar-layer": String(toolbar.layer),
    "--toolbar-height": `${toolbar.height}px`,
    "--toolbar-padding-inline": `${toolbar.horizontalPadding}px`,
    "--toolbar-hit-size": `${toolbar.hitSize}px`,
    "--toolbar-radius": `${toolbar.radius}px`,
    "--toolbar-hover-size": `${toolbar.hoverSize}px`,
    "--toolbar-selected-size": `${toolbar.selectedSize}px`,
    "--toolbar-item-radius": `${toolbar.itemRadius}px`,
    "--toolbar-separator-width": `${toolbar.separatorWidth}px`,
    "--toolbar-separator-height": `${toolbar.separatorHeight}px`,
    "--toolbar-separator-margin": `${toolbar.separatorMargin}px`,
    "--toolbar-focus-outline-width": `${toolbar.focusOutlineWidth}px`,
    "--toolbar-focus-outline-offset": `${toolbar.focusOutlineOffset}px`,
    "--toolbar-shadow": toolbar.shadow,
    "--size-label-height": `${sizeLabel.height}px`,
    "--size-label-layer": String(sizeLabel.layer),
    "--size-label-min-width": `${sizeLabel.minimumWidth}px`,
    "--size-label-padding-inline": `${sizeLabel.horizontalPadding}px`,
    "--size-label-radius": `${sizeLabel.radius}px`,
    "--size-label-foreground": sizeLabel.foreground,
    "--size-label-background": sizeLabel.background,
    "--size-label-font-size": `${sizeLabel.fontSize}px`,
    "--annotation-settings-layer": String(annotationSettings.layer),
    "--annotation-settings-gap": `${annotationSettings.gap}px`,
    "--annotation-settings-min-height": `${annotationSettings.minimumHeight}px`,
    "--annotation-settings-padding-block": `${annotationSettings.paddingBlock}px`,
    "--annotation-settings-padding-inline": `${annotationSettings.paddingInline}px`,
    "--annotation-settings-radius": `${annotationSettings.radius}px`,
    "--annotation-settings-pointer-size": `${annotationSettings.pointerSize}px`,
    "--annotation-settings-pointer-top": `${annotationSettings.pointerTop}px`,
    "--annotation-settings-pointer-left": `${annotationSettings.pointerLeft}px`,
    "--annotation-settings-group-gap": `${annotationSettings.groupGap}px`,
    "--annotation-settings-group-divider-padding": `${annotationSettings.groupDividerPadding}px`,
    "--annotation-settings-button-size": `${annotationSettings.buttonSize}px`,
    "--annotation-settings-button-radius": `${annotationSettings.buttonRadius}px`,
    "--annotation-settings-swatch-size": `${annotationSettings.swatchSize}px`,
    "--annotation-settings-swatch-border": annotationSettings.swatchBorder,
    "--annotation-settings-selected-inset": `${annotationSettings.selectedInset}px`,
    "--annotation-settings-width-sample": `${annotationSettings.widthSample}px`,
    "--annotation-settings-max-width-sample-thickness": `${annotationSettings.maximumWidthSampleThickness}px`,
    "--annotation-settings-pill-radius": `${annotationSettings.pillRadius}px`,
    "--annotation-settings-numeric-font-size": `${annotationSettings.numericFontSize}px`,
    "--annotation-settings-shadow": annotationSettings.shadow,
    "--text-editor-layer": String(textEditor.layer),
    "--text-editor-min-width": `${textEditor.minimumWidth}px`,
    "--text-editor-min-height": `${textEditor.minimumHeight}px`,
    "--text-editor-radius": `${textEditor.radius}px`,
    "--text-editor-border-width": `${textEditor.borderWidth}px`,
    "--text-editor-line-height": String(textEditor.lineHeight),
    "--text-editor-selection-background": textEditor.selectionBackground,
    "--text-editor-handle-size": `${textEditor.handleSize}px`,
    "--text-editor-handle-offset": `${textEditor.handleOffset}px`,
    "--text-editor-handle-radius": textEditor.handleRadius,
    "--output-status-layer": String(outputStatus.layer),
    "--output-status-top": `${outputStatus.top}px`,
    "--output-status-horizontal-center": `${outputStatus.horizontalCenterPercent}%`,
    "--output-status-horizontal-translate": `${outputStatus.horizontalTranslatePercent}%`,
    "--output-status-viewport-margin": `${outputStatus.viewportInset * 2}px`,
    "--output-status-max-width": `${outputStatus.maxWidth}px`,
    "--output-status-padding-block": `${outputStatus.paddingBlock}px`,
    "--output-status-padding-inline": `${outputStatus.paddingInline}px`,
    "--output-status-foreground": outputStatus.foreground,
    "--output-status-background": outputStatus.background,
    "--output-status-font-size": `${outputStatus.fontSize}px`,
    "--output-status-line-height": `${outputStatus.lineHeight}px`,
    "--output-status-radius": `${outputStatus.radius}px`,
    "--output-status-shadow": outputStatus.shadow,
  };
  for (const [name, value] of Object.entries(values)) {
    element.style.setProperty(name, value);
  }
}

function clamp(value: number, minimum: number, maximum: number): number {
  if (maximum < minimum) return minimum;
  return Math.min(Math.max(value, minimum), maximum);
}

export function projectMagnifier(pointer: Point, viewport: Rect): Rect {
  const magnifier = screenshotUiTheme.magnifier;
  const right = viewport.x + viewport.width - magnifier.viewportInset;
  const bottom = viewport.y + viewport.height - magnifier.viewportInset;
  const minimumX = viewport.x + magnifier.viewportInset;
  const minimumY = viewport.y + magnifier.viewportInset;
  const preferredX = pointer.x + magnifier.pointerGap;
  const preferredY = pointer.y + magnifier.pointerGap;
  const x =
    preferredX + magnifier.size <= right
      ? preferredX
      : pointer.x - magnifier.pointerGap - magnifier.size;
  const y =
    preferredY + magnifier.size <= bottom
      ? preferredY
      : pointer.y - magnifier.pointerGap - magnifier.size;
  return {
    x: clamp(x, minimumX, right - magnifier.size),
    y: clamp(y, minimumY, bottom - magnifier.size),
    width: magnifier.size,
    height: magnifier.size,
  };
}

export function projectPixelSample(pointerPhysical: Point, frame: Size): Rect {
  const sampleSize = Math.min(
    screenshotUiTheme.magnifier.samplePhysicalSize,
    frame.width,
    frame.height,
  );
  const sampleRadius = Math.floor(sampleSize / 2);
  return {
    x: clamp(
      Math.floor(pointerPhysical.x) - sampleRadius,
      0,
      frame.width - sampleSize,
    ),
    y: clamp(
      Math.floor(pointerPhysical.y) - sampleRadius,
      0,
      frame.height - sampleSize,
    ),
    width: sampleSize,
    height: sampleSize,
  };
}

export function projectFloatingUi(
  selection: Rect,
  workspace: Rect,
  measuredLabelWidth = 92,
): FloatingUiProjection {
  const width = toolbarLogicalWidth();
  const height = screenshotUiTheme.toolbar.height;
  const workspaceRight = workspace.x + workspace.width;
  const workspaceBottom = workspace.y + workspace.height;
  const outsideLeft = workspace.x + screenshotUiTheme.workspaceInset;
  const outsideRight = workspaceRight - screenshotUiTheme.workspaceInset;
  const outsideBottom = workspaceBottom - screenshotUiTheme.workspaceInset;
  const externalX = clamp(
    selection.width >= width
      ? selection.x + selection.width - width
      : selection.x,
    outsideLeft,
    outsideRight - width,
  );

  let toolbar: FloatingUiProjection["toolbar"];
  const belowY = selection.y + selection.height + screenshotUiTheme.toolbar.gap;
  if (belowY + height <= outsideBottom) {
    toolbar = { x: externalX, y: belowY, width, height, placement: "below" };
  } else if (
    selection.width >= width + screenshotUiTheme.toolbar.insideInset * 2 &&
    selection.height >= height + screenshotUiTheme.toolbar.insideInset * 2
  ) {
    toolbar = {
      x:
        selection.x +
        selection.width -
        width -
        screenshotUiTheme.toolbar.insideInset,
      y:
        selection.y +
        selection.height -
        height -
        screenshotUiTheme.toolbar.insideInset,
      width,
      height,
      placement: "inside",
    };
  } else {
    toolbar = {
      x: externalX,
      y: Math.max(
        workspace.y + screenshotUiTheme.workspaceInset,
        selection.y - screenshotUiTheme.toolbar.gap - height,
      ),
      width,
      height,
      placement: "above",
    };
  }

  const labelHeight = screenshotUiTheme.sizeLabel.height;
  const labelWidth = Math.max(measuredLabelWidth, 1);
  const labelX = clamp(
    selection.x,
    outsideLeft,
    outsideRight - labelWidth,
  );
  const labelAboveY = selection.y - screenshotUiTheme.sizeLabel.gap - labelHeight;
  const sizeLabel: FloatingUiProjection["sizeLabel"] =
    labelAboveY >= workspace.y + screenshotUiTheme.workspaceInset
      ? {
          x: labelX,
          y: labelAboveY,
          width: labelWidth,
          height: labelHeight,
          placement: "above",
        }
      : {
          x: clamp(
            selection.x + screenshotUiTheme.workspaceInset,
            outsideLeft,
            outsideRight - labelWidth,
          ),
          y: selection.y + screenshotUiTheme.workspaceInset,
          width: labelWidth,
          height: labelHeight,
          placement: "inside",
        };

  return { toolbar, sizeLabel };
}

const iconPaths: Readonly<Record<ScreenshotToolbarAction, readonly string[]>> = {
  rectangle: ["M3 3h12v12H3z"],
  arrow: ["M3 15 15 3", "M9 3h6v6"],
  text: ["M4 3h10", "M9 3v12", "M6 15h6"],
  mosaic: ["M3 3h5v5H3z", "M10 3h5v5h-5z", "M3 10h5v5H3z", "M10 10h5v5h-5z"],
  undo: ["M6 5 3 8l3 3", "M4 8h6a4 4 0 0 1 4 4"],
  redo: ["m12 5 3 3-3 3", "M14 8H8a4 4 0 0 0-4 4"],
  save: ["M4 3h9l2 2v10H3V3z", "M6 3v5h6V3", "M6 15v-4h6v4"],
  cancel: ["M4 4l10 10", "M14 4 4 14"],
  complete: ["m3 9 4 4 8-8"],
};

export function createScreenshotToolbarIcon(
  action: ScreenshotToolbarAction,
): SVGSVGElement {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 18 18");
  svg.setAttribute("width", String(screenshotUiTheme.toolbar.iconSize));
  svg.setAttribute("height", String(screenshotUiTheme.toolbar.iconSize));
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("fill", "none");
  svg.setAttribute("stroke", "currentColor");
  svg.setAttribute("stroke-width", "1.5");
  svg.setAttribute("stroke-linecap", "round");
  svg.setAttribute("stroke-linejoin", "round");
  for (const d of iconPaths[action]) {
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", d);
    svg.append(path);
  }
  return svg;
}

export interface ScreenshotToolbarLabels {
  readonly toolbar: string;
  readonly actions: Readonly<Record<ScreenshotToolbarAction, string>>;
}

const defaultToolbarLabels: ScreenshotToolbarLabels = {
  toolbar: "Screenshot tools",
  actions: {
    rectangle: "Rectangle",
    arrow: "Arrow",
    text: "Text",
    mosaic: "Mosaic",
    undo: "Undo",
    redo: "Redo",
    save: "Save PNG",
    cancel: "Cancel",
    complete: "Complete",
  },
};

export interface ScreenshotToolbarOptions {
  readonly enabledActions?: ReadonlySet<ScreenshotToolbarAction>;
  readonly onAction?: (action: ScreenshotToolbarAction) => void;
  readonly labels?: ScreenshotToolbarLabels;
}

export function createProductMark(label: string): HTMLElement {
  const mark = document.createElement("strong");
  mark.className = "snaploom-product-mark";
  mark.textContent = label;
  return mark;
}

export function createScreenshotToolbar(
  options: ScreenshotToolbarOptions = {},
): HTMLDivElement {
  const enabled = options.enabledActions ?? new Set(["cancel", "complete"]);
  const toolbar = document.createElement("div");
  toolbar.className = "screenshot-toolbar";
  toolbar.dataset.overlayUi = "toolbar";
  toolbar.setAttribute("role", "toolbar");
  toolbar.setAttribute("aria-label", (options.labels ?? defaultToolbarLabels).toolbar);

  const actions: readonly (ScreenshotToolbarAction | "separator")[] = [
    "rectangle",
    "arrow",
    "text",
    "mosaic",
    "separator",
    "undo",
    "redo",
    "save",
    "separator",
    "cancel",
    "complete",
  ];
  for (const action of actions) {
    if (action === "separator") {
      const separator = document.createElement("span");
      separator.className = "screenshot-toolbar__separator";
      separator.dataset.overlayUi = "separator";
      separator.setAttribute("role", "separator");
      toolbar.append(separator);
      continue;
    }
    const button = document.createElement("button");
    button.className = `screenshot-toolbar__button screenshot-toolbar__button--${action}`;
    button.type = "button";
    button.dataset.action = action;
    button.dataset.overlayUi = "toolbar";
    const label = (options.labels ?? defaultToolbarLabels).actions[action];
    button.title = label;
    button.setAttribute("aria-label", label);
    button.disabled = !enabled.has(action);
    button.append(createScreenshotToolbarIcon(action));
    button.addEventListener("click", () => options.onAction?.(action));
    toolbar.append(button);
  }
  return toolbar;
}

export function applyScreenshotToolbarLabels(
  toolbar: HTMLElement,
  labels: ScreenshotToolbarLabels,
): void {
  toolbar.setAttribute("aria-label", labels.toolbar);
  for (const action of Object.keys(labels.actions) as ScreenshotToolbarAction[]) {
    const button = toolbar.querySelector<HTMLElement>(`[data-action="${action}"]`);
    if (!button) continue;
    button.title = labels.actions[action];
    button.setAttribute("aria-label", labels.actions[action]);
  }
}
