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
//                                    (points, top left), title, description, help (the tooltip); macOS 27 draws them in no window of the
//                                    process, so CGWindowList cannot see them (needs Accessibility; exit code 3 without it)
//   menu <pid>                       opens the menu of the process's status item (AXPress in AXExtrasMenuBar), prints it as
//                                    JSON (title, enabled, checked, mark, shortcut, help = the item's tooltip, separator,
//                                    submenu, recursively; submenus are opened for reading only) and closes it again (T-209;
//                                    needs Accessibility; exit code 1 when the menu does not open or stays open)
//   menupress <pid> <path>           opens the menu, follows a path of exact titles separated by ">" (e.g.
//                                    "Printer 1>Show camera") and presses the last item; an unknown or disabled item is
//                                    exit code 1 with the titles of that level on stderr; the menu is always closed (T-209)
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
//   axwatch <pid> <identifier> <ms> [<stop>]  JSON lines: the text (value, title or description) and frame of the
//                                    element with that identifier at the start and at every change, polled every 50 ms
//                                    for <ms>, or until 1.5 s after the text contained <stop> (needs Accessibility)
//   windowsat <x> <y>                the on-screen windows containing a point, front to back: pid, owner, number, layer,
//                                    bounds (whether a click there reaches the window under test)
//   front <x> <y> <seconds>          a stand-in other app: an accessory app with one small window at a point, activated;
//                                    prints its pid and runs until <seconds> pass or it is killed
//   regiondiff <a.png> <b.png> <window width> <x> <y> <w> <h>  JSON: share of pixels (luminance difference > 40) and
//                                    mean difference in a rectangle (points from the window's top left) of two captures
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
            "title": text(kAXTitleAttribute), "description": text(kAXDescriptionAttribute), "help": text(kAXHelpAttribute),
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
    // The modifier keys themselves, pressed before and released after the key as on a real keyboard: AppKit routes some
    // shortcuts (Cmd-.) by the modifier state of the keyboard, not by the flags of the key event alone (T-140).
    var modifiers: [(CGKeyCode, CGEventFlags)] = []
    var code: CGKeyCode?
    for part in combo.lowercased().split(separator: "+").map(String.init) {
        switch part {
        case "cmd", "command": modifiers.append((55, .maskCommand))
        case "ctrl", "control": modifiers.append((59, .maskControl))
        case "alt", "opt", "option": modifiers.append((58, .maskAlternate))
        case "shift": modifiers.append((56, .maskShift))
        case "fn": flags.insert(.maskSecondaryFn)
        default:
            guard let found = keyCodes[part] else { fail("unknown key '\(part)'") }
            code = found
        }
    }
    guard let keyCode = code else { fail("no key in '\(combo)'") }
    let source = CGEventSource(stateID: .hidSystemState)
    func post(_ virtualKey: CGKeyCode, _ down: Bool, _ eventFlags: CGEventFlags) {
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: virtualKey, keyDown: down) else { fail("no key event") }
        event.flags = eventFlags
        event.post(tap: .cghidEventTap)
        usleep(30_000)
    }
    for (modifier, flag) in modifiers {
        flags.insert(flag)
        post(modifier, true, flags)
    }
    post(keyCode, true, flags)
    post(keyCode, false, flags)
    for (modifier, flag) in modifiers.reversed() {
        flags.remove(flag)
        post(modifier, false, flags)
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

/// The text an element shows to the Accessibility API: its value, else its title, else its description (Avalonia gives a
/// TextBlock its text as one of them, depending on the version).
func shownText(_ element: AXUIElement) -> String {
    for name in [kAXValueAttribute, kAXTitleAttribute, kAXDescriptionAttribute] {
        let value = text(element, name)
        if !value.isEmpty { return value }
    }
    return ""
}

func findInWindows(_ app: AXUIElement, _ identifier: String) -> AXUIElement? {
    for window in attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? [] {
        if let element = find(window, identifier) { return element }
    }
    return nil
}

/// Watches the text of one element for <milliseconds>: prints a JSON line at the start and at every change (time since
/// the start, found, text, frame), so that a short "Checking…" is seen and a killed watch still leaves its lines. Stops
/// <linger> ms after the text first contained <stop>, so that a text that vanishes again right away is seen as well.
func axWatch(_ pid: Int32, _ identifier: String, _ milliseconds: Int, _ stop: String?) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    let start = Date()
    let linger = 1_500
    var element: AXUIElement?
    var last: String?
    var stopSeen: Int?
    while true {
        let elapsed = Int(Date().timeIntervalSince(start) * 1000)
        // A removed element (hidden TextBlock, closed window) has no role any more: look it up again.
        if element == nil || attribute(element!, kAXRoleAttribute) == nil { element = findInWindows(app, identifier) }
        let shown = element.map(shownText) ?? ""
        let place = element.map(frame) ?? [:]
        let state = "\(element != nil)|\(shown)|\(place["width"] ?? 0)x\(place["height"] ?? 0)"
        if state != last {
            last = state
            if let data = try? JSONSerialization.data(withJSONObject: ["ms": elapsed, "found": element != nil, "text": shown, "frame": place], options: [.sortedKeys]),
               let line = String(data: data, encoding: .utf8) {
                print(line)
                fflush(stdout)
            }
        }
        if stopSeen == nil, let stop, !stop.isEmpty, shown.contains(stop) { stopSeen = elapsed }
        if let seen = stopSeen, elapsed - seen >= linger { return }
        if elapsed >= milliseconds { return }
        usleep(50_000)
    }
}

