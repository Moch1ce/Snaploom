import { describe, expect, it } from "vitest";
import { desktopStatus } from "../src/app";

describe("desktop shell", () => {
  it("reports a ready state", () => {
    expect(desktopStatus()).toContain("已就绪");
  });
});
