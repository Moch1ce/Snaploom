import { describe, expect, it } from "vitest";
import { screenshotUiTheme } from "../src/index";

describe("screenshot UI theme", () => {
  it("keeps the documented Ant blue selection token", () => {
    expect(screenshotUiTheme.colors.selection).toBe("#1677ff");
  });
});
