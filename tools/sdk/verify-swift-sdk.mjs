import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));

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
    throw new Error(result.stderr || result.stdout || `${command} failed`);
  }
  return result.stdout.trim();
}

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}

if (process.platform !== "darwin" || process.arch !== "arm64") {
  throw new Error("Swift XCFramework verification requires macOS arm64");
}

const archiveArgument = argument("archive");
if (!archiveArgument) {
  throw new Error("--archive is required");
}
const archive = resolve(archiveArgument);
const version = argument("version", "0.1.0");
const skipTSan = process.argv.includes("--skip-tsan");
const skipManifestChecksum = process.argv.includes("--skip-manifest-checksum");
const expectedChecksum = run("swift", ["package", "compute-checksum", archive]);
const manifest = readFileSync(join(repository, "Package.swift"), "utf8");
const checksumMatch = manifest.match(/let snaploomBinaryChecksum = "([a-f0-9]{64})"/);
const manifestChecksumMismatch =
  !skipManifestChecksum &&
  (!checksumMatch || checksumMatch[1] !== expectedChecksum);
const expectedUrl = `https://github.com/Moch1ce/Snaploom/releases/download/v${version}/CSnaploomCapture-${version}.xcframework.zip`;
if (!manifest.includes(expectedUrl)) {
  throw new Error(`Package.swift is missing release URL ${expectedUrl}`);
}

