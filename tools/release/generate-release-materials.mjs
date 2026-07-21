// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { createHash } from "node:crypto";
import { lstatSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { assetsForVersion } from "./release-contract.mjs";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

const directory = resolve(argument("directory") ?? "release-payloads");
const version = argument("version");
if (!version) throw new Error("--version is required");
const sbomName = `snaploom-${version}-sbom.cdx.json`;
const noticeName = `snaploom-${version}-third-party-notices.txt`;
const materialContracts = assetsForVersion(version).filter(
  ({ name }) => name !== sbomName && name !== noticeName,
);
const actual = readdirSync(directory).sort();
const expected = materialContracts.map(({ name }) => name).sort();
if (JSON.stringify(actual) !== JSON.stringify(expected)) {
  throw new Error(`release materials require the exact pre-aggregate payload set\nexpected: ${expected.join(", ")}\nactual: ${actual.join(", ")}`);
}

const components = materialContracts.map((contract) => {
  const path = join(directory, contract.name);
  const stat = lstatSync(path);
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size === 0) {
    throw new Error(`invalid release material: ${contract.name}`);
  }
  return {
    type: contract.kind.includes("source") ? "application" : "file",
    "bom-ref": `release:${contract.name}`,
    name: contract.name,
    version,
    hashes: [
      {
        alg: "SHA-256",
        content: createHash("sha256").update(readFileSync(path)).digest("hex"),
      },
    ],
    licenses: contract.license.split(" AND ").map((id) => ({ license: { id } })),
    properties: [
      { name: "snaploom:asset-kind", value: contract.kind },
      { name: "snaploom:asset-platform", value: contract.platform },
      { name: "snaploom:file-bytes", value: String(stat.size) },
    ],
  };
});
const sbom = {
  bomFormat: "CycloneDX",
  specVersion: "1.5",
  version: 1,
  metadata: {
    component: {
      type: "application",
      "bom-ref": `pkg:github/Moch1ce/Snaploom@${version}`,
      name: "Snaploom Release",
      version,
      licenses: [
        { license: { id: "GPL-3.0-or-later" } },
        { license: { id: "Apache-2.0" } },
      ],
    },
  },
  components,
};
writeFileSync(join(directory, sbomName), `${JSON.stringify(sbom, null, 2)}\n`, "utf8");

const notices = [
  `Snaploom ${version} release asset license and notice index`,
  "",
  "Desktop and Capture Host assets are GPL-3.0-or-later products.",
  "Capture SDK assets are independent Apache-2.0 packages and do not include Capture Host.",
  "Each binary/package archive contains its own applicable LICENSE, NOTICE, third-party notice, and SBOM.",
  "The corresponding-source archive contains the exact source and locked build inputs for this release.",
  "",
  ...materialContracts.map(({ name, kind, license }) => `${name} | ${kind} | ${license}`),
  "",
].join("\n");
writeFileSync(join(directory, noticeName), notices, "utf8");
process.stdout.write(`generated ${basename(sbomName)} and ${basename(noticeName)}\n`);
