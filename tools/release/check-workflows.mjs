// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { readFileSync, readdirSync } from "node:fs";
import { basename, join } from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));
const workflowDirectory = join(repository, ".github", "workflows");
for (const name of readdirSync(workflowDirectory).filter((entry) => /\.ya?ml$/.test(entry))) {
  const contents = readFileSync(join(workflowDirectory, name), "utf8");
  for (const match of contents.matchAll(/^\s*uses:\s*([^\s#]+)(?:\s+#.*)?$/gm)) {
    const reference = match[1];
    if (reference.startsWith("./")) continue;
    const revision = reference.slice(reference.lastIndexOf("@") + 1);
    if (!/^[0-9a-f]{40}$/.test(revision)) {
      throw new Error(`${name} uses a mutable action reference: ${reference}`);
    }
  }
  if (/gh\s+release\s+upload[^\n]*--clobber/.test(contents)) {
    throw new Error(`${name} can overwrite immutable release assets`);
  }
}
process.stdout.write(`verified immutable action/release references in ${basename(workflowDirectory)}\n`);
