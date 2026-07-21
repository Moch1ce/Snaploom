import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { validateContractMap } from "./check-contract-map.mjs";

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "snaploom-contract-map-"));
  mkdirSync(join(root, "docs"));
  mkdirSync(join(root, "src"));
  mkdirSync(join(root, "tests"));
  writeFileSync(join(root, "docs", "contract.md"), "### EX-01 Example（MUST）\n");
  writeFileSync(join(root, "src", "example.rs"), "pub fn example() {}\n");
  writeFileSync(join(root, "tests", "example.rs"), "fn behavior_is_preserved() {}\n");
  const document = {
    schema_version: 1,
    contracts: [
      {
        id: "EX-01",
        title: "Example",
        status: "required",
        implementation: ["src/example.rs"],
        evidence: [
          {
            category: "automatic",
            status: "verified",
            path: "tests/example.rs",
            contains: "behavior_is_preserved",
          },
        ],
      },
    ],
  };
  const mapPath = join(root, "contracts.yml");
  writeFileSync(mapPath, JSON.stringify(document));
  return { root, mapPath, contractPath: join(root, "docs", "contract.md") };
}

test("accepts an exact MUST-to-current-evidence map", () => {
  const paths = fixture();
  try {
    assert.deepEqual(validateContractMap(paths), { contractCount: 1 });
  } finally {
    rmSync(paths.root, { recursive: true });
  }
});

test("rejects missing, extra, duplicate, and renamed evidence anchors", () => {
  for (const mutation of [
    (document) => document.contracts.splice(0),
    (document) => document.contracts.push({ ...document.contracts[0], id: "EX-02" }),
    (document) => document.contracts.push(document.contracts[0]),
    (document) => (document.contracts[0].evidence[0].contains = "renamed_test"),
  ]) {
    const paths = fixture();
    try {
      const document = JSON.parse(readFileSync(paths.mapPath, "utf8"));
      mutation(document);
      writeFileSync(paths.mapPath, JSON.stringify(document));
      assert.throws(() => validateContractMap(paths));
    } finally {
      rmSync(paths.root, { recursive: true });
    }
  }
});

test("does not allow an external placeholder to replace current evidence", () => {
  const paths = fixture();
  try {
    const document = JSON.parse(readFileSync(paths.mapPath, "utf8"));
    document.contracts[0].evidence[0] = {
      category: "platform",
      status: "pending-external",
      issue: "#51",
      path: "tests/example.rs",
      contains: "behavior_is_preserved",
    };
    writeFileSync(paths.mapPath, JSON.stringify(document));
    assert.throws(() => validateContractMap(paths), /verified current evidence/);
  } finally {
    rmSync(paths.root, { recursive: true });
  }
});
