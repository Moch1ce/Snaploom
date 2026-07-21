export type Language = "zh-cn" | "en";

export interface AppSettings {
  readonly schemaVersion: number;
  readonly shortcut: string;
  readonly autostart: boolean;
  readonly recentAnnotationStyles: Readonly<Record<string, string>>;
  readonly recentSaveDirectory: string | null;
  readonly language: Language;
}

export interface SettingsSnapshot {
  readonly settings: AppSettings;
  readonly platform: "windows" | "macos";
}

export interface SettingsMutation {
  readonly settings: AppSettings;
  readonly persisted: boolean;
}

export type UpdateState =
  | { readonly state: "current" }
  | {
      readonly state: "available";
      readonly version: string;
      readonly releaseUrl: string;
    }
  | { readonly state: "networkError" }
  | { readonly state: "rateLimited" }
  | { readonly state: "invalidResponse" };

export const settingsControlKeys = [
  "shortcut",
  "autostart",
  "language",
  "privacyLogs",
  "updates",
] as const;

const resources = {
  "zh-cn": {
    ready: "Snaploom 已就绪",
    subtitle: "截图在托盘中运行，仅在您主动触发时访问屏幕。",
    startCapture: "开始截图",
    general: "通用",
    shortcut: "截图快捷键",
    shortcutHint: "快捷键必须包含至少一个修饰键。",
    apply: "应用",
    autostart: "开机启动",
    language: "语言",
    permission: "屏幕录制权限",
    permissionGranted: "已授权",
    permissionRequired: "需要授权",
    permissionRestart: "已授权，重新启动后生效",
    requestPermission: "请求权限",
    openPermission: "打开系统设置",
    privacy: "隐私",
    privacyHint: "日志不会记录截图、识别文本、剪贴板内容、完整路径或错误详情。",
    openLogs: "打开日志目录",
    clearLogs: "清除日志",
    updates: "更新",
    updatesHint: "仅在点击检查时访问此项目的 GitHub Releases，不会自动下载。",
    checkUpdates: "检查更新",
    openRelease: "打开发布页",
    current: "当前已是最新版本。",
    available: "发现新版本：",
    networkError: "无法连接更新服务。",
    rateLimited: "更新服务请求受限，请稍后再试。",
    invalidResponse: "更新信息无效。",
    saved: "设置已保存。",
    processOnly: "设置已在本次运行中生效，但未能写入磁盘。",
    actionFailed: "操作失败，请重试。",
    captureCompleted: "截图已完成。",
    captureCanceled: "截图已取消。",
    capturePermissionError: "Capture Host 需要屏幕录制权限。请在系统设置中授权后重试。",
    captureHostMissing: "Capture Host 不可用；设置仍可正常使用。",
  },
  en: {
    ready: "Snaploom is ready",
    subtitle: "Capture runs from the tray and accesses the screen only when you ask.",
    startCapture: "Start Capture",
    general: "General",
    shortcut: "Capture Shortcut",
    shortcutHint: "The shortcut must include at least one modifier.",
    apply: "Apply",
    autostart: "Launch at Login",
    language: "Language",
    permission: "Screen Recording Permission",
    permissionGranted: "Granted",
    permissionRequired: "Permission required",
    permissionRestart: "Granted; restart required",
    requestPermission: "Request Permission",
    openPermission: "Open System Settings",
    privacy: "Privacy",
    privacyHint: "Logs never contain screenshots, recognized text, clipboard data, full paths, or error details.",
    openLogs: "Open Log Folder",
    clearLogs: "Clear Logs",
    updates: "Updates",
    updatesHint: "GitHub Releases is contacted only when you click Check. Nothing is downloaded automatically.",
    checkUpdates: "Check for Updates",
    openRelease: "Open Release",
    current: "Snaploom is up to date.",
    available: "Update available: ",
    networkError: "Could not reach the update service.",
    rateLimited: "Update checks are rate limited. Try again later.",
    invalidResponse: "The update response is invalid.",
    saved: "Settings saved.",
    processOnly: "The setting is active for this run but could not be written to disk.",
    actionFailed: "The action failed. Try again.",
    captureCompleted: "Capture completed.",
    captureCanceled: "Capture canceled.",
    capturePermissionError: "Capture Host needs Screen Recording permission. Grant it in System Settings, then try again.",
    captureHostMissing: "Capture Host is unavailable. Settings remain available.",
  },
} as const;

export type MessageKey = keyof (typeof resources)["en"];

export function message(language: Language, key: MessageKey): string {
  return resources[language][key];
}

export function resourceKeys(language: Language): readonly string[] {
  return Object.keys(resources[language]).sort();
}

export function languageFromLocale(locale: string | undefined): Language {
  return locale?.toLowerCase().startsWith("zh") ? "zh-cn" : "en";
}

export function updateMessage(language: Language, update: UpdateState): string {
  switch (update.state) {
    case "current":
      return message(language, "current");
    case "available":
      return `${message(language, "available")}${update.version}`;
    case "networkError":
      return message(language, "networkError");
    case "rateLimited":
      return message(language, "rateLimited");
    case "invalidResponse":
      return message(language, "invalidResponse");
  }
}
