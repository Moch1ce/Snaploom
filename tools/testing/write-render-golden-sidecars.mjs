// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));
const overlay = join(repository, "web", "overlay-editor");
const goldenDirectory = join(repository, "testing", "goldens");
const check = process.argv.includes("--check");
const fixtureManifestPath = join(goldenDirectory, "fixtures.json");
const fixtureManifest = readFileSync(fixtureManifestPath);
const fixtures = JSON.parse(fixtureManifest);
const renderHarness = readFileSync(join(overlay, "tests", "visual-harness.ts"));
const uiHarness = readFileSync(
  join(overlay, "tests", "ui-goldens.visual.spec.ts"),
);
const uiFonts = readFileSync(join(overlay, "tests", "ui-golden-fonts.ts"));
const overlayRequire = createRequire(join(overlay, "package.json"));
const playwrightTestPackagePath = overlayRequire.resolve(
  "@playwright/test/package.json",
);
const playwrightRequire = createRequire(playwrightTestPackagePath);
const { chromium: playwrightChromium } = playwrightRequire("playwright");
const playwrightCorePackagePath = createRequire(
  playwrightRequire.resolve("playwright/package.json"),
).resolve("playwright-core/package.json");
const playwrightPackage = JSON.parse(readFileSync(playwrightTestPackagePath));
const browsers = JSON.parse(
  readFileSync(join(dirname(playwrightCorePackagePath), "browsers.json")),
);
const chromium = browsers.browsers.find(
  (browser) => browser.name === "chromium-headless-shell",
);
if (!chromium) {
  throw new Error("pinned Chromium headless-shell revision is unavailable");
}
const chromiumVersion = execFileSync(
  playwrightChromium.executablePath(),
  ["--version"],
  { encoding: "utf8" },
).trim();

const suites = [
  {
    directory: "render",
    fixtures: fixtures.render,
    harness: renderHarness,
    golden: (fixture) => `${fixture.name}-final-png.png`,
    contracts: ["QA-01", "QA-02", "ANN-01", "IMG-01"],
  },
  {
    directory: "ui",
    fixtures: fixtures.ui,
    harness: Buffer.concat([uiHarness, uiFonts]),
    golden: (fixture) => `${fixture.name}.png`,
    contracts: ["UI-01", "UI-02", "UI-03", "UI-04", "ANN-06", "QA-02"],
  },
];

let count = 0;
for (const suite of suites) {
  for (const fixture of suite.fixtures) {
    const golden = suite.golden(fixture);
    const directory = join(goldenDirectory, suite.directory);
    const png = readFileSync(join(directory, golden));
    if (
      png.length < 24 ||
      png.subarray(0, 8).toString("hex") !== "89504e470d0a1a0a"
    ) {
      throw new Error(`invalid PNG golden: ${suite.directory}/${golden}`);
    }
    const actualWidth = png.readUInt32BE(16);
    const actualHeight = png.readUInt32BE(20);
    if (actualWidth !== fixture.width || actualHeight !== fixture.height) {
      throw new Error(
        `golden size mismatch: ${suite.directory}/${golden} is ${actualWidth}x${actualHeight}, expected ${fixture.width}x${fixture.height}`,
      );
    }
    const fixtureSha256 = createHash("sha256")
      .update(fixture.name)
      .update("\0")
      .update(fixtureManifest)
      .update("\0")
      .update(suite.harness)
      .digest("hex");
    const fixtureMetadata = { name: fixture.name, sha256: fixtureSha256 };
    if (fixture.scale !== undefined) fixtureMetadata.scale = fixture.scale;
    if (fixture.start !== undefined && fixture.end !== undefined) {
      fixtureMetadata.selection = { start: fixture.start, end: fixture.end };
    }
    const metadata = {
      schemaVersion: 1,
      golden,
      generator: {
        playwright: playwrightPackage.version,
        browser: chromium.name,
        revision: chromium.revision,
        version: chromiumVersion,
      },
      fixture: fixtureMetadata,
      physicalSize: { width: fixture.width, height: fixture.height },
      contracts: suite.contracts,
    };
    const expected = `${JSON.stringify(metadata, null, 2)}\n`;
    const sidecar = join(directory, `${golden}.json`);
    if (check) {
      const actual = readFileSync(sidecar, "utf8");
      if (actual !== expected) {
        throw new Error(
          `stale golden sidecar: ${suite.directory}/${golden}.json`,
        );
      }
    } else {
      writeFileSync(sidecar, expected);
    }
    count += 1;
  }
}

process.stdout.write(
  `${check ? "verified" : "updated"} ${count} golden sidecars\n`,
);
