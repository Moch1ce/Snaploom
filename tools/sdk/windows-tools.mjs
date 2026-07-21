import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

export function findDumpbin() {
  const fromPath = spawnSync("where.exe", ["dumpbin.exe"], {
    encoding: "utf8",
  });
  if (fromPath.status === 0) {
    const candidate = fromPath.stdout.trim().split(/\r?\n/)[0];
    if (candidate && existsSync(candidate)) {
      return candidate;
    }
  }

  const programFiles = process.env["ProgramFiles(x86)"];
  if (!programFiles) {
    throw new Error("ProgramFiles(x86) is unavailable; cannot locate dumpbin.exe");
  }
  const vswhere = join(
    programFiles,
    "Microsoft Visual Studio",
    "Installer",
    "vswhere.exe",
  );
  const installation = spawnSync(
    vswhere,
    [
      "-latest",
      "-products",
      "*",
      "-requires",
      "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
      "-property",
      "installationPath",
    ],
    { encoding: "utf8" },
  );
  if (installation.status !== 0 || !installation.stdout.trim()) {
    throw new Error("Visual Studio C++ tools are unavailable; cannot locate dumpbin.exe");
  }
  const root = installation.stdout.trim();
  const versionFile = join(
    root,
    "VC",
    "Auxiliary",
    "Build",
    "Microsoft.VCToolsVersion.default.txt",
  );
  const version = readFileSync(versionFile, "utf8").trim();
  const tools = join(root, "VC", "Tools", "MSVC", version, "bin");
  for (const host of ["Hostx64", "Hostx86"]) {
    const candidate = join(tools, host, "x64", "dumpbin.exe");
    if (existsSync(candidate)) {
      return candidate;
    }
  }
  throw new Error("Visual Studio installation does not contain x64 dumpbin.exe");
}
