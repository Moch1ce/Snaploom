import AppKit
import CoreGraphics
import Foundation

@main
struct FrameOrientationTest {
    static func main() throws {
        let image = try makeVerticalOrientationTestImage()
        guard let screen = NSScreen.main else {
            throw TestFailure("无法获取测试显示器")
        }
        let frame = try CapturedFrameHandle(image: image, screen: screen)

        guard frame.width == 1, frame.height == 2 else {
            throw TestFailure("转换后的尺寸不正确")
        }
        guard let pixels = frame.pixels else {
            throw TestFailure("转换后没有像素数据")
        }

        let bytes = pixels.bindMemory(to: UInt8.self, capacity: 8)
        let firstRow = Array(UnsafeBufferPointer(start: bytes, count: 4))
        let secondRow = Array(UnsafeBufferPointer(start: bytes + 4, count: 4))

        guard firstRow == [0, 0, 255, 255] else {
            throw TestFailure("第一行应为红色 BGRA，实际为 \(firstRow)")
        }
        guard secondRow == [255, 0, 0, 255] else {
            throw TestFailure("第二行应为蓝色 BGRA，实际为 \(secondRow)")
        }
    }

    private static func makeVerticalOrientationTestImage() throws -> CGImage {
        let sourcePixels: [UInt8] = [
            0, 0, 255, 255,
            255, 0, 0, 255,
        ]
        let sourceData = Data(sourcePixels) as CFData
        guard
            let provider = CGDataProvider(data: sourceData),
            let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
            let image = CGImage(
                width: 1,
                height: 2,
                bitsPerComponent: 8,
                bitsPerPixel: 32,
                bytesPerRow: 4,
                space: colorSpace,
                bitmapInfo: CGBitmapInfo.byteOrder32Little.union(
                    CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedFirst.rawValue)
                ),
                provider: provider,
                decode: nil,
                shouldInterpolate: false,
                intent: .defaultIntent
            )
        else {
            throw TestFailure("无法创建测试图像")
        }

        return image
    }
}

private struct TestFailure: Error, CustomStringConvertible {
    let description: String

    init(_ description: String) {
        self.description = description
    }
}
