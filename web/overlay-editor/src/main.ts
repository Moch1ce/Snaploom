import { createProductMark, screenshotUiTheme } from "@snaploom/screenshot-ui";
import { describeOverlayShell } from "./app";
import "./style.css";

const root = document.querySelector<HTMLElement>("#app");
if (!root) throw new Error("missing #app");

const canvas = document.createElement("canvas");
canvas.id = "capture-surface";
canvas.setAttribute("aria-label", "截图编辑画布");
canvas.dataset.status = describeOverlayShell().status;
root.style.setProperty("--selection", screenshotUiTheme.colors.selection);
root.append(canvas, createProductMark("Snaploom Capture Host"));
