# Snaploom Tauri screenshot-chain risk prototype

> PROTOTYPE — throwaway evidence for the question: can one opaque Tauri 2
> WebView cover a display, render a screenshot background, support selection,
> Canvas/SVG annotations and IME text, then return the final PNG to Rust at 4K?

Run from this directory:

```sh
PATH="/opt/homebrew/opt/rustup/bin:$PATH" pnpm install
PATH="/opt/homebrew/opt/rustup/bin:$PATH" pnpm prototype
```

Use the bottom switcher or `?variant=A|B|C` to compare:

- A — a single Canvas owns the screenshot, mask, selection and annotations.
- B — an opaque background layer plus a Canvas interaction layer.
- C — an opaque background layer plus SVG/DOM controls and native textarea IME.

The prototype intentionally uses a generated 3840×2160 background rather than
requesting screen-recording permission. Platform capture is decided separately;
this branch isolates the WebView rendering, input and PNG handoff risks.

## Recommended IDE Setup

- [VS Code](https://code.visualstudio.com/) + [Tauri](https://marketplace.visualstudio.com/items?itemName=tauri-apps.tauri-vscode) + [rust-analyzer](https://marketplace.visualstudio.com/items?itemName=rust-lang.rust-analyzer)
