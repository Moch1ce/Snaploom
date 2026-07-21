import { describe, expect, it } from "vitest";
import {
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
  });

  it("keeps the nine-button toolbar width stable", () => {
    expect(toolbarLogicalWidth()).toBe(430);
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
