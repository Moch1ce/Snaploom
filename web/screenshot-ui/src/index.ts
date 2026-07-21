export interface Point {
  readonly x: number;
  readonly y: number;
}

export interface Rect extends Point {
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
  colors: {
    brand: "#07C977",
    danger: "#FF4D4F",
    text: "#202124",
    surface: "#FAFAFA",
    border: "#D8DADF",
    hover: "#F2F2F2",
    selected: "#EDEDED",
    overlayMask: "rgba(0, 0, 0, 0.45098)",
  },
  toolbar: {
    height: 44,
    horizontalPadding: 14,
    hitSize: 40,
    iconSize: 18,
    radius: 8,
    separatorWidth: 1,
    separatorMargin: 10,
    gap: 8,
    insideInset: 16,
  },
  selection: {
    outlineWidth: 2,
    handleSize: 8,
    minimumPhysicalSize: 8,
    gestureThreshold: 4,
  },
  sizeLabel: {
    gap: 4,
    height: 24,
    horizontalPadding: 8,
    radius: 4,
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

function clamp(value: number, minimum: number, maximum: number): number {
  if (maximum < minimum) return minimum;
  return Math.min(Math.max(value, minimum), maximum);
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

const labels: Readonly<Record<ScreenshotToolbarAction, string>> = {
  rectangle: "矩形",
  arrow: "箭头",
  text: "文字",
  mosaic: "马赛克",
  undo: "撤销",
  redo: "重做",
  save: "保存 PNG",
  cancel: "取消",
  complete: "完成",
};

export interface ScreenshotToolbarOptions {
  readonly enabledActions?: ReadonlySet<ScreenshotToolbarAction>;
  readonly onAction?: (action: ScreenshotToolbarAction) => void;
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
  toolbar.setAttribute("aria-label", "截图工具");

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
    button.title = labels[action];
    button.setAttribute("aria-label", labels[action]);
    button.disabled = !enabled.has(action);
    button.append(createScreenshotToolbarIcon(action));
    button.addEventListener("click", () => options.onAction?.(action));
    toolbar.append(button);
  }
  return toolbar;
}
