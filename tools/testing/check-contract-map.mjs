import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const ALLOWED_CATEGORIES = new Set([
  "automatic",
  "visual",
  "platform",
  "performance",
  "installation",
  "signing",
  "release",
]);

const CATEGORY_REQUIREMENTS = {
  visual: new Set([
    "CAP-05",
    "SEL-02",
    "UI-01",
    "UI-02",
    "UI-03",
    "UI-04",
    "ANN-01",
    "ANN-03",
    "ANN-05",
    "ANN-06",
    "ANN-08",
    "IMG-01",
    "QA-02",
  ]),
  platform: new Set([
    "APP-01",
    "APP-02",
    "APP-04",
    "CAP-01",
    "CAP-02",
    "CAP-03",
    "CAP-04",
    "ANN-05",
    "ANN-06",
    "OUT-01",
    "OUT-03",
    "OUT-04",
    "CFG-02",
    "ERR-01",
    "QA-03",
  ]),
  performance: new Set(["ANN-08", "PERF-01", "QA-02", "QA-03"]),
  installation: new Set(["DIST-01", "DIST-02", "DIST-03", "DIST-04", "QA-03"]),
  signing: new Set(["DIST-03", "DIST-04", "QA-04"]),
};

export function contractMustClauses(markdown) {
  const clauses = [];
  const pattern = /^### ([A-Z]+-[0-9]+) (.+?)（MUST）\s*$/gmu;
  for (const match of markdown.matchAll(pattern)) {
    clauses.push({ id: match[1], title: match[2] });
  }
  return clauses;
}

function loadJson(path) {
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    throw new Error(`${path} must be JSON-compatible YAML: ${error.message}`);
  }
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

export function validateContractMap({ root, contractPath, mapPath }) {
  const clauses = contractMustClauses(readFileSync(contractPath, "utf8"));
  const expected = new Map(clauses.map((clause) => [clause.id, clause.title]));
  const document = loadJson(mapPath);
  assert(document.schema_version === 1, "contract map schema_version must be 1");
  assert(Array.isArray(document.contracts), "contract map contracts must be an array");

  const seen = new Set();
  for (const contract of document.contracts) {
    assert(typeof contract.id === "string", "every contract must have an id");
    assert(!seen.has(contract.id), `duplicate contract id: ${contract.id}`);
    seen.add(contract.id);
    assert(expected.has(contract.id), `unexpected non-MUST contract id: ${contract.id}`);
    assert(
      contract.title === expected.get(contract.id),
      `${contract.id} title does not match the equivalence contract`,
    );
    assert(contract.status === "required", `${contract.id} status must be required`);
    assert(
      Array.isArray(contract.implementation) && contract.implementation.length > 0,
      `${contract.id} must reference current implementation`,
    );
    for (const relative of contract.implementation) {
      assert(typeof relative === "string" && relative.length > 0, `${contract.id} has an invalid implementation path`);
      assert(existsSync(resolve(root, relative)), `${contract.id} implementation path is missing: ${relative}`);
    }

    assert(
      Array.isArray(contract.evidence) && contract.evidence.length > 0,
      `${contract.id} must have evidence`,
    );
    const categories = new Set();
    let hasCurrentImplementationEvidence = false;
    for (const evidence of contract.evidence) {
      assert(ALLOWED_CATEGORIES.has(evidence.category), `${contract.id} has invalid evidence category: ${evidence.category}`);
      categories.add(evidence.category);
      assert(
        evidence.status === "verified" || evidence.status === "pending-external",
        `${contract.id} evidence status must be verified or pending-external`,
      );
      assert(typeof evidence.path === "string" && evidence.path.length > 0, `${contract.id} evidence needs a path`);
      const evidencePath = resolve(root, evidence.path);
      assert(existsSync(evidencePath), `${contract.id} evidence path is missing: ${evidence.path}`);
      assert(typeof evidence.contains === "string" && evidence.contains.length > 0, `${contract.id} evidence needs an exact anchor`);
      assert(
        readFileSync(evidencePath, "utf8").includes(evidence.contains),
        `${contract.id} evidence anchor is missing from ${evidence.path}: ${evidence.contains}`,
      );
      if (evidence.status === "verified") {
        hasCurrentImplementationEvidence = true;
      } else {
        assert(/^#[0-9]+$/.test(evidence.issue ?? ""), `${contract.id} pending evidence must name a GitHub issue`);
        assert(evidence.category !== "automatic", `${contract.id} automatic evidence cannot be pending-external`);
      }
    }
    assert(hasCurrentImplementationEvidence, `${contract.id} needs at least one verified current evidence anchor`);
    for (const [category, ids] of Object.entries(CATEGORY_REQUIREMENTS)) {
      if (ids.has(contract.id)) {
        assert(categories.has(category), `${contract.id} requires ${category} evidence`);
      }
    }
  }

  for (const id of expected.keys()) {
    assert(seen.has(id), `missing MUST contract id: ${id}`);
  }
  assert(seen.size === expected.size, `expected ${expected.size} MUST contracts, found ${seen.size}`);
  return { contractCount: seen.size };
}

const scriptPath = fileURLToPath(import.meta.url);
if (process.argv[1] && resolve(process.argv[1]) === scriptPath) {
  const root = resolve(dirname(scriptPath), "../..");
  const result = validateContractMap({
    root,
    contractPath: resolve(root, "docs/research/snaploom-feature-equivalence-contract.md"),
    mapPath: resolve(root, "testing/contract-map/contracts.yml"),
  });
  console.log(`Contract map valid: ${result.contractCount} MUST contracts.`);
}
