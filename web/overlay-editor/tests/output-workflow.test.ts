import { describe, expect, it } from "vitest";
import {
  OutputWorkflow,
  suggestedPngName,
  type NativeOutputPort,
  type OutputEvent,
  type PreparedOutput,
} from "../src/output-workflow";

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, decline) => {
    resolve = accept;
    reject = decline;
  });
  return { promise, resolve, reject };
}

interface HarnessOptions {
  readonly saveResults?: Array<"saved" | "cancelled" | Error>;
  readonly port?: Partial<NativeOutputPort>;
}

function harness(options: HarnessOptions = {}) {
  const saveResults = options.saveResults ?? ["saved"];
  const session = {
    value: "draft|rectangle|undo=2",
    commits: 0,
    rollbacks: 0,
  };
  const png = new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10, 43]);
  let composeCount = 0;
  const clipboard: Uint8Array[] = [];
  const saves: Uint8Array[] = [];
  const calls: string[] = [];
  const events: OutputEvent[] = [];
  const composer = {
    async prepare(): Promise<PreparedOutput> {
      composeCount += 1;
      const before = session.value;
      session.value = "committed|rectangle|undo=3";
      let settled = false;
      return {
        png: png.slice(),
        pixelWidth: 40,
        pixelHeight: 20,
        commit() {
          if (settled) return;
          settled = true;
          session.commits += 1;
        },
        rollback() {
          if (settled) return;
          settled = true;
          session.value = before;
          session.rollbacks += 1;
        },
      };
    },
  };
  const defaultPort: NativeOutputPort = {
    async writePngClipboard(bytes) {
      calls.push("clipboard");
      clipboard.push(bytes.slice());
    },
    async savePng(request, bytes) {
      calls.push(`save:${request.suggestedName}:${request.extension}`);
      saves.push(bytes.slice());
      const result = saveResults.shift() ?? "saved";
      if (result instanceof Error) throw result;
      return result;
    },
    async setOverlayVisible(visible) {
      calls.push(visible ? "show" : "hide");
    },
    async closeOverlay() {
      calls.push("close");
    },
  };
  const port: NativeOutputPort = { ...defaultPort, ...options.port };
  const workflow = new OutputWorkflow({
    composer,
    port,
    now: () => new Date(2026, 6, 21, 14, 5, 9),
    record: (event) => events.push(event),
  });
  return {
    workflow,
    session,
    png,
    clipboard,
    saves,
    calls,
    events,
    composeCount: () => composeCount,
  };
}

describe("Capture Result output workflow", () => {
  it("completes only after native clipboard accepts the exact PNG bytes", async () => {
    const test = harness();

    const outcome = await test.workflow.complete();

    expect(outcome).toEqual({
      kind: "completed",
      png: test.png,
      pixelWidth: 40,
      pixelHeight: 20,
    });
    expect(test.clipboard).toEqual([test.png]);
    expect(test.calls).toEqual(["clipboard", "close"]);
    expect(test.session.commits).toBe(1);
  });

  it("copies and then saves one frozen encoding without changing its bytes", async () => {
    const test = harness();

    const copied = await test.workflow.copyAndContinue();
    const saved = await test.workflow.save();

    expect(copied.kind).toBe("continued");
    expect(saved.kind).toBe("completed");
    expect(test.composeCount()).toBe(1);
    expect(test.clipboard[0]).toEqual(test.saves[0]);
    expect(test.calls).toEqual([
      "clipboard",
      "hide",
      "save:Snaploom_2026-07-21_14-05-09.png:png",
      "close",
    ]);
  });

  it("restores the full prepared editor transaction after save cancellation", async () => {
    const test = harness({ saveResults: ["cancelled"] });

    const outcome = await test.workflow.save();

    expect(outcome).toEqual({ kind: "save-cancelled" });
    expect(test.session.value).toBe("draft|rectangle|undo=2");
    expect(test.session.rollbacks).toBe(1);
    expect(test.calls).toEqual([
      "hide",
      "save:Snaploom_2026-07-21_14-05-09.png:png",
      "show",
    ]);
    expect(test.workflow.snapshot()).toEqual({ busy: false, error: null });
  });

  it("reports a failed overlay restoration instead of hiding a retryable Session", async () => {
    const test = harness({
      saveResults: ["cancelled"],
      port: {
        setOverlayVisible: async (visible) => {
          if (visible) throw new Error("overlay restore failed");
        },
      },
    });

    expect(await test.workflow.save()).toEqual({
      kind: "recoverable-error",
      error: "overlay-restore-failed",
    });
    expect(test.session.value).toBe("draft|rectangle|undo=2");
    expect(test.workflow.snapshot()).toEqual({
      busy: false,
      error: "overlay-restore-failed",
    });
  });

  it("restores and remains retryable after save or clipboard failures", async () => {
    const saveTest = harness({
      saveResults: [new Error("/private/path must never be logged"), "saved"],
    });
    expect(await saveTest.workflow.save()).toEqual({
      kind: "recoverable-error",
      error: "save-failed",
    });
    expect(saveTest.session.value).toBe("draft|rectangle|undo=2");
    expect(await saveTest.workflow.save()).toMatchObject({ kind: "completed" });

    const clipboardTest = harness({
      port: {
        writePngClipboard: async () => {
          throw new Error("secret clipboard bytes");
        },
      },
    });
    expect(await clipboardTest.workflow.complete()).toEqual({
      kind: "recoverable-error",
      error: "clipboard-write-failed",
    });
    expect(clipboardTest.session.value).toBe("draft|rectangle|undo=2");
  });

  it("does not misreport a successful output when only overlay close fails", async () => {
    const test = harness({
      port: {
        closeOverlay: async () => {
          throw new Error("close failed");
        },
      },
    });

    expect(await test.workflow.complete()).toEqual({
      kind: "recoverable-error",
      error: "overlay-close-failed",
    });
    expect(test.clipboard).toEqual([test.png]);
    expect(test.session.commits).toBe(1);
    expect(test.session.rollbacks).toBe(0);
  });

  it("rejects duplicate output while a native operation is pending", async () => {
    const pending = deferred<void>();
    const test = harness({
      port: {
        writePngClipboard: async () => pending.promise,
      },
    });

    const first = test.workflow.complete();
    await Promise.resolve();
    expect(await test.workflow.copyAndContinue()).toEqual({ kind: "busy" });
    expect(test.composeCount()).toBe(1);
    pending.resolve();
    expect(await first).toMatchObject({ kind: "completed" });
  });

  it("records only stable privacy-safe event enums", async () => {
    const test = harness({
      saveResults: [new Error("/Users/alice/secret.png")],
    });
    await test.workflow.save();

    expect(test.events).toEqual([
      "output.save.started",
      "output.save.failed",
    ]);
    expect(JSON.stringify(test.events)).not.toContain("alice");
    expect(JSON.stringify(test.events)).not.toContain("secret");
  });

  it("formats the PNG-only native default name in local time", () => {
    expect(suggestedPngName(new Date(2026, 0, 2, 3, 4, 5))).toBe(
      "Snaploom_2026-01-02_03-04-05.png",
    );
  });
});
