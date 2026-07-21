export const screenshotUiTheme = {
  colors: {
    overlayMask: "rgba(0, 0, 0, 0.48)",
    selection: "#1677ff",
    surface: "#1f1f1f",
    foreground: "#ffffff",
  },
  radius: 8,
} as const;

export function createProductMark(label: string): HTMLElement {
  const mark = document.createElement("strong");
  mark.className = "snaploom-product-mark";
  mark.textContent = label;
  return mark;
}
