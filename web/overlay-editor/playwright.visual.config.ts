import { defineConfig } from "@playwright/test";
import { fileURLToPath } from "node:url";

const goldenPath = fileURLToPath(
  new URL("../../testing/goldens/{arg}{ext}", import.meta.url),
);

export default defineConfig({
  testDir: "./tests",
  testMatch: "**/*.visual.spec.ts",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? "github" : "list",
  snapshotPathTemplate: goldenPath,
  use: {
    baseURL: "http://127.0.0.1:1423",
    browserName: "chromium",
    headless: true,
    viewport: { width: 320, height: 240 },
    deviceScaleFactor: 1,
    colorScheme: "light",
    locale: "en-US",
    timezoneId: "UTC",
    launchOptions: { args: ["--font-render-hinting=none"] },
  },
  webServer: {
    command: "pnpm exec vite --config vite.visual.config.ts",
    url: "http://127.0.0.1:1423/tests/visual-harness.html",
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
