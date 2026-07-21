// Copyright (C) 2026 Snaploom contributors
// SPDX-License-Identifier: GPL-3.0-or-later

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const installer = readFileSync(
  new URL("../../packaging/windows/Snaploom.iss", import.meta.url),
  "utf8",
);
const verifier = readFileSync(
  new URL("../../tests/packaging/verify-windows-packages.ps1", import.meta.url),
  "utf8",
);

test("Windows installer and verifier use the same textual AppId", () => {
  assert.match(installer, /^AppId=Snaploom\.Desktop$/m);
  assert.match(
    verifier,
    /Uninstall\\Snaploom\.Desktop_is1/,
    "the install verifier must inspect the uninstall key derived from AppId",
  );
});
