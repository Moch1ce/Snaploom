import "@fontsource/inter/400.css";
import "@fontsource/noto-sans-sc/chinese-simplified-400.css";

export async function installUiGoldenFonts(): Promise<void> {
  await document.fonts.load("24px Inter", "Snaploom 2026");
  await document.fonts.load('24px "Noto Sans SC"', "中文");
  await document.fonts.ready;
  const root = document.querySelector<HTMLElement>("#app");
  if (!root) throw new Error("missing #app");
  root.style.setProperty(
    "--ui-font-family",
    'Inter, "Noto Sans SC", sans-serif',
  );
}
