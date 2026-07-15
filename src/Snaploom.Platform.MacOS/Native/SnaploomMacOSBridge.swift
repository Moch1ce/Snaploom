import AppKit
import Carbon
import CoreGraphics
import CoreVideo
import ScreenCaptureKit
import ServiceManagement
import UniformTypeIdentifiers

private typealias HotKeyCallback = @convention(c) () -> Void
private typealias ResumeCallback = @convention(c) () -> Void

private var hotKeyCallback: HotKeyCallback?
private var hotKeyReference: EventHotKeyRef?
private var hotKeyHandlerReference: EventHandlerRef?
private var resumeCallback: ResumeCallback?
private var wakeObserver: NSObjectProtocol?

private let hotKeyEventHandler: EventHandlerUPP = { _, _, _ in
    hotKeyCallback?()
    return noErr
}

fileprivate struct WindowCandidateInfo {
    let id: Int64
    let x: Int32
    let y: Int32
    let width: Int32
    let height: Int32
    let zOrder: Int32
    let exclusion: UInt32
}

private struct WindowMetadata {
    let zOrder: Int
    let alpha: Double
}

private let systemUiBundleIdentifiers: Set<String> = [
    "com.apple.controlcenter",
    "com.apple.dock",
    "com.apple.notificationcenterui",
    "com.apple.systemuiserver",
    "com.apple.WindowManager",
]

final class CapturedFrameHandle: @unchecked Sendable {
    let status: Int32
    let width: Int32
    let height: Int32
    let stride: Int32
    let logicalWidth: Double
    let logicalHeight: Double
    let cursorX: Double
    let cursorY: Double
    let displayOriginX: Int32
    let displayOriginY: Int32
    fileprivate let windowCandidates: [WindowCandidateInfo]
    let pixels: UnsafeMutableRawPointer?
    let pixelLength: Int
    let errorMessage: UnsafeMutablePointer<CChar>?

    init(errorStatus: Int32, message: String) {
        status = errorStatus
        width = 0
        height = 0
        stride = 0
        logicalWidth = 0
        logicalHeight = 0
        cursorX = 0
        cursorY = 0
        displayOriginX = 0
        displayOriginY = 0
        windowCandidates = []
        pixels = nil
        pixelLength = 0
        errorMessage = strdup(message)
    }

    init(
        image: CGImage,
        screen: NSScreen,
        displayFrame: CGRect? = nil,
        windows: [SCWindow] = []
    ) throws {
        let width = image.width
        let height = image.height
        let stride = try checkedProduct(width, 4)
        let pixelLength = try checkedProduct(stride, height)

        guard let pixels = calloc(pixelLength, 1) else {
            throw BridgeError.outOfMemory
        }

        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB) else {
            free(pixels)
            throw BridgeError.colorSpaceUnavailable
        }

