import AppKit
import Carbon
import CoreGraphics
import CoreVideo
import ScreenCaptureKit
import UniformTypeIdentifiers

private typealias HotKeyCallback = @convention(c) () -> Void

private var hotKeyCallback: HotKeyCallback?
private var hotKeyReference: EventHotKeyRef?
private var hotKeyHandlerReference: EventHandlerRef?

private let hotKeyEventHandler: EventHandlerUPP = { _, _, _ in
    hotKeyCallback?()
    return noErr
}

private final class CapturedFrameHandle: @unchecked Sendable {
    let status: Int32
    let width: Int32
    let height: Int32
    let stride: Int32
    let logicalWidth: Double
    let logicalHeight: Double
    let cursorX: Double
    let cursorY: Double
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
        pixels = nil
        pixelLength = 0
        errorMessage = strdup(message)
    }

    init(image: CGImage, screen: NSScreen) throws {
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

        context.translateBy(x: 0, y: CGFloat(height))
        context.scaleBy(x: 1, y: -1)
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

        let cursor = CGEvent(source: nil)?.location ?? .zero
        self.status = 0
        self.width = Int32(width)
        self.height = Int32(height)
        self.stride = Int32(stride)
        self.logicalWidth = screen.frame.width
        self.logicalHeight = screen.frame.height
        self.cursorX = cursor.x
        self.cursorY = cursor.y
        self.pixels = pixels
        self.pixelLength = pixelLength
        self.errorMessage = nil
    }

    deinit {
        free(pixels)
        free(errorMessage)
    }
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
    return try CapturedFrameHandle(image: image, screen: screen)
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
public func showPngSavePanel(_ suggestedName: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>? {
    let name = String(cString: suggestedName)
    var selectedPath: UnsafeMutablePointer<CChar>?

    let showPanel = {
        let panel = NSSavePanel()
        panel.title = "保存截图"
        panel.nameFieldStringValue = name
        panel.allowedContentTypes = [.png]
        panel.allowsOtherFileTypes = false
        panel.canCreateDirectories = true

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
