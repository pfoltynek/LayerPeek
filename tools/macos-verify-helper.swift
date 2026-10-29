// Small macOS helper of tools/macos-verify.ps1 (T-105): reads what the verification checks need from the system APIs
// that have no command line tool, so that the app itself needs no extra code. Compiled by the script with swiftc (Xcode
// Command Line Tools); no other dependency. Prints JSON (or plain values) on stdout, errors on stderr with exit code 1.
//
//   screens                          the displays: id, UUID, name, frame and usable area (points, origin top left of
//                                    the main display, as CGWindowList), scale, main
//   windows <pid>                    the windows of a process from CGWindowListCopyWindowInfo: number, layer, bounds,
//                                    on screen; needs no Screen Recording permission (window titles are not read)
//   menubar                          the menu bar level windows (layers 23 to 26) of every process with owner name and
//                                    pid: shows who owns the status items on this macOS version (evidence only)
//   session                          the login session as JSON: locked (the screen is locked: no window can come to the
//                                    front, status items and full-screen Spaces are not shown) and onConsole
//   trusted                          "true" / "false": whether this process (i.e. the terminal that started it) has the
//                                    Accessibility permission; never asks for it
//   key <combo>                      posts a key press, e.g. ctrl+cmd+f, cmd+w, cmd+period, cmd+shift+], escape
//                                    (needs Accessibility; exit code 3 without it)
//   click <x> <y>                    a left click at a point (global, top left origin; needs Accessibility)
//   localized <bundle> <key> <lang>...  for each language the localization macOS picks for a user with that language
//                                    (Bundle.preferredLocalizations) and the value of <key> in its InfoPlist.strings
//   icons <out.png> <menu bar icon dir> <app icon or bundle> <system app>...
//                                    a contact sheet PNG: the menu bar template icons on a light and a dark menu bar at
//                                    1x and 2x, and the app icon next to the system app icons at 16 to 128 points

import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

func fail(_ message: String, code: Int32 = 1) -> Never {
    FileHandle.standardError.write(Data("macos-verify-helper: \(message)\n".utf8))
    exit(code)
}

func printJSON(_ value: Any) {
    guard let data = try? JSONSerialization.data(withJSONObject: value, options: [.prettyPrinted, .sortedKeys]),
          let text = String(data: data, encoding: .utf8) else { fail("could not encode JSON") }
    print(text)
}

/// The height of the main display: converts AppKit's bottom-left origin to the top-left origin of CGWindowList.
func mainHeight() -> CGFloat { CGDisplayBounds(CGMainDisplayID()).height }

func topLeft(_ rect: NSRect) -> [String: Double] {
    ["x": Double(rect.origin.x), "y": Double(mainHeight() - rect.origin.y - rect.height), "width": Double(rect.width), "height": Double(rect.height)]
}

func screens() {
    var result: [[String: Any]] = []
    for screen in NSScreen.screens {
        let number = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
        var uuid = ""
        if let cf = CGDisplayCreateUUIDFromDisplayID(number)?.takeRetainedValue(), let text = CFUUIDCreateString(nil, cf) {
            uuid = (text as String).uppercased()
        }
        result.append([
            "id": Int(number),
            "uuid": uuid,
            "name": screen.localizedName,
            "frame": topLeft(screen.frame),
            "visible": topLeft(screen.visibleFrame),
            "scale": Double(screen.backingScaleFactor),
            "main": number == CGMainDisplayID(),
        ])
    }
    printJSON(result)
}