/// The on-screen windows (front to back) that contain a point: owner pid and name, number, layer, bounds; shows whether
/// a click there reaches the expected window. Needs no Screen Recording (window names are not read).
func windowsAt(_ x: Double, _ y: Double) {
    guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { fail("no window list") }
    var result: [[String: Any]] = []
    for window in list {
        let bounds = window[kCGWindowBounds as String] as? [String: Any] ?? [:]
        func number(_ key: String) -> Double { (bounds[key] as? NSNumber)?.doubleValue ?? 0 }
        let rect = CGRect(x: number("X"), y: number("Y"), width: number("Width"), height: number("Height"))
        let alpha = (window[kCGWindowAlpha as String] as? NSNumber)?.doubleValue ?? 1
        guard rect.contains(CGPoint(x: x, y: y)), alpha > 0 else { continue }
        result.append([
            "pid": (window[kCGWindowOwnerPID as String] as? NSNumber)?.intValue ?? 0,
            "owner": window[kCGWindowOwnerName as String] as? String ?? "",
            "number": (window[kCGWindowNumber as String] as? NSNumber)?.intValue ?? 0,
            "layer": (window[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0,
            "x": rect.origin.x, "y": rect.origin.y, "width": rect.width, "height": rect.height,
        ])
    }
    printJSON(result)
}

/// A stand-in "other app" for the checks: an accessory app (no Dock icon) with one small window at a point of the main
/// display (top left origin), activated once; prints its pid and runs until <seconds> pass or it is killed. Unlike
/// Finder it opens no window over the window under test.
func frontApp(_ x: Double, _ y: Double, _ seconds: Double) {
    let application = NSApplication.shared
    application.setActivationPolicy(.accessory)
    let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 240, height: 60), styleMask: [.titled], backing: .buffered, defer: false)
    window.title = "macos-verify: other app"
    window.isReleasedWhenClosed = false
    window.setFrameTopLeftPoint(NSPoint(x: x, y: mainHeight() - y))
    window.makeKeyAndOrderFront(nil)
    if #available(macOS 14, *) { application.activate() } else { application.activate(ignoringOtherApps: true) }
    print(ProcessInfo.processInfo.processIdentifier)
    fflush(stdout)
    Timer.scheduledTimer(withTimeInterval: seconds, repeats: false) { _ in exit(0) }
    application.run()
}

