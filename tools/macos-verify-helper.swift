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
//   statusitems <pid>                the status items of a process from the Accessibility API (AXExtrasMenuBar): frame
//                                    (points, top left), title, description; macOS 27 draws them in no window of the
//                                    process, so CGWindowList cannot see them (needs Accessibility; exit code 3 without it)
//   trusted                          "true" / "false": whether this process (i.e. the terminal that started it) has the
//                                    Accessibility permission; never asks for it
//   key <combo>                      posts a key press, e.g. ctrl+cmd+f, cmd+w, cmd+period, cmd+shift+], escape
//                                    (needs Accessibility; exit code 3 without it)
//   click <x> <y>                    a left click at a point (global, top left origin; needs Accessibility)
//   rightclick <x> <y>               a right click at a point (needs Accessibility)
//   ctrlclick <x> <y>                a left click with Control held (needs Accessibility)
//   move <x> <y>                     moves the mouse pointer to a point (needs Accessibility)
//   ax <pid>                         the Accessibility tree of the windows of a process as JSON: role, subrole,
//                                    identifier, title, description, value, focused, frame (points, top left), actions,
//                                    children (needs Accessibility)
//   axpress <pid> <identifier>       performs AXPress on the first element with that identifier (needs Accessibility)
//   focusedid <pid>                  the identifier of the focused element of the process, or "" (needs Accessibility)
//   screenrecording                  "true" / "false": whether this process may capture other apps' windows (Screen
//                                    Recording); never asks for it
//   ring <png> <offset> <thickness>  JSON: the mean luminance (0-255) of the band <offset> to <offset + thickness> pixels
//                                    inside each edge of the image (top, right, bottom, left) and of the 2 x 2 pixels in
//                                    each corner (for an outline drawn at the edge of a window capture)
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

func statusItems(_ pid: Int32) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    var bar: CFTypeRef?
    // No extras menu bar (yet): the process has no status item.
    guard AXUIElementCopyAttributeValue(app, "AXExtrasMenuBar" as CFString, &bar) == .success, let bar else { printJSON([]); return }
    var children: CFTypeRef?
    _ = AXUIElementCopyAttributeValue(bar as! AXUIElement, kAXChildrenAttribute as CFString, &children)
    var result: [[String: Any]] = []
    for item in children as? [AXUIElement] ?? [] {
        func text(_ attribute: String) -> String {
            var value: CFTypeRef?
            return AXUIElementCopyAttributeValue(item, attribute as CFString, &value) == .success ? (value as? String ?? "") : ""
        }
        var point = CGPoint.zero
        var size = CGSize.zero
        var value: CFTypeRef?
        if AXUIElementCopyAttributeValue(item, kAXPositionAttribute as CFString, &value) == .success, let value { AXValueGetValue(value as! AXValue, .cgPoint, &point) }
        if AXUIElementCopyAttributeValue(item, kAXSizeAttribute as CFString, &value) == .success, let value { AXValueGetValue(value as! AXValue, .cgSize, &size) }
        result.append([
            "x": Double(point.x), "y": Double(point.y), "width": Double(size.width), "height": Double(size.height),
            "title": text(kAXTitleAttribute), "description": text(kAXDescriptionAttribute),
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

func mouse(_ x: Double, _ y: Double, _ types: [CGEventType], _ button: CGMouseButton, _ flags: CGEventFlags = []) {
    requireTrusted()
    let point = CGPoint(x: x, y: y)
    let source = CGEventSource(stateID: .hidSystemState)
    for type in types {
        guard let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: button) else { fail("no mouse event") }
        if type != .mouseMoved { event.flags = flags }
        event.post(tap: .cghidEventTap)
        usleep(80_000)
    }
}

/// A left click with the Control key really held (key down before, key up after), as a person does it: AppKit and apps
/// may read the current modifier state rather than the flags of the click.
func controlClick(_ x: Double, _ y: Double) {
    requireTrusted()
    let source = CGEventSource(stateID: .hidSystemState)
    let control: CGKeyCode = 59
    guard let down = CGEvent(keyboardEventSource: source, virtualKey: control, keyDown: true) else { fail("no key event") }
    down.flags = .maskControl
    down.post(tap: .cghidEventTap)
    usleep(80_000)
    mouse(x, y, [.mouseMoved, .leftMouseDown, .leftMouseUp], .left, .maskControl)
    guard let up = CGEvent(keyboardEventSource: source, virtualKey: control, keyDown: false) else { fail("no key event") }
    up.flags = []
    up.post(tap: .cghidEventTap)
    usleep(80_000)
}

func attribute(_ element: AXUIElement, _ name: String) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success ? value : nil
}

func text(_ element: AXUIElement, _ name: String) -> String {
    guard let value = attribute(element, name) else { return "" }
    if let string = value as? String { return string }
    if let number = value as? NSNumber { return number.stringValue }
    return ""
}

func frame(_ element: AXUIElement) -> [String: Double] {
    var point = CGPoint.zero
    var size = CGSize.zero
    if let value = attribute(element, kAXPositionAttribute) { AXValueGetValue(value as! AXValue, .cgPoint, &point) }
    if let value = attribute(element, kAXSizeAttribute) { AXValueGetValue(value as! AXValue, .cgSize, &size) }
    return ["x": Double(point.x), "y": Double(point.y), "width": Double(size.width), "height": Double(size.height)]
}

func children(_ element: AXUIElement) -> [AXUIElement] { attribute(element, kAXChildrenAttribute) as? [AXUIElement] ?? [] }

func node(_ element: AXUIElement, _ depth: Int) -> [String: Any] {
    var actions: CFArray?
    _ = AXUIElementCopyActionNames(element, &actions)
    var result: [String: Any] = [
        "role": text(element, kAXRoleAttribute), "subrole": text(element, kAXSubroleAttribute),
        "identifier": text(element, kAXIdentifierAttribute), "title": text(element, kAXTitleAttribute),
        "description": text(element, kAXDescriptionAttribute), "value": text(element, kAXValueAttribute),
        "focused": (attribute(element, kAXFocusedAttribute) as? NSNumber)?.boolValue ?? false,
        "frame": frame(element), "actions": actions as? [String] ?? [],
    ]
    if depth < 40 { result["children"] = children(element).map { node($0, depth + 1) } }
    return result
}

func axTree(_ pid: Int32) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    printJSON((attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []).map { node($0, 0) })
}