func windows(_ pid: Int32) {
    guard let list = CGWindowListCopyWindowInfo([.optionAll, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { fail("no window list") }
    var result: [[String: Any]] = []
    for window in list {
        guard (window[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == pid else { continue }
        let bounds = window[kCGWindowBounds as String] as? [String: Any] ?? [:]
        func number(_ key: String) -> Double { (bounds[key] as? NSNumber)?.doubleValue ?? 0 }
        result.append([
            "number": (window[kCGWindowNumber as String] as? NSNumber)?.intValue ?? 0,
            "layer": (window[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0,
            "x": number("X"), "y": number("Y"), "width": number("Width"), "height": number("Height"),
            "onscreen": (window[kCGWindowIsOnscreen as String] as? NSNumber)?.boolValue ?? false,
            "alpha": (window[kCGWindowAlpha as String] as? NSNumber)?.doubleValue ?? 1,
        ])
    }
    printJSON(result)
}

func menuBarWindows() {
    guard let list = CGWindowListCopyWindowInfo([.optionAll], kCGNullWindowID) as? [[String: Any]] else { fail("no window list") }
    var result: [[String: Any]] = []
    for window in list {
        let layer = (window[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0
        guard (23...26).contains(layer) else { continue }
        let bounds = window[kCGWindowBounds as String] as? [String: Any] ?? [:]
        func number(_ key: String) -> Double { (bounds[key] as? NSNumber)?.doubleValue ?? 0 }
        result.append([
            "layer": layer,
            "owner": window[kCGWindowOwnerName as String] as? String ?? "",
            "pid": (window[kCGWindowOwnerPID as String] as? NSNumber)?.intValue ?? 0,
            "x": number("X"), "y": number("Y"), "width": number("Width"), "height": number("Height"),
            "onscreen": (window[kCGWindowIsOnscreen as String] as? NSNumber)?.boolValue ?? false,
        ])
    }
    printJSON(result)
}

func session() {
    let info = CGSessionCopyCurrentDictionary() as? [String: Any] ?? [:]
    printJSON([
        "locked": (info["CGSSessionScreenIsLocked"] as? NSNumber)?.boolValue ?? false,
        "onConsole": (info[kCGSessionOnConsoleKey as String] as? NSNumber)?.boolValue ?? false,
        "known": !info.isEmpty,
    ])
}

func requireTrusted() {
    if !AXIsProcessTrusted() { fail("no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility)", code: 3) }
}

let keyCodes: [String: CGKeyCode] = [
    "a": 0, "s": 1, "d": 2, "f": 3, "h": 4, "g": 5, "z": 6, "x": 7, "c": 8, "v": 9, "b": 11, "q": 12, "w": 13, "e": 14,
    "r": 15, "y": 16, "t": 17, "o": 31, "u": 32, "i": 34, "p": 35, "l": 37, "j": 38, "k": 40, "n": 45, "m": 46,
    "]": 30, "[": 33, "period": 47, ".": 47, "comma": 43, "tab": 48, "space": 49, "escape": 53, "esc": 53, "return": 36,
    "left": 123, "right": 124, "down": 125, "up": 126, "f11": 103,
]

func key(_ combo: String) {
    requireTrusted()
    var flags: CGEventFlags = []
    var code: CGKeyCode?
    for part in combo.lowercased().split(separator: "+").map(String.init) {
        switch part {
        case "cmd", "command": flags.insert(.maskCommand)
        case "ctrl", "control": flags.insert(.maskControl)
        case "alt", "opt", "option": flags.insert(.maskAlternate)
        case "shift": flags.insert(.maskShift)
        case "fn": flags.insert(.maskSecondaryFn)
        default:
            guard let found = keyCodes[part] else { fail("unknown key '\(part)'") }
            code = found
        }
    }
    guard let keyCode = code else { fail("no key in '\(combo)'") }
    let source = CGEventSource(stateID: .hidSystemState)
    for down in [true, false] {
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: down) else { fail("no key event") }
        event.flags = flags
        event.post(tap: .cghidEventTap)
        usleep(30_000)
    }
}

func click(_ x: Double, _ y: Double) {
    requireTrusted()
    let point = CGPoint(x: x, y: y)
    let source = CGEventSource(stateID: .hidSystemState)
    for type in [CGEventType.mouseMoved, .leftMouseDown, .leftMouseUp] {
        guard let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: .left) else { fail("no mouse event") }
        event.post(tap: .cghidEventTap)
        usleep(80_000)
    }
}

func localized(_ path: String, _ key: String, _ languages: [String]) {
    guard let bundle = Bundle(path: path) else { fail("not a bundle: \(path)") }
    var result: [String: Any] = [:]
    for language in languages {
        let chosen = Bundle.preferredLocalizations(from: bundle.localizations, forPreferences: [language]).first ?? ""
        var value: Any = NSNull()
        if let file = bundle.path(forResource: "InfoPlist", ofType: "strings", inDirectory: nil, forLocalization: chosen),
           let table = NSDictionary(contentsOfFile: file), let text = table[key] as? String {
            value = text
        }
        result[language] = ["localization": chosen, "value": value]
    }
    printJSON(result)
}

func icons(_ output: String, _ menuBarFolder: String, _ appIcon: String, _ systemApps: [String]) {
    let files = ((try? FileManager.default.contentsOfDirectory(atPath: menuBarFolder)) ?? []).filter { $0.hasSuffix(".png") }.sorted()
    let appImages: [(String, NSImage)] = ([appIcon] + systemApps).map { path in
        let name = (path as NSString).lastPathComponent
        if path.hasSuffix(".app") { return (name, NSWorkspace.shared.icon(forFile: path)) }
        return (name, NSImage(contentsOfFile: path) ?? NSImage())
    }
    let cell = 44.0, labelHeight = 16.0, margin = 12.0
    let iconSizes: [Double] = [128, 64, 32, 16]
    let menuWidth = margin * 2 + Double(max(files.count, 1)) * cell
    let appWidth = margin * 2 + Double(appImages.count) * 150
    let width = max(menuWidth, appWidth)
    let height = margin + 4 * (cell + labelHeight) + 2 * (iconSizes.reduce(0, +) + 4 * 8 + labelHeight) + margin
    guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(width * 2), pixelsHigh: Int(height * 2), bitsPerSample: 8,
                                        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { fail("no bitmap") }
    bitmap.size = NSSize(width: width, height: height)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    NSColor.white.setFill()
    NSRect(x: 0, y: 0, width: width, height: height).fill()
    var y = height - margin
    func label(_ text: String, _ x: Double, _ top: Double, _ color: NSColor = .black) {
        (text as NSString).draw(at: NSPoint(x: x, y: top - labelHeight + 2), withAttributes: [.font: NSFont.systemFont(ofSize: 10), .foregroundColor: color])
    }
    // The menu bar: a light and a dark bar, the template image tinted black / white (as AppKit does), at 2x (Retina) and 1x.
    for (title, bar, tint) in [("light menu bar", NSColor(white: 0.93, alpha: 1), NSColor.black), ("dark menu bar", NSColor(white: 0.16, alpha: 1), NSColor.white)] {
        for (scaleName, pointSize) in [("Retina", 18.0), ("@1x (blurred = scaled from 2x)", 18.0)] {
            label("\(title), \(scaleName)", margin, y)
            y -= labelHeight
            bar.setFill()
            NSRect(x: margin, y: y - cell, width: width - 2 * margin, height: cell).fill()
            for (index, file) in files.enumerated() {
                guard let image = NSImage(contentsOfFile: (menuBarFolder as NSString).appendingPathComponent(file)) else { continue }
                let tinted = NSImage(size: NSSize(width: pointSize, height: pointSize), flipped: false) { rect in
                    image.draw(in: rect)
                    tint.set()
                    rect.fill(using: .sourceAtop)
                    return true
                }
                let x = margin + Double(index) * cell + (cell - pointSize) / 2
                let target = NSRect(x: x, y: y - cell + (cell - pointSize) / 2, width: pointSize, height: pointSize)
                if scaleName.hasPrefix("@1x") {
                    // Rendered into an 18 x 18 pixel bitmap first, then drawn at the point size: what a non-Retina display shows.
                    guard let small = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 18, pixelsHigh: 18, bitsPerSample: 8, samplesPerPixel: 4,
                                                       hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { continue }
                    NSGraphicsContext.saveGraphicsState()
                    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: small)
                    tinted.draw(in: NSRect(x: 0, y: 0, width: 18, height: 18))
                    NSGraphicsContext.restoreGraphicsState()
                    let pixelImage = NSImage(size: NSSize(width: 18, height: 18))
                    pixelImage.addRepresentation(small)
                    NSGraphicsContext.current?.imageInterpolation = .none
                    pixelImage.draw(in: target)
                    NSGraphicsContext.current?.imageInterpolation = .default
                } else {
                    tinted.draw(in: target)
                }
            }
            y -= cell
        }
    }
    // The app icon next to the system apps, on a light and a dark background.
    for (background, text) in [(NSColor.white, NSColor.black), (NSColor(white: 0.12, alpha: 1), NSColor.white)] {
        let block = iconSizes.reduce(0, +) + 4 * 8 + labelHeight
        background.setFill()
        NSRect(x: 0, y: y - block, width: width, height: block).fill()
        for (index, (name, image)) in appImages.enumerated() {
            let x = margin + Double(index) * 150
            label(name, x, y, text)
            var top = y - labelHeight
            for size in iconSizes {
                image.draw(in: NSRect(x: x, y: top - size, width: size, height: size))
                top -= size + 8
            }
        }
        y -= block
    }
    NSGraphicsContext.restoreGraphicsState()
    guard let png = bitmap.representation(using: .png, properties: [:]) else { fail("no PNG") }
    do { try png.write(to: URL(fileURLWithPath: output)) } catch { fail("cannot write \(output): \(error)") }
    print(output)
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard let command = arguments.first else { fail("usage: macos-verify-helper screens|windows|menubar|session|trusted|key|click|localized|icons ...") }
switch command {
case "screens": screens()
case "windows" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; windows(pid)
case "menubar": menuBarWindows()
case "session": session()
case "trusted": print(AXIsProcessTrusted() ? "true" : "false")
case "key" where arguments.count == 2: key(arguments[1])
case "click" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; click(x, y)
case "localized" where arguments.count >= 4: localized(arguments[1], arguments[2], Array(arguments[3...]))
case "icons" where arguments.count >= 4: icons(arguments[1], arguments[2], arguments[3], Array(arguments[4...]))
default: fail("unknown command or arguments: \(arguments.joined(separator: " "))")
}
