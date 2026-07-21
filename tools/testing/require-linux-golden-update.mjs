// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync } from "node:fs";

let osRelease = "";
try {
  osRelease = readFileSync("/etc/os-release", "utf8");
} catch {
  // The platform check below reports one stable remediation message.
}

if (
  process.platform !== "linux" ||
  !/^VERSION_ID=["']?24\.04["']?$/m.test(osRelease)
) {
  throw new Error(
    "render goldens must be updated on Ubuntu 24.04 with the pinned Playwright browser",
  );
}

process.stdout.write("render golden update environment is Ubuntu 24.04\n");