func find(_ element: AXUIElement, _ identifier: String, _ depth: Int = 0) -> AXUIElement? {
    if text(element, kAXIdentifierAttribute) == identifier { return element }
    guard depth < 40 else { return nil }
    for child in children(element) { if let found = find(child, identifier, depth + 1) { return found } }
    return nil
}

func axPress(_ pid: Int32, _ identifier: String) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    for window in attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? [] {
        if let element = find(window, identifier) {
            let status = AXUIElementPerformAction(element, kAXPressAction as CFString)
            if status != .success { fail("AXPress on \(identifier) failed: \(status.rawValue)") }
            return
        }
    }
    fail("no element \(identifier)")
}

func focusedIdentifier(_ pid: Int32) {
    requireTrusted()
    guard let focused = attribute(AXUIElementCreateApplication(pid), kAXFocusedUIElementAttribute) else { print(""); return }
    print(text(focused as! AXUIElement, kAXIdentifierAttribute))
}

func ring(_ path: String, _ offset: Int, _ thickness: Int) {
    guard let image = NSImage(contentsOfFile: path), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { fail("not an image: \(path)") }
    let width = cg.width, height = cg.height
    guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { fail("no bitmap") }
    context.draw(cg, in: CGRect(x: 0, y: 0, width: width, height: height))
    guard let pixels = context.data?.assumingMemoryBound(to: UInt8.self) else { fail("no pixels") }
    // Row 0 of the bitmap is the top of the image (CGContext memory order).
    func luminance(_ x: Int, _ y: Int) -> Double {
        let i = (y * width + x) * 4
        return 0.2126 * Double(pixels[i]) + 0.7152 * Double(pixels[i + 1]) + 0.0722 * Double(pixels[i + 2])
    }
    func mean(_ xs: Range<Int>, _ ys: Range<Int>) -> Double {
        var sum = 0.0
        for y in ys { for x in xs { sum += luminance(x, y) } }
        return sum / Double(max(1, xs.count * ys.count))
    }
    let band = offset..<(offset + thickness)
    let inner = thickness + offset
    printJSON([
        "width": width, "height": height,
        "top": mean(inner..<(width - inner), band), "bottom": mean(inner..<(width - inner), (height - offset - thickness)..<(height - offset)),
        "left": mean(band, inner..<(height - inner)), "right": mean((width - offset - thickness)..<(width - offset), inner..<(height - inner)),
        "corners": [mean(0..<2, 0..<2), mean((width - 2)..<width, 0..<2), mean(0..<2, (height - 2)..<height), mean((width - 2)..<width, (height - 2)..<height)],
    ])
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
case "statusitems" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; statusItems(pid)
case "trusted": print(AXIsProcessTrusted() ? "true" : "false")
case "key" where arguments.count == 2: key(arguments[1])
case "click" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; click(x, y)
case "rightclick" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; mouse(x, y, [.mouseMoved, .rightMouseDown, .rightMouseUp], .right)
case "ctrlclick" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; controlClick(x, y)
case "move" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; mouse(x, y, [.mouseMoved], .left)
case "ax" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axTree(pid)
case "axpress" where arguments.count == 3: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axPress(pid, arguments[2])
case "focusedid" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; focusedIdentifier(pid)
case "screenrecording": print(CGPreflightScreenCaptureAccess() ? "true" : "false")
case "ring" where arguments.count == 4: guard let offset = Int(arguments[2]), let thickness = Int(arguments[3]) else { fail("not a number") }; ring(arguments[1], offset, thickness)
case "localized" where arguments.count >= 4: localized(arguments[1], arguments[2], Array(arguments[3...]))
case "icons" where arguments.count >= 4: icons(arguments[1], arguments[2], arguments[3], Array(arguments[4...]))
default: fail("unknown command or arguments: \(arguments.joined(separator: " "))")
}
