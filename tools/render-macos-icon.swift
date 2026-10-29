// Renders the macOS app icon images (T-098) from their SVG sources with the system SVG renderer of macOS (CoreSVG
// through NSImage); no other dependency. Needs macOS and the Xcode Command Line Tools (swift).
//
//   src/LayerPeek.App/Assets/AppIcon/app-icon-macos.svg     -> app-icon-macos-1024.png (master of the .icns, 64-1024 px)
//   src/LayerPeek.App/Assets/AppIcon/app-icon-macos-32.svg  -> app-icon-macos-32.png   (exact 32 px image)
//   src/LayerPeek.App/Assets/AppIcon/app-icon-macos-16.svg  -> app-icon-macos-16.png   (exact 16 px image)
//
// Usage (from any folder):
//   swift tools/render-macos-icon.swift              overwrites the PNGs next to the SVGs
//   swift tools/render-macos-icon.swift --out <dir>  writes the PNGs into <dir> instead (e.g. to compare them)
//
// Output is deterministic: an 8-bit sRGB bitmap with alpha, no colour conversion, PNG without metadata, so an unchanged
// SVG gives byte-identical PNGs (the Pester tests of scripts/ReleaseTools check it). The Windows icon (app-icon.ico) is
// not made here.

import AppKit
import ImageIO
import UniformTypeIdentifiers

let images: [(svg: String, png: String, pixels: Int)] = [
    ("app-icon-macos.svg", "app-icon-macos-1024.png", 1024),
    ("app-icon-macos-32.svg", "app-icon-macos-32.png", 32),
    ("app-icon-macos-16.svg", "app-icon-macos-16.png", 16),
]

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("render-macos-icon: \(message)\n".utf8))
    exit(1)
}

// The asset folder from the location of this script (tools/ next to src/).
let script = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
let assets = script.deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("src/LayerPeek.App/Assets/AppIcon", isDirectory: true)

var output = assets
var arguments = CommandLine.arguments.dropFirst()
while let argument = arguments.popFirst() {
    switch argument {
    case "--out":
        guard let folder = arguments.popFirst() else { fail("--out needs a folder.") }
        output = URL(fileURLWithPath: folder, isDirectory: true)
    default:
        fail("unknown argument '\(argument)'. Usage: swift tools/render-macos-icon.swift [--out <folder>]")
    }
}

guard let sRGB = CGColorSpace(name: CGColorSpace.sRGB) else { fail("no sRGB colour space.") }

for image in images {
    let source = assets.appendingPathComponent(image.svg)
    guard let svg = NSImage(contentsOf: source) else { fail("cannot read \(source.path).") }
    let size = image.pixels
    guard let context = CGContext(
        data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: size * 4, space: sRGB,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    else { fail("cannot create a \(size) px bitmap.") }
    context.clear(CGRect(x: 0, y: 0, width: size, height: size))

    // The SVG is drawn as a vector into the whole bitmap (its viewBox is the canvas).
    NSGraphicsContext.saveGraphicsState()
    let graphics = NSGraphicsContext(cgContext: context, flipped: false)
    graphics.imageInterpolation = .high
    NSGraphicsContext.current = graphics
    svg.draw(in: NSRect(x: 0, y: 0, width: size, height: size), from: .zero, operation: .sourceOver, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()

    guard let bitmap = context.makeImage() else { fail("cannot render \(image.svg).") }
    let target = output.appendingPathComponent(image.png)
    guard let destination = CGImageDestinationCreateWithURL(target as CFURL, UTType.png.identifier as CFString, 1, nil)
    else { fail("cannot write \(target.path).") }
    CGImageDestinationAddImage(destination, bitmap, nil)
    guard CGImageDestinationFinalize(destination) else { fail("cannot write \(target.path).") }
    print("\(image.svg) -> \(target.path) (\(size) x \(size) px)")
}
