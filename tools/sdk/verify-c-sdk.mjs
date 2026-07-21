import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { findDumpbin } from "./windows-tools.mjs";

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

function pythonCommand() {
  const candidates =
    process.platform === "win32"
      ? ["python", "python3"]
      : ["python3", "python"];
  for (const candidate of candidates) {
    if (spawnSync(candidate, ["--version"], { encoding: "utf8" }).status === 0) {
      return candidate;
    }
  }
  throw new Error("Python is required to extract the SDK archive");
}

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}

const archiveArgument = argument("archive");
if (!archiveArgument) {
  throw new Error("--archive is required");
}
const archive = resolve(archiveArgument);
const platform = argument(
  "platform",
  process.platform === "win32" ? "windows-x64" : "macos-arm64",
);
const temporary = mkdtempSync(join(tmpdir(), "snaploom-c-consumer-"));

try {
  const extracted = join(temporary, "relocated", "sdk");
  mkdirSync(extracted, { recursive: true });
  run(pythonCommand(), [
    "-c",
    "import shutil,sys; shutil.unpack_archive(sys.argv[1], sys.argv[2])",
    archive,
    extracted,
  ]);
  const roots = readdirSync(extracted, { withFileTypes: true }).filter((entry) =>
    entry.isDirectory(),
  );
  if (roots.length !== 1) {
    throw new Error("C SDK archive must contain exactly one root directory");
  }
  const prefix = join(extracted, roots[0].name);
  const required = [
    "include/snaploom/snaploom_capture.h",
    "include/snaploom/snaploom_capture.hpp",
    "lib/cmake/SnaploomCapture/SnaploomCaptureConfig.cmake",
    "lib/cmake/SnaploomCapture/SnaploomCaptureConfigVersion.cmake",
    "examples/c/capture.c",
    "examples/cpp/capture.cpp",
    "LICENSES/Apache-2.0.txt",
    "NOTICE",
    "THIRD-PARTY-NOTICES.txt",
    "sbom.cdx.json",
    "README.md",
  ];
  for (const relative of required) {
    if (!existsSync(join(prefix, relative))) {
      throw new Error(`C SDK archive is missing ${relative}`);
    }
  }
  const platformFiles =
    platform === "windows-x64"
      ? ["bin/snaploom_capture.dll", "lib/snaploom_capture.lib"]
      : ["lib/libsnaploom_capture.dylib", "lib/pkgconfig/snaploom-capture.pc"];
  for (const relative of platformFiles) {
    if (!existsSync(join(prefix, relative))) {
      throw new Error(`C SDK archive is missing ${relative}`);
    }
  }

  const forbiddenNames = /(?:capture[-_ ]?host|tauri|capture[-_ ]?session)/i;
  for (const path of walk(prefix)) {
    const relative = path.slice(prefix.length + 1);
    if (forbiddenNames.test(relative)) {
      throw new Error(`forbidden product asset in C SDK archive: ${relative}`);
    }
  }
  const config = readFileSync(
    join(
      prefix,
      "lib",
      "cmake",
      "SnaploomCapture",
      "SnaploomCaptureConfig.cmake",
    ),
    "utf8",
  );
  if (config.includes(repository) || config.includes("sdk/target")) {
    throw new Error("CMake package leaks a repository build path");
  }

  if (platform === "macos-arm64") {
    const dylib = join(prefix, "lib", "libsnaploom_capture.dylib");
    const architectures = run("lipo", ["-archs", dylib]);
    if (architectures.trim() !== "arm64") {
      throw new Error(`unexpected macOS SDK architectures: ${architectures}`);
    }
    const installNames = run("otool", ["-D", dylib]);
    if (!installNames.includes("@rpath/libsnaploom_capture.dylib")) {
      throw new Error(`unexpected dylib install name: ${installNames}`);
    }
    const loadCommands = run("otool", ["-l", dylib]);
    if (!/LC_BUILD_VERSION[\s\S]*?minos 14\.0/.test(loadCommands)) {
      throw new Error("macOS C SDK dylib does not declare macOS 14.0 minimum");
    }
  } else if (platform === "windows-x64") {
    const dll = join(prefix, "bin", "snaploom_capture.dll");
    const dumpbin = findDumpbin();
    const headers = run(dumpbin, ["/headers", dll]);
    if (!/machine \(x64\)/i.test(headers)) {
      throw new Error("Windows C SDK DLL is not x64");
    }
    const importLibrary = join(prefix, "lib", "snaploom_capture.lib");
    const members = run(dumpbin, ["/linkermember:1", importLibrary]);
    const expected = readFileSync(
      join(repository, "sdk", "c-abi", "abi-symbols-v1.txt"),
      "utf8",
    )
      .trim()
      .split(/\r?\n/)
      .sort();
    const actual = [
      ...new Set(members.match(/snaploom_capture_[a-z0-9_]+_v1/g) ?? []),
    ].sort();
    if (JSON.stringify(actual) !== JSON.stringify(expected)) {
      throw new Error("Windows import library does not match the 7-symbol ABI");
    }
  } else {
    throw new Error(`unsupported C SDK platform: ${platform}`);
  }

  const consumer = join(temporary, "consumer");
  cpSync(join(repository, "sdk", "tests", "package-consumer"), consumer, {
    recursive: true,
  });
  const build = join(temporary, "build");
  run("cmake", [
    "-S",
    consumer,
    "-B",
    build,
    `-DCMAKE_PREFIX_PATH=${prefix}`,
    "-DCMAKE_BUILD_TYPE=Release",
  ]);
  run("cmake", ["--build", build, "--config", "Release"]);
  run("ctest", [
    "--test-dir",
    build,
    "--build-config",
    "Release",
    "--output-on-failure",
  ]);
  process.stdout.write(
    `C/C++ archive relocation consumer passed: ${basename(archive)}\n`,
  );
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