const temporary = mkdtempSync(join(tmpdir(), "snaploom-swift-consumer-"));
try {
  const extracted = join(temporary, "extracted");
  mkdirSync(extracted, { recursive: true });
  run("python3", [
    "-c",
    "import shutil,sys; shutil.unpack_archive(sys.argv[1],sys.argv[2])",
    archive,
    extracted,
  ]);
  const framework = join(extracted, "CSnaploomCapture.xcframework");
  if (!existsSync(framework)) {
    throw new Error("archive root does not contain CSnaploomCapture.xcframework");
  }
  for (const relative of [
    "LICENSES/Apache-2.0.txt",
    "NOTICE",
    "THIRD-PARTY-NOTICES.txt",
    "sbom.cdx.json",
  ]) {
    if (!existsSync(join(extracted, relative))) {
      throw new Error(`XCFramework archive is missing ${relative}`);
    }
  }

  const info = JSON.parse(
    run("plutil", ["-convert", "json", "-o", "-", join(framework, "Info.plist")]),
  );
  if (!Array.isArray(info.AvailableLibraries) || info.AvailableLibraries.length !== 1) {
    throw new Error("XCFramework must contain exactly one library slice");
  }
  const slice = info.AvailableLibraries[0];
  if (
    slice.SupportedPlatform !== "macos" ||
    JSON.stringify(slice.SupportedArchitectures) !== JSON.stringify(["arm64"])
  ) {
    throw new Error("XCFramework must contain only the macOS arm64 slice");
  }
  const sliceRoot = join(framework, slice.LibraryIdentifier);
  const dylib = join(sliceRoot, slice.LibraryPath);
  const headers = join(sliceRoot, slice.HeadersPath);
  const header = join(headers, "snaploom_capture.h");
  const moduleMap = join(headers, "module.modulemap");
  for (const path of [dylib, header, moduleMap]) {
    if (!existsSync(path)) {
      throw new Error(`XCFramework is missing ${path.slice(framework.length + 1)}`);
    }
  }
  const expectedFiles = [
    "Info.plist",
    `${slice.LibraryIdentifier}/${slice.LibraryPath}`,
    `${slice.LibraryIdentifier}/${slice.HeadersPath}/module.modulemap`,
    `${slice.LibraryIdentifier}/${slice.HeadersPath}/snaploom_capture.h`,
  ].sort();
  const actualFiles = walk(framework)
    .map((path) => path.slice(framework.length + 1))
    .sort();
  if (JSON.stringify(actualFiles) !== JSON.stringify(expectedFiles)) {
    throw new Error(`unexpected XCFramework contents: ${actualFiles.join(", ")}`);
  }

  if (run("lipo", ["-archs", dylib]) !== "arm64") {
    throw new Error("XCFramework dylib is not arm64-only");
  }
  if (!run("otool", ["-D", dylib]).includes("@rpath/libsnaploom_capture.dylib")) {
    throw new Error("XCFramework dylib has an unexpected install name");
  }
  if (!/LC_BUILD_VERSION[\s\S]*?minos 14\.0/.test(run("otool", ["-l", dylib]))) {
    throw new Error("XCFramework dylib does not declare macOS 14.0 minimum");
  }
  const expectedSymbols = readFileSync(
    join(repository, "sdk", "c-abi", "abi-symbols-v1.txt"),
    "utf8",
  )
    .trim()
    .split(/\r?\n/)
    .sort();
  const actualSymbols = [
    ...new Set(
      (run("nm", ["-gU", dylib]).match(/_snaploom_capture_[a-z0-9_]+_v1/g) ?? []).map(
        (symbol) => symbol.slice(1),
      ),
    ),
  ].sort();
  if (JSON.stringify(actualSymbols) !== JSON.stringify(expectedSymbols)) {
    throw new Error("XCFramework does not expose exactly the seven C ABI symbols");
  }
  const sourceHeader = readFileSync(
    join(repository, "sdk", "c", "include", "snaploom", "snaploom_capture.h"),
  );
  if (!readFileSync(header).equals(sourceHeader)) {
    throw new Error("XCFramework header differs from the authoritative C header");
  }
  if (!readFileSync(moduleMap, "utf8").includes("module CSnaploomCapture")) {
    throw new Error("XCFramework module map does not declare CSnaploomCapture");
  }
  const binaryStrings = run("strings", [dylib]);
  if (
    binaryStrings.includes(repository) ||
    binaryStrings.includes("/home/runner/") ||
    binaryStrings.includes("/Users/runner/")
  ) {
    throw new Error("XCFramework dylib leaks an absolute build path");
  }
  for (const path of walk(framework)) {
    const relative = path.slice(framework.length + 1);
    if (/(?:capture[-_ ]?host|tauri|capture[-_ ]?session|x86_64|iphone|ios)/i.test(relative)) {
      throw new Error(`forbidden asset in XCFramework: ${relative}`);
    }
  }

  const localPackage = join(temporary, "package");
  mkdirSync(localPackage, { recursive: true });
  cpSync(framework, join(localPackage, "CSnaploomCapture.xcframework"), {
    recursive: true,
  });
  cpSync(join(repository, "sdk", "swift", "Sources"), join(localPackage, "Sources"), {
    recursive: true,
  });
  cpSync(join(repository, "sdk", "swift", "Tests"), join(localPackage, "Tests"), {
    recursive: true,
  });
  writeFileSync(
    join(localPackage, "Package.swift"),
    [
      "// swift-tools-version: 5.9",
      "import PackageDescription",
      "let package = Package(",
      '  name: "SnaploomCapture",',
      "  platforms: [.macOS(.v14)],",
      '  products: [.library(name: "SnaploomCapture", targets: ["SnaploomCapture"])],',
      "  targets: [",
      '    .binaryTarget(name: "CSnaploomCapture", path: "CSnaploomCapture.xcframework"),',
      '    .target(name: "SnaploomCapture", dependencies: ["CSnaploomCapture"]),',
      '    .testTarget(name: "SnaploomCaptureTests", dependencies: ["SnaploomCapture"]),',
      "  ]",
      ")",
      "",
    ].join("\n"),
  );

  run("swift", [
    "test",
    "--package-path",
    localPackage,
    "-Xswiftc",
    "-strict-concurrency=complete",
    "-Xswiftc",
    "-warnings-as-errors",
  ]);
  if (!skipTSan) {
    run("swift", [
      "test",
      "--package-path",
      localPackage,
      "--sanitize=thread",
      "-Xswiftc",
      "-strict-concurrency=complete",
    ]);
  }
  run("xcodebuild", [
    "-scheme",
    "SnaploomCapture",
    "-destination",
    "platform=macOS,arch=arm64",
    "-derivedDataPath",
    join(temporary, "DerivedData"),
    "build",
  ], { cwd: localPackage });

  const consumer = join(temporary, "consumer");
  mkdirSync(join(consumer, "Sources", "Consumer"), { recursive: true });
  writeFileSync(
    join(consumer, "Package.swift"),
    [
      "// swift-tools-version: 5.9",
      "import PackageDescription",
      "let package = Package(",
      '  name: "Consumer",',
      "  platforms: [.macOS(.v14)],",
      `  dependencies: [.package(path: ${JSON.stringify(localPackage)})],`,
      "  targets: [.executableTarget(",
      '    name: "Consumer",',
      '    dependencies: [.product(name: "SnaploomCapture", package: "package")]',
      "  )]",
      ")",
      "",
    ].join("\n"),
  );
  writeFileSync(
    join(consumer, "Sources", "Consumer", "main.swift"),
    [
      "import SnaploomCapture",
      "let options = CaptureOptions(disableClipboard: true)",
      "let client = try CaptureClient()",
      "defer { try? client.close() }",
      'print("SnaploomCapture \\(client.runtimeVersion.nativeSemver) consumer ready: \\(options.disableClipboard)")',
      "",
    ].join("\n"),
  );
  run("swift", ["build", "--package-path", consumer]);
  run("swift", ["run", "--package-path", consumer, "Consumer"]);

  const consumerBin = run("swift", [
    "build",
    "--package-path",
    consumer,
    "--show-bin-path",
  ]);
  const consumerApp = join(temporary, "SnaploomConsumer.app");
  const consumerMacOS = join(consumerApp, "Contents", "MacOS");
  const consumerFrameworks = join(consumerApp, "Contents", "Frameworks");
  mkdirSync(consumerMacOS, { recursive: true });
  mkdirSync(consumerFrameworks, { recursive: true });
  const consumerExecutable = join(consumerMacOS, "SnaploomConsumer");
  const embeddedDylib = join(consumerFrameworks, "libsnaploom_capture.dylib");
  cpSync(join(consumerBin, "Consumer"), consumerExecutable);
  cpSync(dylib, embeddedDylib);
  const consumerLoadCommands = run("otool", ["-l", consumerExecutable]);
  const consumerRpaths = [
    ...consumerLoadCommands.matchAll(/cmd LC_RPATH[\s\S]*?path (.+?) \(offset/g),
  ].map((match) => match[1]);
  for (const rpath of consumerRpaths.filter((path) => path.startsWith("/"))) {
    run("install_name_tool", ["-delete_rpath", rpath, consumerExecutable]);
  }
  if (!consumerRpaths.includes("@executable_path/../Frameworks")) {
    run("install_name_tool", [
      "-add_rpath",
      "@executable_path/../Frameworks",
      consumerExecutable,
    ]);
  }
  writeFileSync(
    join(consumerApp, "Contents", "Info.plist"),
    [
      '<?xml version="1.0" encoding="UTF-8"?>',
      '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">',
      '<plist version="1.0"><dict>',
      "<key>CFBundleIdentifier</key><string>org.snaploom.sdk.consumer</string>",
      "<key>CFBundleExecutable</key><string>SnaploomConsumer</string>",
      "<key>CFBundlePackageType</key><string>APPL</string>",
      "<key>LSMinimumSystemVersion</key><string>14.0</string>",
      "</dict></plist>",
      "",
    ].join("\n"),
  );
  run("codesign", ["--force", "--sign", "-", embeddedDylib]);
  run("codesign", ["--force", "--sign", "-", consumerApp]);
  run("codesign", ["--verify", "--deep", "--strict", "--verbose=2", consumerApp]);
  run(consumerExecutable, []);

  const damaged = join(temporary, "damaged.zip");
  cpSync(archive, damaged);
  const bytes = readFileSync(damaged);
  bytes[bytes.length - 1] ^= 1;
  writeFileSync(damaged, bytes);
  if (run("swift", ["package", "compute-checksum", damaged]) === expectedChecksum) {
    throw new Error("checksum mismatch negative path did not change the digest");
  }
  if (manifestChecksumMismatch) {
    throw new Error(
      `Package.swift checksum mismatch: expected ${expectedChecksum}, found ${checksumMatch?.[1] ?? "missing"}`,
    );
  }

  process.stdout.write(
    `Swift package, strict concurrency${skipTSan ? "" : ", TSan"}, Xcode, and embedded/re-signed app consumer passed: ${basename(archive)}\n`,
  );
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