        let bitmapInfo = CGBitmapInfo.byteOrder32Little.union(
            CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedFirst.rawValue)
        )
        guard let context = CGContext(
            data: pixels,
            width: width,
            height: height,
            bitsPerComponent: 8,
            bytesPerRow: stride,
            space: colorSpace,
            bitmapInfo: bitmapInfo.rawValue
        ) else {
            free(pixels)
            throw BridgeError.bitmapContextUnavailable
        }

        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

        let captureFrame = displayFrame ?? CGRect(
            x: 0,
            y: 0,
            width: screen.frame.width,
            height: screen.frame.height
        )
        let scaleX = Double(width) / captureFrame.width
        let scaleY = Double(height) / captureFrame.height
        let cursor = CGEvent(source: nil)?.location ?? captureFrame.origin
        let metadata = currentWindowMetadata()
        self.status = 0
        self.width = Int32(width)
        self.height = Int32(height)
        self.stride = Int32(stride)
        self.logicalWidth = screen.frame.width
        self.logicalHeight = screen.frame.height
        self.cursorX = (cursor.x - captureFrame.minX) * scaleX
        self.cursorY = (cursor.y - captureFrame.minY) * scaleY
        self.displayOriginX = Int32((captureFrame.minX * scaleX).rounded())
        self.displayOriginY = Int32((captureFrame.minY * scaleY).rounded())
        self.windowCandidates = windows.enumerated().compactMap { fallbackZOrder, window in
            let windowFrame = window.frame
            guard windowFrame.intersects(captureFrame) else {
                return nil
            }

            var exclusion: UInt32 = 0
            if !window.isOnScreen {
                exclusion |= 1 << 0
                exclusion |= 1 << 1
            }
            if window.windowLayer != 0 {
                exclusion |= 1 << 2
                exclusion |= 1 << 5
            }
            if window.owningApplication?.processID == getpid() {
                exclusion |= 1 << 3
            }
            if let bundleIdentifier = window.owningApplication?.bundleIdentifier,
               systemUiBundleIdentifiers.contains(bundleIdentifier) {
                exclusion |= 1 << 4
            }
            let windowMetadata = metadata[window.windowID]
            if windowMetadata?.alpha ?? 1 <= 0.01 {
                exclusion |= 1 << 6
            }

            return WindowCandidateInfo(
                id: Int64(window.windowID),
                x: Int32(((windowFrame.minX - captureFrame.minX) * scaleX).rounded()),
                y: Int32(((windowFrame.minY - captureFrame.minY) * scaleY).rounded()),
                width: Int32((windowFrame.width * scaleX).rounded()),
                height: Int32((windowFrame.height * scaleY).rounded()),
                zOrder: Int32(windowMetadata?.zOrder ?? fallbackZOrder),
                exclusion: exclusion
            )
        }
        self.pixels = pixels
        self.pixelLength = pixelLength
        self.errorMessage = nil
    }

    deinit {
        free(pixels)
        free(errorMessage)
    }
}

private func currentWindowMetadata() -> [CGWindowID: WindowMetadata] {
    guard let windowList = CGWindowListCopyWindowInfo(
        [.optionOnScreenOnly, .excludeDesktopElements],
        kCGNullWindowID
    ) as? [[String: Any]] else {
        return [:]
    }

    var result: [CGWindowID: WindowMetadata] = [:]
    for (zOrder, window) in windowList.enumerated() {
        guard let windowNumber = window[kCGWindowNumber as String] as? NSNumber else {
            continue
        }

        let alpha = (window[kCGWindowAlpha as String] as? NSNumber)?.doubleValue ?? 1
        result[CGWindowID(windowNumber.uint32Value)] = WindowMetadata(
            zOrder: zOrder,
            alpha: alpha
        )
    }

    return result
}

private final class CaptureBox: @unchecked Sendable {
    private let lock = NSLock()
    private var storedHandle: CapturedFrameHandle?

    func store(_ handle: CapturedFrameHandle) {
        lock.lock()
        storedHandle = handle
        lock.unlock()
    }

    func take() -> CapturedFrameHandle {
        lock.lock()
        defer { lock.unlock() }
        return storedHandle ?? CapturedFrameHandle(errorStatus: 2, message: "Capture did not return a result.")
    }
}

private enum BridgeError: LocalizedError {
    case arithmeticOverflow
    case bitmapContextUnavailable
    case colorSpaceUnavailable
    case displayUnavailable
    case outOfMemory
    case screenUnavailable

    var errorDescription: String? {
        switch self {
        case .arithmeticOverflow:
            return "The captured image is too large."
        case .bitmapContextUnavailable:
            return "The BGRA bitmap context could not be created."
        case .colorSpaceUnavailable:
            return "The sRGB color space is unavailable."
        case .displayUnavailable:
            return "The current display is unavailable to ScreenCaptureKit."
        case .outOfMemory:
            return "The captured image buffer could not be allocated."
        case .screenUnavailable:
            return "The display under the pointer could not be determined."
        }
    }
}

private func checkedProduct(_ first: Int, _ second: Int) throws -> Int {
    let result = first.multipliedReportingOverflow(by: second)
    if result.overflow {
        throw BridgeError.arithmeticOverflow
    }

    return result.partialValue
}

