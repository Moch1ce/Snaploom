import type { ScreenshotToolbarLabels } from "@snaploom/screenshot-ui";

export type OverlayLanguage = "zh-cn" | "en";

const resources = {
  "zh-cn": {
    toolbar: "截图工具",
    rectangle: "矩形",
    arrow: "箭头",
    text: "文字",
    mosaic: "马赛克",
    undo: "撤销",
    redo: "重做",
    save: "保存 PNG",
    cancel: "取消",
    complete: "完成",
    canvas: "截图编辑画布",
    textEditor: "文字标注输入",
    annotationStyle: "标注样式",
    color: "颜色",
    strokeWidth: "线宽",
    fontSize: "字号",
    mosaicBrush: "马赛克画笔",
    mosaicStrength: "马赛克强度",
    clipboardWriteFailed: "复制失败，请重试",
    saveFailed: "保存失败，请更换位置后重试",
    pngEncodeFailed: "图片生成失败，请重试",
    overlayCloseFailed: "截图窗口关闭失败",
    overlayRestoreFailed: "截图窗口恢复失败，请重新唤起截图",
  },
  en: {
    toolbar: "Screenshot tools",
    rectangle: "Rectangle",
    arrow: "Arrow",
    text: "Text",
    mosaic: "Mosaic",
    undo: "Undo",
    redo: "Redo",
    save: "Save PNG",
    cancel: "Cancel",
    complete: "Complete",
    canvas: "Screenshot editor canvas",
    textEditor: "Text annotation input",
    annotationStyle: "Annotation style",
    color: "Color",
    strokeWidth: "Stroke width",
    fontSize: "Font size",
    mosaicBrush: "Mosaic brush",
    mosaicStrength: "Mosaic strength",
    clipboardWriteFailed: "Copy failed. Try again.",
    saveFailed: "Save failed. Choose another location and try again.",
    pngEncodeFailed: "Image generation failed. Try again.",
    overlayCloseFailed: "The capture window could not close.",
    overlayRestoreFailed: "The capture window could not be restored. Start capture again.",
  },
} as const;

export type OverlayMessageKey = keyof (typeof resources)["en"];

export function overlayMessage(
  language: OverlayLanguage,
  key: OverlayMessageKey,
): string {
  return resources[language][key];
}

export function overlayResourceKeys(language: OverlayLanguage): readonly string[] {
  return Object.keys(resources[language]).sort();
}

export function overlayToolbarLabels(
  language: OverlayLanguage,
): ScreenshotToolbarLabels {
  return {
    toolbar: overlayMessage(language, "toolbar"),
    actions: {
      rectangle: overlayMessage(language, "rectangle"),
      arrow: overlayMessage(language, "arrow"),
      text: overlayMessage(language, "text"),
      mosaic: overlayMessage(language, "mosaic"),
      undo: overlayMessage(language, "undo"),
      redo: overlayMessage(language, "redo"),
      save: overlayMessage(language, "save"),
      cancel: overlayMessage(language, "cancel"),
      complete: overlayMessage(language, "complete"),
    },
  };
}
