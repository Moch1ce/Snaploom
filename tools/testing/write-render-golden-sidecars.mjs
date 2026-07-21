// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));
const overlay = join(repository, "web", "overlay-editor");
const goldenDirectory = join(repository, "testing", "goldens", "render");
const check = process.argv.includes("--check");
const fixtures = [
  { name: "rectangle", width: 160, height: 96 },
  { name: "arrow", width: 160, height: 96 },
  { name: "text", width: 160, height: 96 },
  { name: "mosaic", width: 160, height: 96 },
  { name: "composite-overlap", width: 160, height: 96 },
  { name: "dpi-100", width: 80, height: 48 },
  { name: "dpi-125", width: 100, height: 60 },
  { name: "dpi-150", width: 120, height: 72 },
  { name: "dpi-175", width: 140, height: 84 },
  { name: "dpi-200", width: 160, height: 96 },
];
const harness = readFileSync(join(overlay, "tests", "visual-harness.ts"));
const overlayRequire = createRequire(join(overlay, "package.json"));
const playwrightTestPackagePath = overlayRequire.resolve(
  "@playwright/test/package.json",
);
const playwrightRequire = createRequire(playwrightTestPackagePath);
const playwrightCorePackagePath = createRequire(
  playwrightRequire.resolve("playwright/package.json"),
).resolve("playwright-core/package.json");
const playwrightPackage = JSON.parse(
  readFileSync(playwrightTestPackagePath),
);
const browsers = JSON.parse(
  readFileSync(join(dirname(playwrightCorePackagePath), "browsers.json")),
);
const chromium = browsers.browsers.find(
  (browser) => browser.name === "chromium-headless-shell",
);
if (!chromium) throw new Error("pinned Chromium headless-shell revision is unavailable");

for (const fixture of fixtures) {
  const golden = `${fixture.name}-final-png.png`;
  readFileSync(join(goldenDirectory, golden));
  const fixtureSha256 = createHash("sha256")
    .update(fixture.name)
    .update("\0")
    .update(harness)
    .digest("hex");
  const metadata = {
    schemaVersion: 1,
    golden,
    generator: {
      playwright: playwrightPackage.version,
      browser: chromium.name,
      revision: chromium.revision,
    },
    fixture: { name: fixture.name, sha256: fixtureSha256 },
    physicalSize: { width: fixture.width, height: fixture.height },
    contracts: ["QA-01", "QA-02", "ANN-01", "IMG-01"],
  };
  const expected = `${JSON.stringify(metadata, null, 2)}\n`;
  const sidecar = join(goldenDirectory, `${golden}.json`);
  if (check) {
    const actual = readFileSync(sidecar, "utf8");
    if (actual !== expected) {
      throw new Error(`stale render golden sidecar: ${golden}.json`);
    }
  } else {
    writeFileSync(sidecar, expected);
  }
}

process.stdout.write(
  `${check ? "verified" : "updated"} ${fixtures.length} render golden sidecars\n`,
);
