// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync } from "node:fs";

const paths = process.argv.slice(2);
if (paths.length === 0) throw new Error("no CycloneDX SBOM was generated");
for (const path of paths) {
  const document = JSON.parse(readFileSync(path, "utf8"));
  if (document.bomFormat !== "CycloneDX" || !Array.isArray(document.components)) {
    throw new Error(`${path} is not a CycloneDX component document`);
  }
  for (const component of document.components) {
    if (!component.name || !component.version) {
      throw new Error(`${path} contains an unidentified component`);
    }
  }
}
