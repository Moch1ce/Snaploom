import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
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
import { basename, dirname, join, resolve } from "node:path";
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
    throw new Error(
      `${command} ${args.join(" ")} failed\n${result.stdout}\n${result.stderr}`,
    );
  }
  return result.stdout.trim();
}

function pythonCommand() {
  for (const candidate of process.platform === "win32"
    ? ["python", "python3"]
    : ["python3", "python"]) {
    if (spawnSync(candidate, ["--version"], { encoding: "utf8" }).status === 0) {
      return candidate;
    }
  }
  throw new Error("Python is required to inspect the NuGet package");
}

function inspectPackage(packagePath) {
  const script = [
    "import hashlib,json,sys,zipfile",
    "p=sys.argv[1]",
    "with zipfile.ZipFile(p) as z:",
    " entries={n:{'size':z.getinfo(n).file_size,'sha256':hashlib.sha256(z.read(n)).hexdigest(),'prefix':z.read(n)[:4].hex()} for n in z.namelist()}",
    " print(json.dumps(entries,sort_keys=True))",
  ].join("\n");
  return JSON.parse(run(pythonCommand(), ["-c", script, packagePath]));
}

function inspectSymbols(symbolPath) {
  const script = [
    "import json,sys,zipfile",
    "with zipfile.ZipFile(sys.argv[1]) as z:",
    " names=z.namelist()",
    " pdb=z.read('lib/net8.0/Snaploom.Capture.pdb')",
    " result={'names':names,'sourceLink':b'https://raw.githubusercontent.com/moch1ce/Snaploom/' in pdb,'absolutePath':any(marker in pdb for marker in (b'/Users/',b'/home/runner/',b'D:\\\\a\\\\'))}",
    " print(json.dumps(result,sort_keys=True))",
  ].join("\n");
  return JSON.parse(run(pythonCommand(), ["-c", script, symbolPath]));
}

function packageVersion(packagePath) {
  const match = basename(packagePath).match(/^Snaploom\.Capture\.(.+)\.nupkg$/);
  if (!match) {
    throw new Error(`unexpected NuGet package name: ${packagePath}`);
  }
  return match[1];
}

