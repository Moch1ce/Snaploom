import { describe, expect, it } from "vitest";
import { overlayMessage, overlayResourceKeys } from "../src/localization";

describe("overlay localization contract", () => {
  it("keeps Chinese and English resource keys aligned", () => {
    expect(overlayResourceKeys("zh-cn")).toEqual(overlayResourceKeys("en"));
  });

  it("localizes toolbar, annotation settings, and output failures", () => {
    for (const language of ["zh-cn", "en"] as const) {
      for (const key of [
        "toolbar",
        "rectangle",
        "annotationStyle",
        "clipboardWriteFailed",
        "saveFailed",
      ] as const) {
        expect(typeof overlayMessage(language, key)).toBe("string");
      }
    }
  });
});
