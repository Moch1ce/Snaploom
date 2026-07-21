import { describe, expect, it } from "vitest";
import { routePointerPath } from "../src/app";

describe("root capture-phase pointer isolation", () => {
  it.each(["toolbar", "separator", "settings", "textarea", "size-label"])(
    "keeps %s pointer gestures out of the screenshot surface",
    (role) => {
      expect(routePointerPath(0, [role, "root"])).toBe("ui");
    },
  );
  it("routes canvas left click and any right click deterministically", () => {
    expect(routePointerPath(0, ["canvas", "root"])).toBe("surface");
    expect(routePointerPath(2, ["toolbar", "root"])).toBe("cancel");
  });
});