@_cdecl("snaploom_register_screenshot_hot_key")
public func registerScreenshotHotKey(
    _ keyCode: UInt32,
    _ modifiers: UInt32,
    _ callback: @escaping @convention(c) () -> Void
) -> Int32 {
    unregisterScreenshotHotKey()
    hotKeyCallback = callback

    var eventType = EventTypeSpec(
        eventClass: OSType(kEventClassKeyboard),
        eventKind: UInt32(kEventHotKeyPressed)
    )
    let handlerStatus = InstallEventHandler(
        GetApplicationEventTarget(),
        hotKeyEventHandler,
        1,
        &eventType,
        nil,
        &hotKeyHandlerReference
    )
    guard handlerStatus == noErr else {
        hotKeyCallback = nil
        return handlerStatus
    }

    let hotKeyID = EventHotKeyID(signature: 0x534E4150, id: 1)
    let registrationStatus = RegisterEventHotKey(
        keyCode,
        modifiers,
        hotKeyID,
        GetApplicationEventTarget(),
        0,
        &hotKeyReference
    )

    if registrationStatus != noErr {
        unregisterScreenshotHotKey()
    }

    return registrationStatus
}

@_cdecl("snaploom_unregister_screenshot_hot_key")
public func unregisterScreenshotHotKey() {
    if let hotKeyReference {
        UnregisterEventHotKey(hotKeyReference)
    }
    if let hotKeyHandlerReference {
        RemoveEventHandler(hotKeyHandlerReference)
    }

    hotKeyReference = nil
    hotKeyHandlerReference = nil
    hotKeyCallback = nil
}

@_cdecl("snaploom_set_auto_start_enabled")
public func setAutoStartEnabled(_ enabled: Int32) -> Int32 {
    if #available(macOS 13.0, *) {
        do {
            if enabled == 1 {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            return 1
        } catch {
            return 0
        }
    }

    return 0
}

@_cdecl("snaploom_is_auto_start_enabled")
public func isAutoStartEnabled() -> Int32 {
    if #available(macOS 13.0, *) {
        return SMAppService.mainApp.status == .enabled ? 1 : 0
    }

    return 0
}

@_cdecl("snaploom_start_resume_monitoring")
public func startResumeMonitoring(
    _ callback: @escaping @convention(c) () -> Void
) {
    stopResumeMonitoring()
    resumeCallback = callback
    wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
        forName: NSWorkspace.didWakeNotification,
        object: nil,
        queue: .main
    ) { _ in
        resumeCallback?()
    }
}

@_cdecl("snaploom_stop_resume_monitoring")
public func stopResumeMonitoring() {
    if let wakeObserver {
        NSWorkspace.shared.notificationCenter.removeObserver(wakeObserver)
    }

    wakeObserver = nil
    resumeCallback = nil
}

@_cdecl("snaploom_screen_capture_permission")
public func screenCapturePermission() -> Int32 {
    CGPreflightScreenCaptureAccess() ? 1 : 0
}

@_cdecl("snaploom_request_screen_capture_permission")
public func requestScreenCapturePermission() -> Int32 {
    CGRequestScreenCaptureAccess() ? 1 : 0
}

@_cdecl("snaploom_open_screen_capture_settings")
public func openScreenCaptureSettings() {
    guard let url = URL(
        string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture"
    ) else {
        return
    }

    DispatchQueue.main.async {
        NSWorkspace.shared.open(url)
    }
}

@_cdecl("snaploom_configure_capture_overlay")
public func configureCaptureOverlay(_ pointer: UnsafeMutableRawPointer) {
    let configureWindow = {
        let window = Unmanaged<NSWindow>.fromOpaque(pointer).takeUnretainedValue()
        window.level = NSWindow.Level(
            rawValue: Int(CGWindowLevelForKey(.screenSaverWindow))
        )
        window.collectionBehavior.formUnion([.canJoinAllSpaces, .fullScreenAuxiliary])
        window.hidesOnDeactivate = false

        let pointerLocation = NSEvent.mouseLocation
        if let screen = NSScreen.screens.first(where: { $0.frame.contains(pointerLocation) }) ??
            window.screen ??
            NSScreen.main {
            window.setFrame(screen.frame, display: true)
        }
    }

    if Thread.isMainThread {
        configureWindow()
    } else {
        DispatchQueue.main.sync(execute: configureWindow)
    }
}