/// JSON: how much a rectangle (points from the top left of a window <windowWidth> points wide) differs between two
/// captures of that window: the share of pixels whose luminance differs by more than 40 and the mean difference. Shows
/// whether a text was really drawn there (a capture before and after it appeared).
func regionDiff(_ before: String, _ after: String, _ windowWidth: Double, _ x: Double, _ y: Double, _ width: Double, _ height: Double) {
    func pixels(_ path: String) -> (CGContext, Int, Int) {
        guard let image = NSImage(contentsOfFile: path), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { fail("not an image: \(path)") }
        guard let context = CGContext(data: nil, width: cg.width, height: cg.height, bitsPerComponent: 8, bytesPerRow: cg.width * 4,
                                      space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { fail("no bitmap") }
        context.draw(cg, in: CGRect(x: 0, y: 0, width: cg.width, height: cg.height))
        return (context, cg.width, cg.height)
    }
    let (a, aw, ah) = pixels(before)
    let (b, bw, bh) = pixels(after)
    guard aw == bw, ah == bh else { fail("the captures differ in size (\(aw) x \(ah), \(bw) x \(bh))") }
    guard let pa = a.data?.assumingMemoryBound(to: UInt8.self), let pb = b.data?.assumingMemoryBound(to: UInt8.self) else { fail("no pixels") }
    func luminance(_ p: UnsafeMutablePointer<UInt8>, _ i: Int) -> Double { 0.2126 * Double(p[i]) + 0.7152 * Double(p[i + 1]) + 0.0722 * Double(p[i + 2]) }
    // Pixels per point of the capture (2 on Retina, 1 at @1x).
    let scale = Double(aw) / max(1, windowWidth)
    let left = max(0, Int(x * scale)), top = max(0, Int(y * scale))
    let right = min(aw, Int((x + width) * scale)), bottom = min(ah, Int((y + height) * scale))
    var changed = 0, total = 0
    var sum = 0.0
    if right > left && bottom > top {
        for row in top..<bottom {
            for column in left..<right {
                let i = (row * aw + column) * 4
                let difference = abs(luminance(pa, i) - luminance(pb, i))
                sum += difference
                if difference > 40 { changed += 1 }
                total += 1
            }
        }
    }
    printJSON(["pixels": total, "changed": changed, "share": total > 0 ? Double(changed) / Double(total) : 0, "mean": total > 0 ? sum / Double(total) : 0, "scale": scale])
}

// ---------------------------------------------------------------------------------------------------------------------
// The status item menu (T-209)
// ---------------------------------------------------------------------------------------------------------------------

/// How long one menu level may take to open.
let menuLevelTimeout = 2.0

/// The first visible status item of a process (its AXMenuBarItem in AXExtrasMenuBar), or nil.
func statusItem(_ pid: Int32) -> AXUIElement? {
    guard let bar = attribute(AXUIElementCreateApplication(pid), "AXExtrasMenuBar") else { return nil }
    return children(bar as! AXUIElement).first { let size = frame($0); return size["width"]! > 0 && size["height"]! > 0 }
}

/// The on-screen windows of a process on the pop-up menu level: an open NSMenu (and each open submenu) is one of them.
func menuWindowCount(_ pid: Int32) -> Int {
    let level = Int(CGWindowLevelForKey(.popUpMenuWindow))
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
    return list.filter { ($0[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == pid && ($0[kCGWindowLayer as String] as? NSNumber)?.intValue == level }.count
}

func submenu(_ element: AXUIElement) -> AXUIElement? { children(element).first { text($0, kAXRoleAttribute) == kAXMenuRole } }

func waitFor<T>(_ seconds: Double, _ probe: () -> T?) -> T? {
    let deadline = Date().addingTimeInterval(seconds)
    repeat {
        if let value = probe() { return value }
        usleep(50_000)
    } while Date() < deadline
    return nil
}

/// Opens the menu of an element (a status item, or a menu item with a submenu) and returns it once it has items. AXPress
/// runs the app's menu tracking inside the action, so its answer only comes when the menu closes: a short messaging
/// timeout lets the helper go on, and "cannot complete" means the menu is open. A menu item whose submenu items are
/// readable already is not pressed (-always: the status item, whose menu has to be really shown); AXShowMenu is the
/// second try.
func openMenu(_ element: AXUIElement, always: Bool = false) -> AXUIElement? {
    if !always, let menu = submenu(element), !children(menu).isEmpty { return menu }
    AXUIElementSetMessagingTimeout(element, 0.25)
    for action in [kAXPressAction, kAXShowMenuAction] {
        let status = AXUIElementPerformAction(element, action as CFString)
        guard status == .success || status == .cannotComplete else { continue }
        let menu = waitFor(menuLevelTimeout) { () -> AXUIElement? in
            guard let menu = submenu(element), !children(menu).isEmpty else { return nil }
            return menu
        }
        if let menu { return menu }
    }
    return nil
}

/// Closes every open menu of the process: AXCancel on the top menu, then Esc while a menu window is still on screen. When
/// no menu window was ever seen (a macOS that draws the menu in another process), the window list cannot tell, so one Esc
/// is sent blindly unless the pressed item closed the menu itself. An open menu blocks all input, so this runs on every
/// way out. Returns whether no menu window is left.
func closeMenus(_ pid: Int32, _ top: AXUIElement?, windowsSeen: Bool, pressed: Bool = false) -> Bool {
    if let top { _ = AXUIElementPerformAction(top, kAXCancelAction as CFString) }
    let closed = { waitFor(0.5) { menuWindowCount(pid) == 0 ? true : nil } != nil }
    guard windowsSeen else {
        if !pressed { key("escape") }
        return true
    }
    for _ in 0..<3 {
        if closed() { return true }
        key("escape")
    }
    return closed()
}

/// "ctrl+opt+shift+cmd+q" from AXMenuItemCmdChar and AXMenuItemCmdModifiers (bits: shift 1, option 2, control 4, no command 8).
func shortcut(_ item: AXUIElement) -> String {
    let character = text(item, "AXMenuItemCmdChar")
    guard !character.isEmpty else { return "" }
    let modifiers = (attribute(item, "AXMenuItemCmdModifiers") as? NSNumber)?.intValue ?? 0
    var parts: [String] = []
    if modifiers & 4 != 0 { parts.append("ctrl") }
    if modifiers & 2 != 0 { parts.append("opt") }
    if modifiers & 1 != 0 { parts.append("shift") }
    if modifiers & 8 == 0 { parts.append("cmd") }
    return (parts + [character.lowercased()]).joined(separator: "+")
}

func isEnabled(_ item: AXUIElement) -> Bool { (attribute(item, kAXEnabledAttribute) as? NSNumber)?.boolValue ?? false }

/// The items of an open menu as JSON objects; a submenu is opened (for reading only) when its items are not readable yet.
func menuItems(_ menu: AXUIElement, _ depth: Int) -> [[String: Any]] {
    children(menu).filter { text($0, kAXRoleAttribute) == kAXMenuItemRole }.map { item in
        let title = text(item, kAXTitleAttribute)
        let mark = text(item, "AXMenuItemMarkChar")
        let keys = shortcut(item)
        let enabled = isEnabled(item)
        var result: [String: Any] = [
            "title": title, "enabled": enabled, "checked": !mark.isEmpty, "mark": mark, "shortcut": keys,
            "help": text(item, kAXHelpAttribute), "separator": false, "submenu": NSNull(),
        ]
        if let sub = submenu(item) {
            let open = depth < 4 ? (children(sub).isEmpty ? openMenu(item) : sub) : nil
            result["submenu"] = open.map { menuItems($0, depth + 1) } ?? []
        } else if title.isEmpty && keys.isEmpty && !enabled {
            // NSMenuItem.separatorItem: an untitled, disabled item without a submenu.
            result["separator"] = true
        }
        return result
    }
}

/// Opens the status item menu, runs <body> with it (returns whether it pressed an item, which closes the menu itself) and
/// closes the menu on every way out; exit code 1 when it does not open or stays open.
func withStatusMenu(_ pid: Int32, _ body: (AXUIElement) -> Bool) {
    requireTrusted()
    // Every Accessibility call of the helper gives up after a second instead of the default 6 s.
    AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), 1.0)
    guard let item = statusItem(pid) else { fail("no status item of process \(pid) in the menu bar (AXExtrasMenuBar)") }
    guard let menu = openMenu(item, always: true) else {
        _ = closeMenus(pid, nil, windowsSeen: menuWindowCount(pid) > 0)
        fail("the menu of the status item did not open within \(menuLevelTimeout) s")
    }
    let windowsSeen = menuWindowCount(pid) > 0
    let pressed = body(menu)
    if !closeMenus(pid, menu, windowsSeen: windowsSeen, pressed: pressed) { fail("the menu is still open after AXCancel and Esc") }
}

func menuJSON(_ pid: Int32) {
    withStatusMenu(pid) { menu in
        // menuWindows: the open menu windows of the app while it was read (0 = this macOS draws menus elsewhere).
        printJSON(["items": menuItems(menu, 0), "menuWindows": menuWindowCount(pid)])
        return false
    }
}

func menuPress(_ pid: Int32, _ path: String) {
    let titles = path.components(separatedBy: ">")
    var problem: String?
    withStatusMenu(pid) { top in
        var menu = top
        for (index, title) in titles.enumerated() {
            let items = children(menu).filter { text($0, kAXRoleAttribute) == kAXMenuItemRole }
            let level = titles[..<index].joined(separator: ">")
            guard let item = items.first(where: { text($0, kAXTitleAttribute) == title }) else {
                let available = items.map { "'\(text($0, kAXTitleAttribute))'" }.filter { $0 != "''" }
                problem = "no item '\(title)' in the menu\(level.isEmpty ? "" : " '\(level)'"); its items: \(available.joined(separator: ", "))"
                return false
            }
            guard isEnabled(item) else { problem = "the item '\(title)' is disabled (help: '\(text(item, kAXHelpAttribute))')"; return false }
            if index < titles.count - 1 {
                guard let next = openMenu(item) else { problem = "the submenu '\(title)' did not open within \(menuLevelTimeout) s"; return false }
                menu = next
                continue
            }
            AXUIElementSetMessagingTimeout(item, 0.25)
            let status = AXUIElementPerformAction(item, kAXPressAction as CFString)
            if status != .success && status != .cannotComplete { problem = "AXPress on '\(title)' failed: \(status.rawValue)"; return false }
            // The pressed item closes the menu itself; closeMenus only cleans up what is left.
            _ = waitFor(1.0) { menuWindowCount(pid) == 0 ? true : nil }
            return true
        }
        return false
    }
    if let problem { fail(problem) }
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard let command = arguments.first else { fail("usage: macos-verify-helper screens|windows|menubar|session|statusitems|menu|menupress|trusted|key|click|localized|icons ...") }
switch command {
case "screens": screens()
case "windows" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; windows(pid)
case "menubar": menuBarWindows()
case "session": session()
case "statusitems" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; statusItems(pid)
case "menu" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; menuJSON(pid)
case "menupress" where arguments.count == 3: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; menuPress(pid, arguments[2])
case "trusted": print(AXIsProcessTrusted() ? "true" : "false")
case "key" where arguments.count == 2: key(arguments[1])
case "click" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; click(x, y)
case "rightclick" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; mouse(x, y, [.mouseMoved, .rightMouseDown, .rightMouseUp], .right)
case "ctrlclick" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; controlClick(x, y)
case "move" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; mouse(x, y, [.mouseMoved], .left)
case "ax" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axTree(pid)
case "axpress" where arguments.count == 3: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axPress(pid, arguments[2])
case "focusedid" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; focusedIdentifier(pid)
case "axwatch" where arguments.count == 4 || arguments.count == 5:
    guard let pid = Int32(arguments[1]), let milliseconds = Int(arguments[3]) else { fail("not a pid or a time") }
    axWatch(pid, arguments[2], milliseconds, arguments.count == 5 ? arguments[4] : nil)
case "windowsat" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; windowsAt(x, y)
case "front" where arguments.count == 4:
    guard let x = Double(arguments[1]), let y = Double(arguments[2]), let seconds = Double(arguments[3]) else { fail("not a point or a time") }
    frontApp(x, y, seconds)
case "regiondiff" where arguments.count == 8:
    let numbers = arguments[3...].compactMap(Double.init)
    guard numbers.count == 5 else { fail("not a number") }
    regionDiff(arguments[1], arguments[2], numbers[0], numbers[1], numbers[2], numbers[3], numbers[4])
case "screenrecording": print(CGPreflightScreenCaptureAccess() ? "true" : "false")
case "ring" where arguments.count == 4: guard let offset = Int(arguments[2]), let thickness = Int(arguments[3]) else { fail("not a number") }; ring(arguments[1], offset, thickness)
case "localized" where arguments.count >= 4: localized(arguments[1], arguments[2], Array(arguments[3...]))
case "icons" where arguments.count >= 4: icons(arguments[1], arguments[2], arguments[3], Array(arguments[4...]))
default: fail("unknown command or arguments: \(arguments.joined(separator: " "))")
}
