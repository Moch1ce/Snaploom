import { createProductMark } from "@snaploom/screenshot-ui";
import { desktopStatus } from "./app";
import "./style.css";

const root = document.querySelector<HTMLElement>("#app");
if (!root) throw new Error("missing #app");
root.append(createProductMark(desktopStatus()));