@_cdecl("snaploom_copy_png_to_clipboard")
public func copyPngToClipboard(
    _ bytes: UnsafePointer<UInt8>?,
    _ length: Int
) -> Int32 {
    guard let bytes, length > 0 else {
        return 0
    }

    let data = Data(bytes: bytes, count: length)
    let write = {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        return pasteboard.setData(data, forType: .png) ? Int32(1) : Int32(0)
    }
    return Thread.isMainThread ? write() : DispatchQueue.main.sync(execute: write)
}

@_cdecl("snaploom_copy_text_to_clipboard")
public func copyTextToClipboard(_ text: UnsafePointer<CChar>?) -> Int32 {
    guard let text else {
        return 0
    }

    let value = String(cString: text)
    let write = {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        return pasteboard.setString(value, forType: .string) ? Int32(1) : Int32(0)
    }
    return Thread.isMainThread ? write() : DispatchQueue.main.sync(execute: write)
}

@available(macOS 14.0, *)
private func captureCurrentDisplay() async throws -> CapturedFrameHandle {
    guard let screen = NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) ?? NSScreen.main else {
        throw BridgeError.screenUnavailable
    }
    guard let displayNumber = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else {
        throw BridgeError.displayUnavailable
    }

    let shareableContent = try await SCShareableContent.excludingDesktopWindows(
        false,
        onScreenWindowsOnly: true
    )
    guard let display = shareableContent.displays.first(where: {
        $0.displayID == CGDirectDisplayID(displayNumber.uint32Value)
    }) else {
        throw BridgeError.displayUnavailable
    }

    let filter = SCContentFilter(display: display, excludingWindows: [])
    let configuration = SCStreamConfiguration()
    configuration.width = Int(screen.frame.width * screen.backingScaleFactor)
    configuration.height = Int(screen.frame.height * screen.backingScaleFactor)
    configuration.pixelFormat = kCVPixelFormatType_32BGRA
    configuration.showsCursor = false
    configuration.colorSpaceName = CGColorSpace.sRGB

    let image = try await SCScreenshotManager.captureImage(
        contentFilter: filter,
        configuration: configuration
    )
    return try CapturedFrameHandle(
        image: image,
        screen: screen,
        displayFrame: display.frame,
        windows: shareableContent.windows
    )
}

@_cdecl("snaploom_capture_current_display")
public func captureCurrentDisplayBlocking() -> UnsafeMutableRawPointer {
    guard CGPreflightScreenCaptureAccess() else {
        return Unmanaged.passRetained(
            CapturedFrameHandle(errorStatus: 1, message: "Screen Recording permission is required.")
        ).toOpaque()
    }

    let box = CaptureBox()
    let semaphore = DispatchSemaphore(value: 0)
    Task.detached {
        do {
            box.store(try await captureCurrentDisplay())
        } catch {
            box.store(
                CapturedFrameHandle(
                    errorStatus: 2,
                    message: error.localizedDescription
                )
            )
        }
        semaphore.signal()
    }
    semaphore.wait()
    return Unmanaged.passRetained(box.take()).toOpaque()
}

private func frame(from pointer: UnsafeMutableRawPointer) -> CapturedFrameHandle {
    Unmanaged<CapturedFrameHandle>.fromOpaque(pointer).takeUnretainedValue()
}

@_cdecl("snaploom_frame_status")
public func frameStatus(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    frame(from: pointer).status
}

@_cdecl("snaploom_frame_width")
public func frameWidth(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    frame(from: pointer).width
}

@_cdecl("snaploom_frame_height")
public func frameHeight(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    frame(from: pointer).height
}

@_cdecl("snaploom_frame_stride")
public func frameStride(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    frame(from: pointer).stride
}

@_cdecl("snaploom_frame_logical_width")
public func frameLogicalWidth(_ pointer: UnsafeMutableRawPointer) -> Double {
    frame(from: pointer).logicalWidth
}

@_cdecl("snaploom_frame_logical_height")
public func frameLogicalHeight(_ pointer: UnsafeMutableRawPointer) -> Double {
    frame(from: pointer).logicalHeight
}

@_cdecl("snaploom_frame_cursor_x")
public func frameCursorX(_ pointer: UnsafeMutableRawPointer) -> Double {
    frame(from: pointer).cursorX
}

@_cdecl("snaploom_frame_cursor_y")
public func frameCursorY(_ pointer: UnsafeMutableRawPointer) -> Double {
    frame(from: pointer).cursorY
}

