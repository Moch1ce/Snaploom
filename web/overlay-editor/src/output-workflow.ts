export type SavePngDisposition = "saved" | "cancelled";

export interface SavePngRequest {
  readonly suggestedName: string;
  readonly extension: "png";
  readonly mimeType: "image/png";
}

export interface NativeOutputPort {
  /** The implementation must copy the bytes before the returned promise resolves. */
  writePngClipboard(png: Uint8Array): Promise<void>;
  /** The native side owns the dialog and filesystem; the WebView supplies only PNG bytes. */
  savePng(request: SavePngRequest, png: Uint8Array): Promise<SavePngDisposition>;
  setOverlayVisible(visible: boolean): Promise<void>;
  closeOverlay(): Promise<void>;
}

export interface PreparedOutput {
  readonly png: Uint8Array;
  readonly pixelWidth: number;
  readonly pixelHeight: number;
  commit(): void;
  rollback(): void;
}

export interface OutputComposer {
  prepare(): Promise<PreparedOutput>;
}

export type OutputError =
  | "png-encode-failed"
  | "clipboard-write-failed"
  | "save-failed"
  | "overlay-close-failed"
  | "overlay-restore-failed";

export type OutputEvent =
  | "output.complete.started"
  | "output.complete.succeeded"
  | "output.complete.failed"
  | "output.copy.started"
  | "output.copy.succeeded"
  | "output.copy.failed"
  | "output.save.started"
  | "output.save.succeeded"
  | "output.save.cancelled"
  | "output.save.failed";

export type OutputOutcome =
  | {
      readonly kind: "completed";
      readonly png: Uint8Array;
      readonly pixelWidth: number;
      readonly pixelHeight: number;
    }
  | {
      readonly kind: "continued";
      readonly png: Uint8Array;
      readonly pixelWidth: number;
      readonly pixelHeight: number;
    }
  | { readonly kind: "save-cancelled" }
  | { readonly kind: "recoverable-error"; readonly error: OutputError }
  | { readonly kind: "busy" };

export interface OutputWorkflowState {
  readonly busy: boolean;
  readonly error: OutputError | null;
}

interface FrozenOutput {
  readonly png: Uint8Array;
  readonly pixelWidth: number;
  readonly pixelHeight: number;
}

export interface OutputWorkflowOptions {
  readonly composer: OutputComposer;
  readonly port: NativeOutputPort;
  readonly now?: () => Date;
  readonly record?: (event: OutputEvent) => void;
  readonly onStateChange?: (state: OutputWorkflowState) => void;
}

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

export function suggestedPngName(now: Date): string {
  return [
    `Snaploom_${now.getFullYear()}`,
    pad(now.getMonth() + 1),
    pad(now.getDate()),
  ].join("-") + `_${pad(now.getHours())}-${pad(now.getMinutes())}-${pad(now.getSeconds())}.png`;
}

export class OutputWorkflow {
  readonly #composer: OutputComposer;
  readonly #now: () => Date;
  readonly #record: (event: OutputEvent) => void;
  readonly #onStateChange: (state: OutputWorkflowState) => void;
  readonly #port: NativeOutputPort;
  #busy = false;
  #error: OutputError | null = null;
  #frozen: FrozenOutput | null = null;

  constructor(options: OutputWorkflowOptions) {
    this.#composer = options.composer;
    this.#port = options.port;
    this.#now = options.now ?? (() => new Date());
    this.#record = options.record ?? (() => undefined);
    this.#onStateChange = options.onStateChange ?? (() => undefined);
  }

