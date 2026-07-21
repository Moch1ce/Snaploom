export interface OverlayShell {
  readonly canvasCount: number;
  readonly status: "ready";
}

export function describeOverlayShell(): OverlayShell {
  return { canvasCount: 1, status: "ready" };
}
