import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import {
  createProductMark,
  screenshotUiTheme,
} from "@snaploom/screenshot-ui";
import {
  type Language,
  type SettingsMutation,
  type SettingsSnapshot,
  type UpdateState,
  message,
  updateMessage,
} from "./app";
import "./style.css";

interface CaptureTerminalEvent {
  readonly terminal: "completed" | "canceled" | string;
}

type CapturePermissionView =
  | "granted"
  | "notGranted"
  | "restartRequired"
  | "notApplicable";

const rootElement = document.querySelector<HTMLElement>("#app");
if (!rootElement) throw new Error("missing #app");
const root: HTMLElement = rootElement;
let activeLanguage: Language = "en";
let activeStatus: HTMLElement | null = null;
let activeCaptureButton: HTMLButtonElement | null = null;
let activePermissionStatus: HTMLElement | null = null;

document.documentElement.style.setProperty(
  "--snaploom-brand",
  screenshotUiTheme.colors.brand,
);
document.documentElement.style.setProperty(
  "--snaploom-border",
  screenshotUiTheme.colors.border,
);

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
  text?: string,
): HTMLElementTagNameMap[K] {
  const value = document.createElement(tag);
  if (className) value.className = className;
  if (text) value.textContent = text;
  return value;
}

function button(label: string, action: () => Promise<void>): HTMLButtonElement {
  const control = element("button", "button", label);
  control.type = "button";
  control.addEventListener("click", async () => {
    control.disabled = true;
    try {
      await action();
    } finally {
      control.disabled = false;
    }
  });
  return control;
}

async function boot(): Promise<void> {
  const snapshot = await invoke<SettingsSnapshot>("settings_snapshot");
  render(snapshot);
}

function render(snapshot: SettingsSnapshot): void {
  root.replaceChildren();
  const language = snapshot.settings.language;
  activeLanguage = language;
  const header = element("header", "header");
  header.append(createProductMark(message(language, "ready")));
  header.append(element("p", "subtitle", message(language, "subtitle")));
  const status = element("p", "status");
  activeStatus = status;
  status.setAttribute("role", "status");
  header.append(status);
  const captureButton = button(message(language, "startCapture"), async () => {
    await invoke("start_capture");
  });
  activeCaptureButton = captureButton;
  header.append(captureButton);
  root.append(header);

  const general = section(message(language, "general"));
  const shortcutRow = row(message(language, "shortcut"));
  const shortcut = element("input", "text-input");
  shortcut.value = snapshot.settings.shortcut;
  shortcut.autocomplete = "off";
  shortcut.setAttribute("aria-label", message(language, "shortcut"));
  shortcutRow.append(shortcut);
  shortcutRow.append(
    button(message(language, "apply"), async () => {
      try {
        const mutation = await invoke<SettingsMutation>("replace_shortcut", {
          shortcut: shortcut.value,
        });
        showMutation(status, language, mutation);
      } catch {
        status.textContent = message(language, "actionFailed");
      }
    }),
  );
  shortcutRow.append(element("small", "hint", message(language, "shortcutHint")));
  general.append(shortcutRow);

  const autostartRow = row(message(language, "autostart"));
  const autostart = element("input");
  autostart.type = "checkbox";
  autostart.checked = snapshot.settings.autostart;
  autostart.addEventListener("change", async () => {
    autostart.disabled = true;
    try {
      const mutation = await invoke<SettingsMutation>("set_autostart", {
        enabled: autostart.checked,
      });
      showMutation(status, language, mutation);
    } catch {
      autostart.checked = !autostart.checked;
      status.textContent = message(language, "actionFailed");
    } finally {
      autostart.disabled = false;
    }
  });
  autostartRow.append(autostart);
  general.append(autostartRow);

  const languageRow = row(message(language, "language"));
  const languageSelect = element("select", "select");
  languageSelect.append(new Option("简体中文", "zh-cn"));
  languageSelect.append(new Option("English", "en"));
  languageSelect.value = language;
  languageSelect.addEventListener("change", async () => {
    const next = languageSelect.value as Language;
    const mutation = await invoke<SettingsMutation>("set_language", {
      language: next,
    });
    render({ ...snapshot, settings: mutation.settings });
  });
  languageRow.append(languageSelect);
  general.append(languageRow);

  if (snapshot.platform === "macos") {
    const permissionRow = row(message(language, "permission"));
    const permissionStatus = element("span", "permission-state");
    permissionStatus.setAttribute("role", "status");
    activePermissionStatus = permissionStatus;
    const requestButton = button(
      message(language, "requestPermission"),
      async () => {
        try {
          updatePermissionStatus(
            permissionStatus,
            language,
            await invoke<CapturePermissionView>("request_capture_permission"),
          );
        } catch {
          permissionStatus.textContent = message(language, "actionFailed");
        }
      },
    );
    const openButton = button(message(language, "openPermission"), async () => {
      try {
        await invoke("open_capture_permission_settings");
      } catch {
        permissionStatus.textContent = message(language, "actionFailed");
      }
    });
    const permissionActions = element("div", "permission-actions");
    permissionActions.append(requestButton, openButton);
    permissionRow.append(permissionStatus, permissionActions);
    general.append(permissionRow);
    void refreshPermission(permissionStatus, language);
  } else {
    activePermissionStatus = null;
  }

  root.append(general);

  const privacy = section(message(language, "privacy"));
  privacy.append(element("p", "hint", message(language, "privacyHint")));
  const privacyActions = element("div", "actions");
  privacyActions.append(
    button(message(language, "openLogs"), () => invoke("open_privacy_logs")),
    button(message(language, "clearLogs"), () => invoke("clear_privacy_logs")),
  );
  privacy.append(privacyActions);
  root.append(privacy);

  const updates = section(message(language, "updates"));
  updates.append(element("p", "hint", message(language, "updatesHint")));
  const updateState = element("p", "update-state");
  updateState.setAttribute("role", "status");
  const updateActions = element("div", "actions");
  const releaseButton = button(message(language, "openRelease"), () =>
    invoke("open_verified_release"),
  );
  releaseButton.hidden = true;
  updateActions.append(
    button(message(language, "checkUpdates"), async () => {
      const result = await invoke<UpdateState>("check_for_updates");
      updateState.textContent = updateMessage(language, result);
      releaseButton.hidden = result.state !== "available";
    }),
    releaseButton,
  );
  updates.append(updateActions, updateState);
  root.append(updates);

}