  snapshot(): OutputWorkflowState {
    return { busy: this.#busy, error: this.#error };
  }

  invalidate(): void {
    if (this.#busy) return;
    this.#frozen?.png.fill(0);
    this.#frozen = null;
    this.#setState(false, null);
  }

  dispose(): void {
    this.#frozen?.png.fill(0);
    this.#frozen = null;
  }

  async complete(): Promise<OutputOutcome> {
    return this.#clipboardOutput("complete", true);
  }

  async copyAndContinue(): Promise<OutputOutcome> {
    return this.#clipboardOutput("copy", false);
  }

  async save(): Promise<OutputOutcome> {
    if (!this.#begin("output.save.started")) return { kind: "busy" };
    let prepared: PreparedOutput | null = null;
    let hidden = false;
    let outputCommitted = false;
    try {
      prepared = await this.#prepare();
      await this.#port.setOverlayVisible(false);
      hidden = true;
      const disposition = await this.#port.savePng(
        {
          suggestedName: suggestedPngName(this.#now()),
          extension: "png",
          mimeType: "image/png",
        },
        prepared.png.slice(),
      );
      if (disposition === "cancelled") {
        prepared.rollback();
        if (!(await this.#restoreOverlay(hidden))) {
          this.#record("output.save.failed");
          this.#setState(false, "overlay-restore-failed");
          return {
            kind: "recoverable-error",
            error: "overlay-restore-failed",
          };
        }
        this.#record("output.save.cancelled");
        this.#setState(false, null);
        return { kind: "save-cancelled" };
      }
      this.#freeze(prepared);
      outputCommitted = true;
      await this.#port.closeOverlay();
      this.#record("output.save.succeeded");
      this.#setState(false, null);
      return this.#completedOutcome();
    } catch {
      if (outputCommitted) {
        const restored = await this.#restoreOverlay(hidden);
        const error = restored
          ? "overlay-close-failed"
          : "overlay-restore-failed";
        this.#record("output.save.failed");
        this.#setState(false, error);
        return { kind: "recoverable-error", error };
      }
      prepared?.rollback();
      const restored = await this.#restoreOverlay(hidden);
      this.#record("output.save.failed");
      const error = !restored
        ? "overlay-restore-failed"
        : prepared
          ? "save-failed"
          : "png-encode-failed";
      this.#setState(false, error);
      return { kind: "recoverable-error", error };
    }
  }

  async #clipboardOutput(
    intent: "complete" | "copy",
    closeAfterWrite: boolean,
  ): Promise<OutputOutcome> {
    if (!this.#begin(`output.${intent}.started`)) return { kind: "busy" };
    let prepared: PreparedOutput | null = null;
    let outputCommitted = false;
    try {
      prepared = await this.#prepare();
      await this.#port.writePngClipboard(prepared.png.slice());
      this.#freeze(prepared);
      outputCommitted = true;
      if (closeAfterWrite) await this.#port.closeOverlay();
      this.#record(`output.${intent}.succeeded`);
      this.#setState(false, null);
      return closeAfterWrite ? this.#completedOutcome() : this.#continuedOutcome();
    } catch {
      if (outputCommitted) {
        this.#record(`output.${intent}.failed`);
        this.#setState(false, "overlay-close-failed");
        return { kind: "recoverable-error", error: "overlay-close-failed" };
      }
      prepared?.rollback();
      this.#record(`output.${intent}.failed`);
      const error: OutputError = prepared
        ? "clipboard-write-failed"
        : "png-encode-failed";
      this.#setState(false, error);
      return { kind: "recoverable-error", error };
    }
  }

  #begin(event: OutputEvent): boolean {
    if (this.#busy) return false;
    this.#record(event);
    this.#setState(true, null);
    return true;
  }

  async #prepare(): Promise<PreparedOutput> {
    if (!this.#frozen) return this.#composer.prepare();
    const frozen = this.#frozen;
    return {
      png: frozen.png.slice(),
      pixelWidth: frozen.pixelWidth,
      pixelHeight: frozen.pixelHeight,
      commit: () => undefined,
      rollback: () => undefined,
    };
  }

  #freeze(prepared: PreparedOutput): void {
    prepared.commit();
    this.#frozen?.png.fill(0);
    this.#frozen = {
      png: prepared.png.slice(),
      pixelWidth: prepared.pixelWidth,
      pixelHeight: prepared.pixelHeight,
    };
  }

  #completedOutcome(): Extract<OutputOutcome, { kind: "completed" }> {
    const frozen = this.#requireFrozen();
    return {
      kind: "completed",
      png: frozen.png.slice(),
      pixelWidth: frozen.pixelWidth,
      pixelHeight: frozen.pixelHeight,
    };
  }

  #continuedOutcome(): Extract<OutputOutcome, { kind: "continued" }> {
    const frozen = this.#requireFrozen();
    return {
      kind: "continued",
      png: frozen.png.slice(),
      pixelWidth: frozen.pixelWidth,
      pixelHeight: frozen.pixelHeight,
    };
  }

  #requireFrozen(): FrozenOutput {
    if (!this.#frozen) throw new Error("missing frozen Capture Result");
    return this.#frozen;
  }

  async #restoreOverlay(hidden: boolean): Promise<boolean> {
    if (!hidden) return true;
    try {
      await this.#port.setOverlayVisible(true);
      return true;
    } catch {
      return false;
    }
  }

  #setState(busy: boolean, error: OutputError | null): void {
    this.#busy = busy;
    this.#error = error;
    this.#onStateChange(this.snapshot());
  }
}
