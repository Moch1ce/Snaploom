import { expect, test } from "@playwright/test";

const fixtures = [
  { name: "rectangle", width: 160, height: 96 },
  { name: "arrow", width: 160, height: 96 },
  { name: "text", width: 160, height: 96 },
  { name: "mosaic", width: 160, height: 96 },
  { name: "composite-overlap", width: 160, height: 96 },
  { name: "dpi-100", width: 80, height: 48 },
  { name: "dpi-125", width: 100, height: 60 },
  { name: "dpi-150", width: 120, height: 72 },
  { name: "dpi-175", width: 140, height: 84 },
  { name: "dpi-200", width: 160, height: 96 },
] as const;

for (const fixture of fixtures) {
  test(`${fixture.name} final renderer object list and cropped PNG`, async ({
    page,
  }) => {
    await page.goto(`/tests/visual-harness.html?fixture=${fixture.name}`);
    const output = page.getByTestId("render-output");
    await expect(output).toHaveAttribute("data-ready", "true");
    await expect(output).toHaveJSProperty("naturalWidth", fixture.width);
    await expect(output).toHaveJSProperty("naturalHeight", fixture.height);
    const metadata = await output.evaluate(async (element) => {
      const response = await fetch((element as HTMLImageElement).src);
      const bytes = new Uint8Array(await response.arrayBuffer());
      const signature = [...bytes.subarray(0, 8)]
        .map((byte) => byte.toString(16).padStart(2, "0"))
        .join("");
      const chunks: string[] = [];
      const decoder = new TextDecoder("ascii");
      for (let offset = 8; offset + 12 <= bytes.length; ) {
        const view = new DataView(bytes.buffer, bytes.byteOffset + offset, 4);
        const length = view.getUint32(0);
        chunks.push(decoder.decode(bytes.subarray(offset + 4, offset + 8)));
        offset += length + 12;
      }
      return {
        signature,
        bitDepth: bytes[24],
        colorType: bytes[25],
        ancillaryChunks: chunks.filter((chunk) => /^[a-z]/.test(chunk)),
      };
    });
    expect(metadata).toEqual({
      signature: "89504e470d0a1a0a",
      bitDepth: 8,
      colorType: 6,
      ancillaryChunks: [],
    });
    await expect(output).toHaveScreenshot(`${fixture.name}-final-png.png`, {
      animations: "disabled",
      caret: "hide",
      maxDiffPixels: 0,
      threshold: 0,
      scale: "css",
    });
  });
}
