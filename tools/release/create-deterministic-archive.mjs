// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import { spawnSync } from "node:child_process";
import { basename, resolve } from "node:path";

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function pythonCommand() {
  for (const command of process.platform === "win32" ? ["python", "python3"] : ["python3", "python"]) {
    if (spawnSync(command, ["--version"], { stdio: "ignore" }).status === 0) return command;
  }
  throw new Error("Python is required for deterministic archives");
}

export function createArchive({ root, output, format }) {
  if (format !== "zip" && format !== "tar.gz") {
    throw new Error(`unsupported deterministic archive format: ${format}`);
  }
  const script = [
    "import gzip,os,stat,sys,tarfile,zipfile",
    "root=os.path.abspath(sys.argv[1]); output=os.path.abspath(sys.argv[2]); fmt=sys.argv[3]",
    "base=os.path.dirname(root); paths=[]",
    "for current,dirs,files in os.walk(root):",
    " dirs.sort(); files.sort()",
    " for name in dirs+files: paths.append(os.path.join(current,name))",
    "if fmt=='zip':",
    " with zipfile.ZipFile(output,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as archive:",
    "  for path in paths:",
    "   rel=os.path.relpath(path,base).replace(os.sep,'/'); isdir=os.path.isdir(path)",
    "   if isdir: rel+='/'",
    "   info=zipfile.ZipInfo(rel,(1980,1,1,0,0,0)); mode=0o755 if isdir or os.access(path,os.X_OK) else 0o644",
    "   info.external_attr=((stat.S_IFDIR if isdir else stat.S_IFREG)|mode)<<16; info.compress_type=zipfile.ZIP_DEFLATED",
    "   archive.writestr(info,b'' if isdir else open(path,'rb').read())",
    "else:",
    " with open(output,'wb') as raw:",
    "  with gzip.GzipFile(filename='',mode='wb',fileobj=raw,mtime=0,compresslevel=9) as gz:",
    "   with tarfile.open(fileobj=gz,mode='w',format=tarfile.PAX_FORMAT) as archive:",
    "    for path in paths:",
    "     rel=os.path.relpath(path,base).replace(os.sep,'/'); info=archive.gettarinfo(path,arcname=rel)",
    "     info.uid=0; info.gid=0; info.uname=''; info.gname=''; info.mtime=0; info.pax_headers={}",
    "     archive.addfile(info,None if info.isdir() else open(path,'rb'))",
  ].join("\n");
  const result = spawnSync(
    pythonCommand(),
    ["-c", script, resolve(root), resolve(output), format],
    { encoding: "utf8" },
  );
  if (result.status !== 0) throw new Error(result.stderr || result.stdout || "archive creation failed");
}

if (process.argv[1] && basename(process.argv[1]) === "create-deterministic-archive.mjs") {
  const root = argument("root");
  const output = argument("output");
  const format = argument("format");
  if (!root || !output || !format) throw new Error("--root, --output, and --format are required");
  createArchive({ root, output, format });
  process.stdout.write(`${resolve(output)}\n`);
}
