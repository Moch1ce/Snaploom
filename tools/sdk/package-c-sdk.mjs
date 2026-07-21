import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
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
  throw new Error("Python is required to create the SDK archive");
}

function render(source, replacements) {
  let contents = readFileSync(source, "utf8");
  for (const [name, value] of Object.entries(replacements)) {
    contents = contents.replaceAll(`@${name}@`, value);
  }
  return contents;
}

const platform = argument(
  "platform",
  process.platform === "win32" ? "windows-x64" : "macos-arm64",
);
if (platform !== "windows-x64" && platform !== "macos-arm64") {
  throw new Error(`unsupported C SDK platform: ${platform}`);
}

const version = argument("version", "0.1.0");
if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) {
  throw new Error(`invalid SDK version: ${version}`);
}
const versionMajor = version.split(".")[0];
const output = resolve(argument("output", join(repository, "dist")));
const defaultLibrary =
  platform === "windows-x64"
    ? join(repository, "sdk", "target", "release", "snaploom_capture.dll")
    : join(repository, "sdk", "target", "release", "libsnaploom_capture.dylib");
const library = resolve(argument("library", defaultLibrary));
if (!existsSync(library)) {
  throw new Error(`native C SDK library does not exist: ${library}`);
}

let importLibrary;
if (platform === "windows-x64") {
  const explicit = argument("import-library");
  const candidates = explicit
    ? [resolve(explicit)]
    : [
        join(dirname(library), "snaploom_capture.dll.lib"),
        join(dirname(library), "snaploom_capture.lib"),
      ];
  importLibrary = candidates.find(existsSync);
  if (!importLibrary) {
    throw new Error(`Windows import library not found beside ${library}`);
  }
}

mkdirSync(output, { recursive: true });
const temporary = mkdtempSync(join(tmpdir(), "snaploom-c-sdk-"));
const directoryName = `snaploom-capture-sdk-c-${version}-${platform}`;
const root = join(temporary, directoryName);

try {
  const include = join(root, "include", "snaploom");
  const cmake = join(root, "lib", "cmake", "SnaploomCapture");
  mkdirSync(include, { recursive: true });
  mkdirSync(cmake, { recursive: true });
  mkdirSync(join(root, "examples", "c"), { recursive: true });
  mkdirSync(join(root, "examples", "cpp"), { recursive: true });
  mkdirSync(join(root, "LICENSES"), { recursive: true });

  cpSync(
    join(repository, "sdk", "c", "include", "snaploom", "snaploom_capture.h"),
    join(include, "snaploom_capture.h"),
  );
  cpSync(
    join(repository, "sdk", "cpp", "include", "snaploom", "snaploom_capture.hpp"),
    join(include, "snaploom_capture.hpp"),
  );
  cpSync(
    join(repository, "sdk", "c", "examples", "capture.c"),
    join(root, "examples", "c", "capture.c"),
  );
  cpSync(
    join(repository, "sdk", "cpp", "examples", "capture.cpp"),
    join(root, "examples", "cpp", "capture.cpp"),
  );
  cpSync(join(repository, "sdk", "c", "README.md"), join(root, "README.md"));
  cpSync(
    join(repository, "LICENSES", "Apache-2.0.txt"),
    join(root, "LICENSES", "Apache-2.0.txt"),
  );
  cpSync(
    join(repository, "sdk", "distribution", "NOTICE"),
    join(root, "NOTICE"),
  );
  cpSync(
    join(repository, "sdk", "distribution", "THIRD-PARTY-NOTICES.txt"),
    join(root, "THIRD-PARTY-NOTICES.txt"),
  );
  cpSync(
    join(repository, "sdk", "distribution", "sbom.cdx.json"),
    join(root, "sbom.cdx.json"),
  );

  writeFileSync(
    join(cmake, "SnaploomCaptureConfig.cmake"),
    render(
      join(repository, "sdk", "c", "cmake", "SnaploomCaptureConfig.cmake.in"),
      {},
    ),
  );
  writeFileSync(
    join(cmake, "SnaploomCaptureConfigVersion.cmake"),
    render(
      join(
        repository,
        "sdk",
        "c",
        "cmake",
        "SnaploomCaptureConfigVersion.cmake.in",
      ),
      { SNAPLOOM_VERSION: version, SNAPLOOM_VERSION_MAJOR: versionMajor },
    ),
  );

  if (platform === "windows-x64") {
    mkdirSync(join(root, "bin"), { recursive: true });
    cpSync(library, join(root, "bin", "snaploom_capture.dll"));
    cpSync(importLibrary, join(root, "lib", "snaploom_capture.lib"));
  } else {
    cpSync(library, join(root, "lib", "libsnaploom_capture.dylib"));
    mkdirSync(join(root, "lib", "pkgconfig"), { recursive: true });
    writeFileSync(
      join(root, "lib", "pkgconfig", "snaploom-capture.pc"),
      render(
        join(
          repository,
          "sdk",
          "c",
          "pkgconfig",
          "snaploom-capture.pc.in",
        ),
        { SNAPLOOM_VERSION: version },
      ),
    );
  }

  const format = platform === "windows-x64" ? "zip" : "gztar";
  const extension = format === "zip" ? ".zip" : ".tar.gz";
  const archive = join(output, `${directoryName}${extension}`);
  rmSync(archive, { force: true });
  run(pythonCommand(), [
    "-c",
    "import shutil,sys; shutil.make_archive(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4])",
    join(output, directoryName),
    format,
    temporary,
    directoryName,
  ]);
  process.stdout.write(`${archive}\n`);
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
