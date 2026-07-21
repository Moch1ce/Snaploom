import { PointerEvent, useEffect, useMemo, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import "./App.css";

const FRAME = { width: 3840, height: 2160 } as const;

type Variant = "A" | "B" | "C";
type Tool = "select" | "rectangle" | "arrow" | "text" | "mosaic";
type Point = { x: number; y: number };
type Rect = Point & { width: number; height: number };
type Annotation =
  | { id: number; kind: "rectangle" | "arrow" | "mosaic"; from: Point; to: Point }
  | { id: number; kind: "text"; at: Point; text: string };
type CaptureReceipt = {
  widthPx: number;
  heightPx: number;
  pngBytes: number;
  sha256: string;
};

const windows: Rect[] = [
  { x: 250, y: 180, width: 1380, height: 980 },
  { x: 1780, y: 260, width: 1660, height: 720 },
  { x: 960, y: 1220, width: 1920, height: 700 },
];

function normalizedRect(from: Point, to: Point): Rect {
  return {
    x: Math.min(from.x, to.x),
    y: Math.min(from.y, to.y),
    width: Math.abs(to.x - from.x),
    height: Math.abs(to.y - from.y),
  };
}

function pointFor(event: PointerEvent<HTMLElement>): Point {
  const bounds = event.currentTarget.getBoundingClientRect();
  return {
    x: Math.max(0, Math.min(FRAME.width, ((event.clientX - bounds.left) / bounds.width) * FRAME.width)),
    y: Math.max(0, Math.min(FRAME.height, ((event.clientY - bounds.top) / bounds.height) * FRAME.height)),
  };
}

function createBackground(): HTMLCanvasElement {
  const canvas = document.createElement("canvas");
  canvas.width = FRAME.width;
  canvas.height = FRAME.height;
  const context = canvas.getContext("2d", { alpha: false })!;
  const gradient = context.createLinearGradient(0, 0, FRAME.width, FRAME.height);
  gradient.addColorStop(0, "#dce9f8");
  gradient.addColorStop(0.5, "#b9c9df");
  gradient.addColorStop(1, "#8b9cb7");
  context.fillStyle = gradient;
  context.fillRect(0, 0, FRAME.width, FRAME.height);

  for (const [index, window] of windows.entries()) {
    context.fillStyle = "rgba(255,255,255,.92)";
    context.fillRect(window.x, window.y, window.width, window.height);
    context.fillStyle = index === 0 ? "#d54b4b" : index === 1 ? "#2177d6" : "#2d9a67";
    context.fillRect(window.x, window.y, window.width, 88);
    context.fillStyle = "rgba(34,43,58,.10)";
    for (let row = 0; row < 6; row += 1) {
      context.fillRect(window.x + 70, window.y + 150 + row * 110, window.width - 140, 32);
    }
  }

  return canvas;
}

function drawArrow(context: CanvasRenderingContext2D, from: Point, to: Point, offset: Point = { x: 0, y: 0 }) {
  const start = { x: from.x - offset.x, y: from.y - offset.y };
  const end = { x: to.x - offset.x, y: to.y - offset.y };
  const angle = Math.atan2(end.y - start.y, end.x - start.x);
  context.beginPath();
  context.moveTo(start.x, start.y);
  context.lineTo(end.x, end.y);
  context.stroke();
  context.beginPath();
  context.moveTo(end.x, end.y);
  context.lineTo(end.x - 42 * Math.cos(angle - Math.PI / 6), end.y - 42 * Math.sin(angle - Math.PI / 6));
  context.lineTo(end.x - 42 * Math.cos(angle + Math.PI / 6), end.y - 42 * Math.sin(angle + Math.PI / 6));
  context.closePath();
  context.fill();
}

function drawAnnotations(context: CanvasRenderingContext2D, annotations: Annotation[], offset: Point = { x: 0, y: 0 }) {
  context.save();
  context.strokeStyle = "#ff3b30";
  context.fillStyle = "#ff3b30";
  context.lineWidth = 12;
  context.lineJoin = "round";
  context.lineCap = "round";
  context.font = "600 58px -apple-system, BlinkMacSystemFont, sans-serif";

  for (const annotation of annotations) {
    if (annotation.kind === "text") {
      context.fillText(annotation.text, annotation.at.x - offset.x, annotation.at.y - offset.y);
      continue;
    }
    const rect = normalizedRect(annotation.from, annotation.to);
    if (annotation.kind === "rectangle") {
      context.strokeRect(rect.x - offset.x, rect.y - offset.y, rect.width, rect.height);
    } else if (annotation.kind === "arrow") {
      drawArrow(context, annotation.from, annotation.to, offset);
    } else {
      context.save();
      context.globalAlpha = 0.72;
      context.fillStyle = "#475569";
      const size = 44;
      for (let x = rect.x; x < rect.x + rect.width; x += size) {
        for (let y = rect.y; y < rect.y + rect.height; y += size) {
          if ((Math.floor(x / size) + Math.floor(y / size)) % 2 === 0) {
            context.fillRect(x - offset.x, y - offset.y, size, size);
          }
        }
      }
      context.restore();
    }
  }
  context.restore();
}

function drawOverlay(
  context: CanvasRenderingContext2D,
  background: HTMLCanvasElement | null,
  selection: Rect,
  annotations: Annotation[],
  includeBackground: boolean,
) {
  context.clearRect(0, 0, FRAME.width, FRAME.height);
  if (includeBackground && background) context.drawImage(background, 0, 0);
  context.fillStyle = "rgba(15, 23, 42, .46)";
  context.fillRect(0, 0, FRAME.width, FRAME.height);
  if (includeBackground && background) {
    context.drawImage(background, selection.x, selection.y, selection.width, selection.height, selection.x, selection.y, selection.width, selection.height);
  } else {
    context.clearRect(selection.x, selection.y, selection.width, selection.height);
  }
  context.strokeStyle = "#14b8a6";
  context.lineWidth = 7;
  context.strokeRect(selection.x, selection.y, selection.width, selection.height);
  drawAnnotations(context, annotations);
}

function SvgAnnotations({ annotations }: { annotations: Annotation[] }) {
  return (
    <svg className="svg-layer" viewBox={`0 0 ${FRAME.width} ${FRAME.height}`} aria-label="SVG annotation variant">
      <defs>
        <marker id="arrowhead" markerWidth="12" markerHeight="8" refX="11" refY="4" orient="auto">
          <polygon points="0 0, 12 4, 0 8" fill="#ff3b30" />
        </marker>
        <pattern id="mosaic" width="80" height="80" patternUnits="userSpaceOnUse">
          <rect width="40" height="40" fill="#334155" />
          <rect x="40" y="40" width="40" height="40" fill="#334155" />
        </pattern>
      </defs>
      {annotations.map((annotation) => {
        if (annotation.kind === "text") {
          return <text key={annotation.id} x={annotation.at.x} y={annotation.at.y} className="svg-text">{annotation.text}</text>;
        }
        const rect = normalizedRect(annotation.from, annotation.to);
        if (annotation.kind === "rectangle") {
          return <rect key={annotation.id} {...rect} className="svg-shape" />;
        }
        if (annotation.kind === "arrow") {
          return <line key={annotation.id} x1={annotation.from.x} y1={annotation.from.y} x2={annotation.to.x} y2={annotation.to.y} className="svg-arrow" />;
        }
        return <rect key={annotation.id} {...rect} className="svg-mosaic" />;
      })}
    </svg>
  );
}

function App() {
  const initialVariant = new URLSearchParams(window.location.search).get("variant")?.toUpperCase();
  const [variant, setVariant] = useState<Variant>(initialVariant === "B" || initialVariant === "C" ? initialVariant : "A");
  const [tool, setTool] = useState<Tool>("select");
  const [selection, setSelection] = useState<Rect>({ x: 740, y: 430, width: 1640, height: 920 });
  const [annotations, setAnnotations] = useState<Annotation[]>([]);
  const [gesture, setGesture] = useState<{ from: Point; tool: Tool } | null>(null);
  const [textDraft, setTextDraft] = useState<{ at: Point; text: string } | null>(null);
  const [status, setStatus] = useState("Ready — generated 4K screenshot background");
  const [drawMs, setDrawMs] = useState(0);
  const background = useMemo(createBackground, []);
  const backgroundUrl = useMemo(() => background.toDataURL("image/png"), [background]);
  const singleCanvas = useRef<HTMLCanvasElement>(null);
  const layeredCanvas = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    invoke<CaptureReceipt>("complete_capture", {
      pngBase64: backgroundUrl,
      widthPx: FRAME.width,
      heightPx: FRAME.height,
    })
      .then((receipt) => {
        setStatus(`4K bridge probe: ${receipt.pngBytes.toLocaleString()} bytes, sha256 ${receipt.sha256.slice(0, 12)}…`);
      })
      .catch(() => {
        // Browser-only preview has no Tauri invoke bridge; interaction remains usable.
      });
  }, [backgroundUrl]);

  const changeVariant = (next: Variant) => {
    setVariant(next);
    const url = new URL(window.location.href);
    url.searchParams.set("variant", next);
    window.history.replaceState({}, "", url);
  };

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target?.matches("input, textarea, [contenteditable=true]")) return;
      if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
        const variants: Variant[] = ["A", "B", "C"];
        const direction = event.key === "ArrowRight" ? 1 : -1;
        changeVariant(variants[(variants.indexOf(variant) + direction + variants.length) % variants.length]);
      }
      if (event.key.toLowerCase() === "v") setTool("select");
      if (event.key.toLowerCase() === "r") setTool("rectangle");
      if (event.key.toLowerCase() === "a") setTool("arrow");
      if (event.key.toLowerCase() === "t") setTool("text");
      if (event.key.toLowerCase() === "m") setTool("mosaic");
      if (event.key === "Escape") setStatus("Canceled locally — session state retained for inspection");
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [variant]);

  useEffect(() => {
    const started = performance.now();
    const canvas = variant === "A" ? singleCanvas.current : layeredCanvas.current;
    const context = canvas?.getContext("2d");
    if (context) drawOverlay(context, background, selection, annotations, variant === "A");
    const elapsed = performance.now() - started;
    setDrawMs(elapsed);
  }, [annotations, background, selection, variant]);

  const onPointerDown = (event: PointerEvent<HTMLElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId);
    const point = pointFor(event);
    if (tool === "text") {
      setTextDraft({ at: point, text: "" });
      return;
    }
    setGesture({ from: point, tool });
    if (tool === "select") setSelection({ ...point, width: 1, height: 1 });
  };

  const onPointerMove = (event: PointerEvent<HTMLElement>) => {
    if (!gesture) return;
    const point = pointFor(event);
    if (gesture.tool === "select") setSelection(normalizedRect(gesture.from, point));
  };

  const onPointerUp = (event: PointerEvent<HTMLElement>) => {
    if (!gesture) return;
    const point = pointFor(event);
    if (gesture.tool !== "select") {
      setAnnotations((items) => [...items, { id: Date.now(), kind: gesture.tool, from: gesture.from, to: point } as Annotation]);
    }
    setGesture(null);
  };

  const commitText = () => {
    if (textDraft?.text.trim()) {
      setAnnotations((items) => [...items, { id: Date.now(), kind: "text", at: textDraft.at, text: textDraft.text.trim() }]);
    }
    setTextDraft(null);
  };

  const complete = async () => {
    const output = document.createElement("canvas");
    output.width = Math.max(1, Math.round(selection.width));
    output.height = Math.max(1, Math.round(selection.height));
    const context = output.getContext("2d", { alpha: false })!;
    context.drawImage(background, selection.x, selection.y, selection.width, selection.height, 0, 0, output.width, output.height);
    context.scale(output.width / selection.width, output.height / selection.height);
    drawAnnotations(context, annotations, { x: selection.x, y: selection.y });
    const pngBase64 = output.toDataURL("image/png");
    try {
      const receipt = await invoke<CaptureReceipt>("complete_capture", {
        pngBase64,
        widthPx: output.width,
        heightPx: output.height,
      });
      setStatus(`Rust receipt: ${receipt.widthPx}×${receipt.heightPx}, ${receipt.pngBytes.toLocaleString()} bytes, sha256 ${receipt.sha256.slice(0, 12)}…`);
    } catch (error) {
      setStatus(`Web preview generated ${(pngBase64.length / 1024).toFixed(1)} KiB data URL (${String(error)})`);
    }
  };

  const cancel = async () => {
    try {
      const result = await invoke<string>("cancel_capture");
      setStatus(`Rust receipt: ${result}`);
    } catch {
      setStatus("Canceled in Web preview");
    }
  };

  const surfaceHandlers = { onPointerDown, onPointerMove, onPointerUp };

  return (
    <main className="prototype-shell">
      <section
        className={`capture-surface variant-${variant.toLowerCase()}`}
        aria-label="Snaploom screenshot overlay prototype"
        {...surfaceHandlers}
      >
        {variant === "A" && <canvas ref={singleCanvas} width={FRAME.width} height={FRAME.height} className="full-canvas" />}
        {variant === "B" && (
          <>
            <img src={backgroundUrl} className="background-image" alt="Generated 4K screenshot" draggable={false} />
            <canvas ref={layeredCanvas} width={FRAME.width} height={FRAME.height} className="full-canvas overlay-canvas" />
          </>
        )}
        {variant === "C" && (
          <>
            <img src={backgroundUrl} className="background-image" alt="Generated 4K screenshot" draggable={false} />
            <div className="dom-mask" />
            <div
              className="dom-selection"
              style={{
                left: `${(selection.x / FRAME.width) * 100}%`,
                top: `${(selection.y / FRAME.height) * 100}%`,
                width: `${(selection.width / FRAME.width) * 100}%`,
                height: `${(selection.height / FRAME.height) * 100}%`,
              }}
            />
            <SvgAnnotations annotations={annotations} />
          </>
        )}

        {textDraft && (
          <textarea
            className="text-editor"
            autoFocus
            value={textDraft.text}
            style={{ left: `${(textDraft.at.x / FRAME.width) * 100}%`, top: `${(textDraft.at.y / FRAME.height) * 100}%` }}
            onChange={(event) => setTextDraft({ ...textDraft, text: event.currentTarget.value })}
            onPointerDown={(event) => event.stopPropagation()}
            onBlur={commitText}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
                event.preventDefault();
                commitText();
              }
            }}
            placeholder="输入中文 / Type text"
          />
        )}

        <div className="selection-size" style={{ left: `${(selection.x / FRAME.width) * 100}%`, top: `${((selection.y + selection.height) / FRAME.height) * 100}%` }}>
          {Math.round(selection.width)} × {Math.round(selection.height)}
        </div>

        <nav className="toolbar" aria-label="Annotation tools" onPointerDown={(event) => event.stopPropagation()}>
          {(["select", "rectangle", "arrow", "text", "mosaic"] as Tool[]).map((item) => (
            <button key={item} className={tool === item ? "active" : ""} onClick={() => setTool(item)}>
              {item === "select" ? "V 选择" : item === "rectangle" ? "R 矩形" : item === "arrow" ? "A 箭头" : item === "text" ? "T 文字" : "M 马赛克"}
            </button>
          ))}
          <span className="divider" />
          <button onClick={() => setAnnotations((items) => items.slice(0, -1))}>撤销</button>
          <button className="complete" onClick={complete}>完成</button>
          <button onClick={cancel}>取消</button>
        </nav>

        <aside className="metrics">
          <strong>PROTOTYPE</strong>
          <span>Variant {variant}</span>
          <span>4K backing store</span>
          <span>draw {drawMs.toFixed(2)} ms</span>
          <span>{annotations.length} annotations</span>
          <span>{status}</span>
        </aside>
      </section>

      <div className="variant-switcher" role="toolbar" aria-label="Prototype variants">
        <button onClick={() => changeVariant(variant === "A" ? "C" : variant === "B" ? "A" : "B")} aria-label="Previous variant">←</button>
        <span>{variant === "A" ? "A — Single Canvas" : variant === "B" ? "B — Layered Canvas" : "C — SVG + DOM"}</span>
        <button onClick={() => changeVariant(variant === "A" ? "B" : variant === "B" ? "C" : "A")} aria-label="Next variant">→</button>
      </div>
    </main>
  );
}

export default App;