function sha256File(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function configureConsumer(root, version, rid, framework = "net8.0") {
  mkdirSync(root, { recursive: true });
  const template = readFileSync(
    join(
      repository,
      "sdk",
      "dotnet",
      "tests",
      "package-consumer",
      "Snaploom.Consumer.csproj.in",
    ),
    "utf8",
  );
  writeFileSync(
    join(root, "Snaploom.Consumer.csproj"),
    template
      .replaceAll("@VERSION@", version)
      .replaceAll("@RID@", rid)
      .replaceAll("@FRAMEWORK@", framework),
  );
  cpSync(
    join(repository, "sdk", "dotnet", "tests", "package-consumer", "Program.cs"),
    join(root, "Program.cs"),
  );
}

function restoreLocked(root) {
  run("dotnet", ["restore"], { cwd: root });
  run("dotnet", ["restore", "--locked-mode"], { cwd: root });
}

function verifyUnsupported(packagePath, version, temporary) {
  const feed = dirname(packagePath);
  const runtimeRoot = join(temporary, "unsupported-runtime");
  configureConsumer(runtimeRoot, version, "");
  writeFileSync(join(runtimeRoot, "NuGet.Config"), nugetConfig(feed));
  restoreLocked(runtimeRoot);
  run("dotnet", ["run", "-c", "Release", "--no-restore", "--", "unsupported"], {
    cwd: runtimeRoot,
  });

  const buildRoot = join(temporary, "unsupported-build");
  configureConsumer(buildRoot, version, "linux-x64");
  writeFileSync(join(buildRoot, "NuGet.Config"), nugetConfig(feed));
  const result = spawnSync("dotnet", ["build", "-c", "Release"], {
    cwd: buildRoot,
    encoding: "utf8",
  });
  const output = `${result.stdout}\n${result.stderr}`;
  if (result.status === 0 || !output.includes("SNAPLOOM001")) {
    throw new Error(`unsupported RID did not fail with SNAPLOOM001\n${output}`);
  }
}

function nugetConfig(feed) {
  const normalized = resolve(feed)
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;");
  return `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="snaploom-local" value="${normalized}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
`;
}

const packagePath = resolve(argument("package"));
if (!existsSync(packagePath)) {
  throw new Error(`NuGet package does not exist: ${packagePath}`);
}
const version = packageVersion(packagePath);
const entries = inspectPackage(packagePath);
const symbolPath = packagePath.replace(/\.nupkg$/, ".snupkg");
if (!existsSync(symbolPath)) {
  throw new Error(`NuGet symbol package does not exist: ${symbolPath}`);
}
const symbols = inspectSymbols(symbolPath);
if (!symbols.names.includes("lib/net8.0/Snaploom.Capture.pdb") || !symbols.sourceLink) {
  throw new Error("NuGet symbol package is missing its portable PDB or GitHub Source Link mapping");
}
if (symbols.absolutePath) {
  throw new Error("NuGet symbol package leaks an absolute build-machine path");
}
const required = [
  "lib/net8.0/Snaploom.Capture.dll",
  "lib/net8.0/Snaploom.Capture.xml",
  "runtimes/win-x64/native/snaploom_capture.dll",
  "runtimes/osx-arm64/native/libsnaploom_capture.dylib",
  "buildTransitive/Snaploom.Capture.targets",
  "LICENSES/Apache-2.0.txt",
  "NOTICE",
  "THIRD-PARTY-NOTICES.txt",
  "sbom.cdx.json",
  "README.md",
];
for (const entry of required) {
  if (!entries[entry] || entries[entry].size === 0) {
    throw new Error(`NuGet package is missing required non-empty entry: ${entry}`);
  }
}
for (const entry of Object.keys(entries)) {
  if (
    /(?:^|\/)(?:capture[-_]?host|tauri|capture[-_]?core)(?:\/|\.|$)/i.test(
      entry,
    ) ||
    /(?:^|\/)GPL-/i.test(entry) ||
    entry.endsWith(".exe")
  ) {
    throw new Error(`forbidden product asset in NuGet package: ${entry}`);
  }
}
if (
  entries["runtimes/win-x64/native/snaploom_capture.dll"].prefix !==
  "4d5a9000"
) {
  throw new Error("win-x64 native asset is not a PE DLL");
}
if (
  entries["runtimes/osx-arm64/native/libsnaploom_capture.dylib"].prefix !==
  "cffaedfe"
) {
  throw new Error("osx-arm64 native asset is not a 64-bit little-endian Mach-O");
}
for (const [option, entry] of [
  ["windows-native", "runtimes/win-x64/native/snaploom_capture.dll"],
  ["macos-native", "runtimes/osx-arm64/native/libsnaploom_capture.dylib"],
]) {
  const nativePath = argument(option);
  if (nativePath && sha256File(resolve(nativePath)) !== entries[entry].sha256) {
    throw new Error(`${entry} does not match its staged native build byte-for-byte`);
  }
}

const platform = argument("platform", "unsupported");
const framework = argument("framework", "net8.0");
if (framework !== "net8.0" && framework !== "net10.0") {
  throw new Error(`unsupported consumer target framework: ${framework}`);
}
const temporary = mkdtempSync(join(tmpdir(), "snaploom-dotnet-consumer-"));
try {
  const unsupported = platform === "unsupported";
  if (unsupported) {
    verifyUnsupported(packagePath, version, temporary);
    process.stdout.write("unsupported RID diagnostics verified\n");
  }

  const platformConfig = unsupported
    ? undefined
    : {
        "windows-x64": {
          rid: "win-x64",
          native: "snaploom_capture.dll",
          executable: "Snaploom.Consumer.exe",
        },
        "macos-arm64": {
          rid: "osx-arm64",
          native: "libsnaploom_capture.dylib",
          executable: "Snaploom.Consumer",
        },
      }[platform];
  if (!unsupported && !platformConfig) {
    throw new Error(`unsupported package verification platform: ${platform}`);
  }

  if (!unsupported) {
    const root = join(temporary, "consumer");
    configureConsumer(root, version, platformConfig.rid, framework);
    writeFileSync(join(root, "NuGet.Config"), nugetConfig(dirname(packagePath)));
    restoreLocked(root);
    run("dotnet", ["run", "-c", "Release", "--no-restore"], {
      cwd: root,
    });

    const normalNative = join(
      root,
      "bin",
      "Release",
      framework,
      platformConfig.rid,
      platformConfig.native,
    );
    if (!existsSync(normalNative)) {
      throw new Error(
        `RID native asset was not copied to consumer output: ${normalNative}`,
      );
    }

    const trimmed = join(temporary, "trimmed");
    run(
      "dotnet",
      [
        "publish",
        "-c",
        "Release",
        "-r",
        platformConfig.rid,
        "--self-contained",
        "true",
        "-p:PublishTrimmed=true",
        "-o",
        trimmed,
      ],
      { cwd: root },
    );
    run(join(trimmed, platformConfig.executable), []);
    if (!existsSync(join(trimmed, platformConfig.native))) {
      throw new Error("trimmed consumer omitted the native SDK asset");
    }

    const aot = join(temporary, "aot");
    run(
      "dotnet",
      [
        "publish",
        "-c",
        "Release",
        "-r",
        platformConfig.rid,
        "-p:PublishAot=true",
        "-p:PublishTrimmed=true",
        "-o",
        aot,
      ],
      { cwd: root },
    );
    run(join(aot, platformConfig.executable), []);
    if (!existsSync(join(aot, platformConfig.native))) {
      throw new Error("NativeAOT consumer omitted the native SDK asset");
    }

    process.stdout.write(
      `${platform} ${framework} NuGet build/run, trim, and NativeAOT verified\n`,
    );
  }
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