@_cdecl("snaploom_frame_display_origin_x")
public func frameDisplayOriginX(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    frame(from: pointer).displayOriginX
}

@_cdecl("snaploom_frame_display_origin_y")
public func frameDisplayOriginY(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    frame(from: pointer).displayOriginY
}

@_cdecl("snaploom_frame_window_count")
public func frameWindowCount(_ pointer: UnsafeMutableRawPointer) -> Int32 {
    Int32(frame(from: pointer).windowCandidates.count)
}

private func windowCandidate(
    from pointer: UnsafeMutableRawPointer,
    at index: Int32
) -> WindowCandidateInfo? {
    let candidates = frame(from: pointer).windowCandidates
    guard index >= 0, Int(index) < candidates.count else {
        return nil
    }

    return candidates[Int(index)]
}

@_cdecl("snaploom_frame_window_id")
public func frameWindowId(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> Int64 {
    windowCandidate(from: pointer, at: index)?.id ?? 0
}

@_cdecl("snaploom_frame_window_x")
public func frameWindowX(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> Int32 {
    windowCandidate(from: pointer, at: index)?.x ?? 0
}

@_cdecl("snaploom_frame_window_y")
public func frameWindowY(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> Int32 {
    windowCandidate(from: pointer, at: index)?.y ?? 0
}

@_cdecl("snaploom_frame_window_width")
public func frameWindowWidth(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> Int32 {
    windowCandidate(from: pointer, at: index)?.width ?? 0
}

@_cdecl("snaploom_frame_window_height")
public func frameWindowHeight(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> Int32 {
    windowCandidate(from: pointer, at: index)?.height ?? 0
}

@_cdecl("snaploom_frame_window_z_order")
public func frameWindowZOrder(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> Int32 {
    windowCandidate(from: pointer, at: index)?.zOrder ?? 0
}

@_cdecl("snaploom_frame_window_exclusion")
public func frameWindowExclusion(_ pointer: UnsafeMutableRawPointer, _ index: Int32) -> UInt32 {
    windowCandidate(from: pointer, at: index)?.exclusion ?? 0
}

@_cdecl("snaploom_frame_pixel_data")
public func framePixelData(_ pointer: UnsafeMutableRawPointer) -> UnsafeMutableRawPointer? {
    frame(from: pointer).pixels
}

@_cdecl("snaploom_frame_pixel_length")
public func framePixelLength(_ pointer: UnsafeMutableRawPointer) -> Int {
    frame(from: pointer).pixelLength
}

@_cdecl("snaploom_frame_error_message")
public func frameErrorMessage(_ pointer: UnsafeMutableRawPointer) -> UnsafePointer<CChar>? {
    guard let message = frame(from: pointer).errorMessage else {
        return nil
    }

    return UnsafePointer(message)
}

@_cdecl("snaploom_release_frame")
public func releaseFrame(_ pointer: UnsafeMutableRawPointer) {
    Unmanaged<CapturedFrameHandle>.fromOpaque(pointer).release()
}

@_cdecl("snaploom_show_png_save_panel")
public func showPngSavePanel(
    _ suggestedName: UnsafePointer<CChar>,
    _ initialDirectory: UnsafePointer<CChar>?
) -> UnsafeMutablePointer<CChar>? {
    let name = String(cString: suggestedName)
    var selectedPath: UnsafeMutablePointer<CChar>?

    let showPanel = {
        let panel = NSSavePanel()
        panel.title = "保存截图"
        panel.nameFieldStringValue = name
        panel.allowedContentTypes = [.png]
        panel.allowsOtherFileTypes = false
        panel.canCreateDirectories = true
        if let initialDirectory {
            let directory = String(cString: initialDirectory)
            panel.directoryURL = URL(fileURLWithPath: directory, isDirectory: true)
        }

        if panel.runModal() == .OK, let url = panel.url {
            selectedPath = strdup(url.path)
        }
    }

    if Thread.isMainThread {
        showPanel()
    } else {
        DispatchQueue.main.sync(execute: showPanel)
    }

    return selectedPath
}

@_cdecl("snaploom_release_string")
public func releaseString(_ pointer: UnsafeMutablePointer<CChar>?) {
    free(pointer)
}
