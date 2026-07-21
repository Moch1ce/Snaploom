import { expect, test, type Locator, type Page } from "@playwright/test";
import fixtures from "../../../testing/goldens/fixtures.json" with { type: "json" };

function uiFixture(name: string) {
  const fixture = fixtures.ui.find((candidate) => candidate.name === name);
  if (!fixture) throw new Error(`unknown UI golden fixture: ${name}`);
  return fixture;
}

async function boundingBox(locator: Locator) {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  return box!;
}

async function installControlledFonts(page: Page): Promise<void> {
  await page.evaluate(async () => {
    const modulePath = "/tests/ui-golden-fonts.ts";
    const fonts = await import(modulePath);
    await fonts.installUiGoldenFonts();
  });
}

test("production overlay keeps pointer capture and renders toolbar settings geometry", async ({
  page,
}) => {
  const fixture = uiFixture("toolbar-settings");
  await page.setViewportSize({ width: fixture.width, height: fixture.height });
  await page.goto("/");
  const root = page.locator("#app");
  const canvas = page.locator("#capture-surface");
  await expect(root).toHaveAttribute("data-status", "ready");
  await installControlledFonts(page);

  await page.mouse.move(700, 360);
  await page.mouse.down();
  await expect
    .poll(() => canvas.evaluate((element) => element.hasPointerCapture(1)))
    .toBe(true);
  await page.mouse.move(120, 80);
  await page.mouse.up();
  await expect
    .poll(() => canvas.evaluate((element) => element.hasPointerCapture(1)))
    .toBe(false);

  const sizeLabel = page.locator(".screenshot-size-label");
  const toolbar = page.locator(".screenshot-toolbar");
  await expect(sizeLabel).toHaveText("928 × 448");
  await expect(toolbar).toBeVisible();
  await page.locator('[data-action="rectangle"]').click();
  const settings = page.locator(".annotation-settings");
  await expect(settings).toBeVisible();

  const toolbarBox = await boundingBox(toolbar);
  const settingsBox = await boundingBox(settings);
  const labelBox = await boundingBox(sizeLabel);
  expect(toolbarBox.width).toBe(430);
  expect(toolbarBox.height).toBe(44);
  expect(settingsBox.y).toBeGreaterThan(toolbarBox.y);
  expect(settingsBox.x).toBeGreaterThanOrEqual(0);
  expect(settingsBox.x + settingsBox.width).toBeLessThanOrEqual(fixture.width);
  expect(labelBox.y + labelBox.height).toBeLessThanOrEqual(80);

  await expect(root).toHaveScreenshot(["ui", `${fixture.name}.png`], {
    animations: "disabled",
    caret: "hide",
    maxDiffPixels: 0,
    threshold: 0,
    scale: "css",
  });
});

test("production textarea preserves IME preedit and accepts numeric symbol emoji text", async ({
  page,
}) => {
  const fixture = uiFixture("textarea-composition");
  await page.setViewportSize({ width: fixture.width, height: fixture.height });
  await page.goto("/");
  const root = page.locator("#app");
  await expect(root).toHaveAttribute("data-status", "ready");
  await installControlledFonts(page);

  await page.mouse.move(700, 360);
  await page.mouse.down();
  await page.mouse.move(120, 80);
  await page.mouse.up();
  await page.locator('[data-action="text"]').click();
  await page.mouse.click(300, 180);

  const textarea = page.locator(".text-editor textarea");
  const frame = page.locator(".text-editor");
  await expect(textarea).toBeVisible();
  await textarea.evaluate((element) => {
    const input = element as HTMLTextAreaElement;
    input.dispatchEvent(
      new CompositionEvent("compositionstart", { bubbles: true, data: "" }),
    );
    input.value = "中文";
    input.dispatchEvent(
      new CompositionEvent("compositionupdate", {
        bubbles: true,
        data: "中文",
      }),
    );
    input.dispatchEvent(
      new InputEvent("input", {
        bubbles: true,
        data: "中文",
        inputType: "insertCompositionText",
        isComposing: true,
      }),
    );
  });
  await expect
    .poll(() =>
      page.evaluate(() =>
        window.__SNAPLOOM_OVERLAY__.editor.annotations.snapshotState().textDraft,
      ),
    )
    .toMatchObject({ value: "中文", preedit: "中文", isComposing: true });

  const frameBox = await boundingBox(frame);
  expect(frameBox.x).toBeGreaterThanOrEqual(120);
  expect(frameBox.y).toBeGreaterThanOrEqual(80);
  expect(frameBox.x + frameBox.width).toBeLessThanOrEqual(700);
  expect(frameBox.y + frameBox.height).toBeLessThanOrEqual(360);
  await expect(root).toHaveScreenshot(["ui", `${fixture.name}.png`], {
    animations: "disabled",
    caret: "hide",
    maxDiffPixels: 0,
    threshold: 0,
    scale: "css",
  });

  const finalText = "中文 Snaploom 2026 #+!? ✓ 😀";
  await textarea.evaluate((element, value) => {
    const input = element as HTMLTextAreaElement;
    input.dispatchEvent(
      new CompositionEvent("compositionend", {
        bubbles: true,
        data: "中文",
      }),
    );
    input.value = value;
    input.dispatchEvent(
      new InputEvent("input", {
        bubbles: true,
        data: value,
        inputType: "insertText",
        isComposing: false,
      }),
    );
  }, finalText);
  const output = await page.evaluate(async () => {
    const editor = window.__SNAPLOOM_OVERLAY__.editor;
    const png = await editor.composePng();
    return {
      bytes: png.byteLength,
      text: editor.annotations.snapshotState().objects.at(-1),
    };
  });
  expect(output.bytes).toBeGreaterThan(0);
  expect(output.text).toMatchObject({ kind: "text", text: finalText });
});
