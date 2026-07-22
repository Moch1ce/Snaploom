// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));
const defaultRepositoryUrl = "https://github.com/Moch1ce/Snaploom.git";

function argument(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    encoding: "utf8",
    stdio: "pipe",
    ...options,
  });
  if (result.status !== 0) {
    throw new Error(
      `${command} ${args.join(" ")} failed\n${result.stdout}\n${result.stderr}`,
    );
  }
  return result.stdout.trim();
}

export function verifySwiftReleaseManifest(contents, { version, checksum }) {
  const expectedUrl =
    `https://github.com/Moch1ce/Snaploom/releases/download/v${version}/` +
    `CSnaploomCapture-${version}.xcframework.zip`;
  const checksumMatches = [
    ...contents.matchAll(
      /let\s+snaploomBinaryChecksum\s*=\s*"([0-9a-f]+)"/g,
    ),
  ];
  const binaryTargetMatches = [
    ...contents.matchAll(
      /\.binaryTarget\(\s*name:\s*"CSnaploomCapture"\s*,\s*url:\s*"([^"]+)"\s*,\s*checksum:\s*snaploomBinaryChecksum\s*,?\s*\)/gs,
    ),
  ];
  if (
    !/^\d+\.\d+\.\d+$/.test(version) ||
    !/^[0-9a-f]{64}$/.test(checksum) ||
    checksumMatches.length !== 1 ||
    checksumMatches[0][1] !== checksum
  ) {
    throw new Error("Package.swift checksum does not match the public XCFramework archive");
  }
  if (
    binaryTargetMatches.length !== 1 ||
    (contents.match(/\.binaryTarget\s*\(/g) ?? []).length !== 1 ||
    binaryTargetMatches[0][1] !== expectedUrl
  ) {
    throw new Error("Package.swift binary target URL is not the exact public stable asset");
  }
}

export function verifyResolvedSwiftPackage(
  resolved,
  { version, commit, repositoryUrl = defaultRepositoryUrl },
) {
  const pin = resolved?.pins?.[0];
  const validSchema =
    resolved?.version === 2 ||
    (resolved?.version === 3 && /^[0-9a-f]{64}$/.test(resolved?.originHash ?? ""));
  if (
    !validSchema ||
    resolved?.pins?.length !== 1 ||
    pin?.identity !== "snaploom" ||
    pin?.kind !== "remoteSourceControl" ||
    pin?.location !== repositoryUrl ||
    pin?.state?.version !== version ||
    pin?.state?.revision !== commit ||
    !/^[0-9a-f]{40}$/.test(commit)
  ) {
    throw new Error("resolved package is not the exact public Snaploom tag commit");
  }
}

function consumerManifest(version, repositoryUrl) {
  return `// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "SnaploomStableConsumer",
    platforms: [.macOS(.v14)],
    products: [],
    dependencies: [
        .package(url: "${repositoryUrl}", exact: "${version}")
    ],
    targets: [
        .testTarget(
            name: "StableConsumerTests",
            dependencies: [
                .product(name: "SnaploomCapture", package: "snaploom")
            ]
        )
    ]
)
`;
}

const consumerTest = `import SnaploomCapture
import XCTest

final class StableConsumerTests: XCTestCase {
    func testPublicPackageLoadsCaptureRuntime() throws {
        let client = try CaptureClient()
        XCTAssertEqual(client.runtimeVersion.abiMajor, 1)
        try client.close()
    }
}
`;

if (
  process.argv[1] &&
  basename(process.argv[1]) === "verify-stable-swift-consumer.mjs"
) {
  const archiveArgument = argument("archive");
  const version = argument("version");
  const commit = argument("commit");
  const repositoryUrl = argument("repository-url", defaultRepositoryUrl);
  if (!archiveArgument || !version || !commit) {
    throw new Error("--archive, --version, and --commit are required");
  }
  const archive = resolve(archiveArgument);
  if (process.platform !== "darwin" || process.arch !== "arm64") {
    throw new Error("stable Swift package verification requires macOS arm64");
  }

  const checksum = run("swift", ["package", "compute-checksum", archive]);
  verifySwiftReleaseManifest(readFileSync(join(repository, "Package.swift"), "utf8"), {
    version,
    checksum,
  });

  const temporary = mkdtempSync(join(tmpdir(), "snaploom-swift-stable-consumer-"));
  try {
    const tests = join(temporary, "Tests", "StableConsumerTests");
    mkdirSync(tests, { recursive: true });
    writeFileSync(join(temporary, "Package.swift"), consumerManifest(version, repositoryUrl));
    writeFileSync(join(tests, "StableConsumerTests.swift"), consumerTest);

    run("swift", ["package", "resolve", "--package-path", temporary]);
    verifyResolvedSwiftPackage(
      JSON.parse(readFileSync(join(temporary, "Package.resolved"), "utf8")),
      { version, commit, repositoryUrl },
    );
    run("swift", ["build", "--package-path", temporary]);
    run("swift", [
      "test",
      "--package-path",
      temporary,
      "-Xswiftc",
      "-strict-concurrency=complete",
      "-Xswiftc",
      "-warnings-as-errors",
    ]);
    process.stdout.write(
      `verified public Swift package ${version} resolves ${commit} and builds/tests\n`,
    );
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}
