import { describe, expect, it } from "vitest";
import {
  languageFromLocale,
  resourceKeys,
  settingsControlKeys,
  updateMessage,
} from "../src/app";

describe("desktop settings contract", () => {
  it("falls back to English for unsupported system languages", () => {
    expect(languageFromLocale("zh-Hans-CN")).toBe("zh-cn");
    expect(languageFromLocale("fr-FR")).toBe("en");
    expect(languageFromLocale(undefined)).toBe("en");
  });

  it("keeps complete key parity between Chinese and English", () => {
    expect(resourceKeys("zh-cn")).toEqual(resourceKeys("en"));
  });

  it("does not expose a theme setting because the product is fixed light", () => {
    expect(settingsControlKeys).not.toContain("theme");
  });

  it("renders typed manual update states", () => {
    expect(updateMessage("en", { state: "current" })).toContain("up to date");
    expect(
      updateMessage("zh-cn", {
        state: "available",
        version: "v1.2.3",
        releaseUrl:
          "https://github.com/Moch1ce/Snaploom/releases/tag/v1.2.3",
      }),
    ).toContain("v1.2.3");
    expect(updateMessage("en", { state: "rateLimited" })).toContain(
      "rate limited",
    );
  });
});
