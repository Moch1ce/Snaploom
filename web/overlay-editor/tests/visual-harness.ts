import "@fontsource/inter/400.css";
import "@fontsource/noto-sans-sc/chinese-simplified-400.css";
import { OverlayEditor, type CaptureSnapshot } from "../src/app";
import type { AnnotationTool } from "../src/annotations";
import fixtures from "../../../testing/goldens/fixtures.json" with { type: "json" };

type Fixture = (typeof fixtures.render)[number];
type FixtureName = Fixture["name"];

function isFixtureName(value: string | null): value is FixtureName {
  return fixtures.render.some((fixture) => fixture.name === value);
}

function fixtureForName(name: FixtureName): Fixture {
  const fixture = fixtures.render.find((candidate) => candidate.name === name);
  if (!fixture) throw new Error(`unknown fixture: ${name}`);
  return fixture;
}

function createCapture(scale: number): {
  snapshot: CaptureSnapshot;
  binary: ArrayBuffer;
} {
  const width = 96 * scale;
  const height = 64 * scale;
  const bytes = new Uint8Array(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const offset = (y * width + x) * 4;
      const checker = (Math.floor(x / 12) + Math.floor(y / 12)) % 2;
      bytes[offset] = (x * 3 + y) % 256;
      bytes[offset + 1] = checker ? 196 : (y * 4) % 256;
      bytes[offset + 2] = checker ? (x * 2) % 256 : 232;
      bytes[offset + 3] = 255;
    }
  }
  return {
    snapshot: {
      sessionId: "render-golden",
      physicalSize: { width, height },
      logicalSize: { width: 96, height: 64 },
      globalOrigin: { x: -320, y: 40 },
      pointerPhysical: { x: 96, y: 64 },
      workAreaLogical: { x: -160, y: 20, width: 96, height: 64 },
      stride: width * 4,
      windows: [],
    },
    binary: bytes.buffer,
  };
}

function gesture(
  editor: OverlayEditor,
  tool: AnnotationTool,
  start: { x: number; y: number },
  end: { x: number; y: number },
): void {
  const session = editor.annotations;
  const scale = editor.model.scale;
  session.setTool(tool, false);
  session.pointerDown({ x: start.x * scale.scaleX, y: start.y * scale.scaleY });
  session.pointerMove({ x: end.x * scale.scaleX, y: end.y * scale.scaleY });
  session.pointerUp({ x: end.x * scale.scaleX, y: end.y * scale.scaleY });
}

function drawFixture(editor: OverlayEditor, fixture: FixtureName): void {
  const session = editor.annotations;
  const composite = fixture === "composite-overlap" || fixture.startsWith("dpi-");
  if (fixture === "rectangle" || composite) {
    session.setStyle({ color: "#FF4D4F", strokeWidth: 4 });
    gesture(editor, "rectangle", { x: 14, y: 14 }, { x: 54, y: 40 });
  }
  if (fixture === "arrow" || composite) {
    session.setStyle({ color: "#1677FF", strokeWidth: 8 });
    gesture(editor, "arrow", { x: 24, y: 46 }, { x: 78, y: 18 });
  }
  if (fixture === "mosaic" || composite) {
    session.setStyle({ mosaicBrushSize: 32, mosaicBlockSize: 12 });
    gesture(
      editor,
      "mosaic",
      composite ? { x: 60, y: 46 } : { x: 20, y: 44 },
      composite ? { x: 82, y: 46 } : { x: 74, y: 44 },
    );
  }
  if (fixture === "text" || composite) {
    session.setStyle({ color: "#202124", fontSize: 16 });
    session.setTool("text", false);
    const textOrigin = composite
      ? { x: 60 * editor.model.scale.scaleX, y: 30 * editor.model.scale.scaleY }
      : { x: 36, y: 36 };
    session.pointerDown(textOrigin);
    session.pointerUp(textOrigin);
    session.updateTextDraft("中");
    session.compositionStart();
    session.compositionUpdate("中文");
    session.compositionEnd("中文");
    session.updateTextDraft("中文 Snaploom");
    session.commitTextDraft();
  }
}

async function main(): Promise<void> {
  const fixture = new URLSearchParams(location.search).get("fixture");
  if (!isFixtureName(fixture)) throw new Error(`unknown fixture: ${fixture}`);
  const definition = fixtureForName(fixture);
  await document.fonts.load("32px Inter", "Snaploom");
  await document.fonts.load('32px "Noto Sans SC"', "中文");
  await document.fonts.ready;

  const canvas = document.createElement("canvas");
  const toolbar = document.createElement("div");
  const sizeLabel = document.createElement("div");
  const capture = createCapture(definition.scale);
  const editor = new OverlayEditor(capture.snapshot, capture.binary, {
    canvas,
    toolbar,
    sizeLabel,
  }, {
    renderFontFamily: 'Inter, "Noto Sans SC", sans-serif',
  });
  const [startX, startY] = definition.start as [number, number];
  const [endX, endY] = definition.end as [number, number];
  editor.pointerDown({ x: startX, y: startY });
  editor.pointerMove({ x: endX, y: endY });
  editor.pointerUp({ x: endX, y: endY });
  drawFixture(editor, fixture);

  const png = await editor.composePng();
  const image = document.createElement("img");
  image.dataset.testid = "render-output";
  image.alt = `${fixture} final PNG`;
  image.src = URL.createObjectURL(new Blob([png], { type: "image/png" }));
  await image.decode();
  image.dataset.ready = "true";
  document.body.append(image);
  editor.dispose();
}

main().catch((error: unknown) => {
  const output = document.createElement("pre");
  output.dataset.testid = "render-error";
  output.textContent =
    error instanceof Error ? (error.stack ?? error.message) : String(error);
  document.body.append(output);
});