async function refreshPermission(
  target: HTMLElement,
  language: Language,
): Promise<void> {
  try {
    updatePermissionStatus(
      target,
      language,
      await invoke<CapturePermissionView>("capture_permission"),
    );
  } catch {
    target.textContent = message(language, "actionFailed");
  }
}

function updatePermissionStatus(
  target: HTMLElement,
  language: Language,
  permission: CapturePermissionView,
): void {
  target.textContent =
    permission === "granted"
      ? message(language, "permissionGranted")
      : permission === "restartRequired"
        ? message(language, "permissionRestart")
        : permission === "notGranted"
          ? message(language, "permissionRequired")
          : "";
}

function section(title: string): HTMLElement {
  const value = element("section", "section");
  value.append(element("h2", undefined, title));
  return value;
}

function row(label: string): HTMLElement {
  const value = element("div", "row");
  value.append(element("label", "label", label));
  return value;
}

function showMutation(
  target: HTMLElement,
  language: Language,
  mutation: SettingsMutation,
): void {
  target.textContent = message(
    language,
    mutation.persisted ? "saved" : "processOnly",
  );
}

void boot().catch(() => {
  root.replaceChildren(createProductMark("Snaploom"));
  root.append(element("p", "status", "Desktop service unavailable."));
});

void listen<CaptureTerminalEvent>("capture-terminal", ({ payload }) => {
  if (!activeStatus) return;
  const hostUnavailable = [
    "hostNotFound",
    "hostStartFailed",
    "hostStartTimeout",
    "platformUnavailable",
  ].includes(payload.terminal);
  if (hostUnavailable && activeCaptureButton) {
    activeCaptureButton.disabled = true;
  }
  const permissionFailure =
    payload.terminal === "permissionNotGranted" ||
    payload.terminal === "permissionRevoked";
  if (permissionFailure && activePermissionStatus) {
    void refreshPermission(activePermissionStatus, activeLanguage);
  }
  activeStatus.textContent =
    payload.terminal === "completed"
      ? message(activeLanguage, "captureCompleted")
      : payload.terminal === "canceled"
        ? message(activeLanguage, "captureCanceled")
        : permissionFailure
          ? message(activeLanguage, "capturePermissionError")
          : hostUnavailable
            ? message(activeLanguage, "captureHostMissing")
            : payload.terminal === "captureUnavailable" ||
                payload.terminal === "displayUnavailable"
              ? message(activeLanguage, "captureUnavailable")
              : message(activeLanguage, "captureUnexpectedError");
});
