import { readFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const root = fileURLToPath(new URL("../../", import.meta.url));
const extension = process.platform === "win32" ? "dll" : process.platform === "darwin" ? "dylib" : "so";
const prefix = process.platform === "win32" ? "" : "lib";
const library = process.argv[2] ?? join(root, "sdk", "target", "release", `${prefix}snaploom_capture.${extension}`);
const expected = (await readFile(join(root, "sdk", "c-abi", "abi-symbols-v1.txt"), "utf8"))
  .trim()
  .split("\n")
  .sort();

const command = process.platform === "win32" ? "dumpbin" : "nm";
const args = process.platform === "win32"
  ? ["/exports", library]
  : process.platform === "darwin"
    ? ["-gU", library]
    : ["-D", "--defined-only", library];
const result = spawnSync(command, args, { encoding: "utf8" });
if (result.status !== 0) {
  console.error(result.stderr || result.stdout || `${command} failed`);
  process.exit(result.status ?? 1);
}

const actual = [...new Set(
  result.stdout
    .match(/snaploom_capture_[a-z0-9_]+_v1/g) ?? [],
)].sort();
if (JSON.stringify(actual) !== JSON.stringify(expected)) {
  console.error(`C ABI symbol mismatch\nexpected: ${expected.join(", ")}\nactual:   ${actual.join(", ")}`);
  process.exit(1);
}
console.log(`C ABI symbol allowlist OK (${actual.length} symbols)`);
