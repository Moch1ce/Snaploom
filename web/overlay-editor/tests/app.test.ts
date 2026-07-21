import { describe, expect, it } from "vitest";
import { describeOverlayShell } from "../src/app";

describe("capture host shell", () => {
  it("declares a single visible canvas", () => {
    expect(describeOverlayShell()).toEqual({ canvasCount: 1, status: "ready" });
  });
});
