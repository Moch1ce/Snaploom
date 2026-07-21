import { describe, expect, it } from "vitest";
import {
  projectMagnifier,
  projectPixelSample,
  projectFloatingUi,
  screenshotUiTheme,
  toolbarLogicalWidth,
} from "../src/index";

describe("Screenshot UI contract", () => {
  it("freezes the documented light theme and brand geometry", () => {
    expect(screenshotUiTheme.colors).toEqual({
      brand: "#07C977",
      danger: "#FF4D4F",
      text: "#202124",
      surface: "#FAFAFA",
      border: "#D8DADF",
      hover: "#F2F2F2",
      selected: "#EDEDED",
      overlayMask: "rgba(0, 0, 0, 0.45098)",
    });
    expect(screenshotUiTheme.toolbar).toMatchObject({
      height: 44,
      horizontalPadding: 14,
      hitSize: 40,
      iconSize: 18,
      radius: 8,
    });
    expect(screenshotUiTheme.selection.outlineWidth).toBe(2);
    expect(screenshotUiTheme.magnifier).toMatchObject({
      size: 132,
      samplePhysicalSize: 55,
      pointerGap: 16,
      viewportInset: 8,
      crosshairSize: 16,
      crosshairWidth: 2,
    });
    expect(screenshotUiTheme.outputStatus).toEqual({
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
    });
  });

  it("keeps the nine-button toolbar width stable", () => {
    expect(toolbarLogicalWidth()).toBe(430);
  });

  it("projects the pixel magnifier beside the pointer and flips at viewport edges", () => {
    const viewport = { x: 0, y: 0, width: 800, height: 450 };
    expect(projectMagnifier({ x: 320, y: 240 }, viewport)).toEqual({
      x: 336,
      y: 256,
      width: 132,
      height: 132,
    });
    expect(projectMagnifier({ x: 790, y: 440 }, viewport)).toEqual({
      x: 642,
      y: 292,
      width: 132,
      height: 132,
    });
  });

  it("keeps a 55 physical-pixel sample centered and clamped to the frame", () => {
    expect(
      projectPixelSample({ x: 320, y: 240 }, { width: 1280, height: 720 }),
    ).toEqual({ x: 293, y: 213, width: 55, height: 55 });
    expect(
      projectPixelSample({ x: 2, y: 719 }, { width: 1280, height: 720 }),
    ).toEqual({ x: 0, y: 665, width: 55, height: 55 });
  });

  it("places toolbar below, inside, or above without crossing workspace", () => {
    const workspace = { x: 0, y: 24, width: 1000, height: 700 };
    expect(
      projectFloatingUi(
        { x: 300, y: 200, width: 500, height: 300 },
        workspace,
      ).toolbar,
    ).toEqual({ x: 370, y: 508, width: 430, height: 44, placement: "below" });
    expect(
      projectFloatingUi(
        { x: 300, y: 500, width: 500, height: 190 },
        workspace,
      ).toolbar,
    ).toEqual({ x: 354, y: 630, width: 430, height: 44, placement: "inside" });
    expect(
      projectFloatingUi(
        { x: 600, y: 660, width: 150, height: 30 },
        workspace,
      ).toolbar,
    ).toEqual({ x: 562, y: 608, width: 430, height: 44, placement: "above" });
  });
});
