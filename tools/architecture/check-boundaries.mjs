import { readFile, readdir } from "node:fs/promises";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const forbidden = ["../product", "../../product", "../../../product", "GPL-3.0"];
const checked = [];

async function walk(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name === "target") {
      continue;
    }
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      await walk(path);
    } else if (
      entry.name === "Cargo.toml" ||
      /\.(rs|h|hpp|c|cpp|proto|cs|swift|ts|js|json)$/.test(entry.name)
    ) {
      checked.push(path);
    }
  }
}

await walk(join(root, "sdk"));

const violations = [];
for (const url of checked) {
  const content = await readFile(url, "utf8");
  for (const marker of forbidden) {
    if (content.includes(marker)) {
      violations.push(`${relative(root, url)}: ${marker}`);
    }
  }
}

if (violations.length > 0) {
  console.error("Apache SDK boundary violation:\n" + violations.join("\n"));
  process.exit(1);
}

console.log(`Apache SDK boundary OK (${checked.length} source/manifest files checked)`);
