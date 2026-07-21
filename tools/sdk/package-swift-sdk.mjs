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
import { join, resolve } from "node:path";
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

if (process.platform !== "darwin" || process.arch !== "arm64") {
  throw new Error("Swift XCFramework packaging requires macOS arm64");
}

const version = argument("version", "0.1.0");
if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) {
  throw new Error(`invalid SDK version: ${version}`);
}

const library = resolve(
  argument(
    "library",
    join(repository, "sdk", "target", "release", "libsnaploom_capture.dylib"),
  ),
);
if (!existsSync(library)) {
  throw new Error(`native C SDK library does not exist: ${library}`);
}

const output = resolve(argument("output", join(repository, "dist")));
mkdirSync(output, { recursive: true });
const archive = join(output, `CSnaploomCapture-${version}.xcframework.zip`);
const temporary = mkdtempSync(join(tmpdir(), "snaploom-swift-sdk-"));

try {
  const headers = join(temporary, "Headers");
  const framework = join(temporary, "CSnaploomCapture.xcframework");
  mkdirSync(headers, { recursive: true });
  cpSync(
    join(repository, "sdk", "c", "include", "snaploom", "snaploom_capture.h"),
    join(headers, "snaploom_capture.h"),
  );
  writeFileSync(
    join(headers, "module.modulemap"),
    [
      "module CSnaploomCapture {",
      '  header "snaploom_capture.h"',
      "  export *",
      "}",
      "",
    ].join("\n"),
  );

  run("xcodebuild", [
    "-create-xcframework",
    "-library",
    library,
    "-headers",
    headers,
    "-output",
    framework,
  ]);

  mkdirSync(join(temporary, "LICENSES"), { recursive: true });
  cpSync(
    join(repository, "LICENSES", "Apache-2.0.txt"),
    join(temporary, "LICENSES", "Apache-2.0.txt"),
  );
  cpSync(
    join(repository, "sdk", "distribution", "NOTICE"),
    join(temporary, "NOTICE"),
  );
  cpSync(
    join(repository, "sdk", "distribution", "THIRD-PARTY-NOTICES.txt"),
    join(temporary, "THIRD-PARTY-NOTICES.txt"),
  );
  cpSync(
    join(repository, "sdk", "distribution", "sbom.cdx.json"),
    join(temporary, "sbom.cdx.json"),
  );

  rmSync(archive, { force: true });
  const zipScript = [
    "import os,stat,sys,zipfile",
    "root=os.path.abspath(sys.argv[1])",
    "archive=os.path.abspath(sys.argv[2])",
    "paths=[]",
    "for current,dirs,files in os.walk(root):",
    " dirs.sort(); files.sort()",
    " for name in files: paths.append(os.path.join(current,name))",
    "with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as output:",
    " for path in paths:",
    "  relative=os.path.relpath(path,root).replace(os.sep,'/')",
    "  info=zipfile.ZipInfo(relative,(1980,1,1,0,0,0))",
    "  mode=0o755 if os.stat(path).st_mode & 0o111 else 0o644",
    "  info.external_attr=(stat.S_IFREG|mode)<<16",
    "  info.compress_type=zipfile.ZIP_DEFLATED",
    "  with open(path,'rb') as source: output.writestr(info,source.read(),compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)",
  ].join("\n");
  run("python3", ["-c", zipScript, temporary, archive]);

  const checksum = run("swift", ["package", "compute-checksum", archive]);
  const manifest = readFileSync(join(repository, "Package.swift"), "utf8");
  const releaseUrl = `https://github.com/Moch1ce/Snaploom/releases/download/v${version}/CSnaploomCapture-${version}.xcframework.zip`;
  if (!manifest.includes(releaseUrl)) {
    throw new Error(`Package.swift does not declare release URL ${releaseUrl}`);
  }
  process.stdout.write(`${archive}\n${checksum}\n`);
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
