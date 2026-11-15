// Small macOS helper of tools/macos-verify.ps1 (T-105): reads what the verification checks need from the system APIs
// that have no command line tool, so that the app itself needs no extra code. Compiled by the script with swiftc (Xcode
// Command Line Tools); no other dependency. Prints JSON (or plain values) on stdout, errors on stderr with exit code 1.
//
//   screens                          the displays: id, UUID, name, frame and usable area (points, origin top left of
//                                    the main display, as CGWindowList), scale, main; read as an app reads them (the menu
//                                    bar of every display with separate Spaces left out of its usable area)
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
//                                    A title ending with "*" matches every item whose title starts with the rest (an item
//                                    whose title carries a changing percentage, T-213)
//   trusted                          "true" / "false": whether this process (i.e. the terminal that started it) has the
//                                    Accessibility permission; never asks for it
//   key <combo>                      posts a key press, e.g. ctrl+cmd+f, cmd+w, cmd+period, cmd+shift+], escape
//                                    (needs Accessibility; exit code 3 without it)
//   type <text>                      types the text as Unicode key events into the focused element of the front app,
//                                    whatever the keyboard layout (needs Accessibility; T-213)
//   panel <pid> [<seconds>]          the open panels (NSOpenPanel / NSSavePanel: a window or sheet with a default and a
//                                    cancel button and a file browser) of the process, or of the panel service of
//                                    sandboxed apps when it has none, waiting up to <seconds> (default 0) for one: JSON
//                                    list with pid and process (where the panel really is), role, subrole, sheet, title,
//                                    texts (the message), main, focused (the key window of its app), frontmost (its app is
//                                    active), folder (AXDocument) and folderTitle (the path pop-up), defaultButton and
//                                    cancelButton (title, enabled: the language of AppKit), items of the folder shown
//                                    (name, role, enabled and alpha: the opacity of the name's text colour, each where AX
//                                    has it; T-243) and frame (needs Accessibility; T-213)
//   panelgo <pid> <path> [<seconds>] chooses <path> in the open panel (waits up to <seconds>, default 5): Cmd-Shift-G, the
//                                    path typed (as "type"), Return, then the default button (Open / Choose); prints
//                                    JSON (field: what the Go to field held). Exit code 1: no panel, 2: no Go to field or
//                                    the panel stayed open, 3: no Accessibility, 4: the default button stays disabled
//                                    (the item cannot be chosen); on every failure the panel is cancelled (T-213)
//   panelcancel <pid>                closes every open panel of the process with its cancel button, else Esc; "no panel"
//                                    when none is open; exit code 1 when one stays open (T-213)
//   click <x> <y>                    a left click at a point (global, top left origin; needs Accessibility)
//   rightclick <x> <y>               a right click at a point (needs Accessibility)
//   ctrlclick <x> <y>                a left click with Control held (needs Accessibility)
//   move <x> <y>                     moves the mouse pointer to a point (needs Accessibility)
//   ax <pid>                         the Accessibility tree of the windows of a process as JSON: role, subrole,
//                                    identifier, title, description, value, focused, frame (points, top left), actions,
//                                    children; enabled and help (AXHelp) where present (needs Accessibility)
//   axpress <pid> <identifier>       performs AXPress on the first element with that identifier (needs Accessibility)
//   ncpress <text> <action> [<seconds>]  a notification banner or Notification Centre entry (the AX tree of
//                                    com.apple.notificationcenterui) whose texts contain <text>: performs <action> on it,
//                                    AXPress (= a click) or the name of a button of the notification (an AX custom action
//                                    "Name:<action>…", e.g. "False alarm"); waits up to <seconds> (default 10) for it.
//                                    Prints the element (description, actions) as JSON. Exit code 1: no such
//                                    notification, 2: the action failed, 3: no Accessibility, 4: the notification is
//                                    there but macOS shows no such action in AX (its actions on stderr) (T-212)
//   axwindows <pid>                  the windows of a process as JSON: title, document (AXDocument: the file URL of
//                                    the folder a Finder window shows), frame, subrole, main, focused (the key window),
//                                    minimized, buttons (close, minimize, zoom, fullscreen: those of its title bar); no
//                                    contents (needs Accessibility; T-210, T-215)
//   axwin <pid> <title> <action>     on the window with that exact title ("*": the main window): minimize, restore,
//                                    raise, fullscreen (the green button) or close (needs Accessibility; T-215)
//   focusedmenubar                   the menu bar the system shows: pid of the active app and the titles and frames of
//                                    the top items of its AXMenuBar (needs Accessibility; T-215)
//   dock <app> info|menu|press|menupress <title>  the Dock item of an app (its bundle path, or its exact title): its
//                                    title, subrole, url, frame, running; the items of its Dock menu (AXShowMenu, read,
//                                    closed: the app's open windows are items of it); AXPress on the item (a click); or
//                                    AXPress on the item of its menu with that title. Exit code 1: no such item, menu or
//                                    menu item (the titles on stderr), 2: the press failed (needs Accessibility; T-215)
//   drag <x1> <y1> <x2> <y2> [<ms>]  a left mouse drag in even steps over <ms> (default 600) (needs Accessibility; T-215)
//   axclose <pid> <path>             closes the window of a process whose AXDocument is <path> (a Finder window of a
//                                    folder; one without AXDocument, as Finder's on macOS 27, by its title = the last part
//                                    of <path>) with its close button (needs Accessibility; T-210)
//   focusedid <pid>                  the identifier of the focused element of the process, or "" (needs Accessibility)
//   axwatch <pid> <identifier> <ms> [<stop>]  JSON lines: the text (value, title or description) and frame of the
//                                    element with that identifier at the start and at every change, polled every 50 ms
//                                    for <ms>, or until 1.5 s after the text contained <stop> (needs Accessibility)
//   inputsource                      the keyboard input sources as JSON: current (the selected one's id, e.g.
//                                    com.apple.keylayout.Czech) and enabled (the ids of the enabled keyboard layouts)
//   inputsource <id> <seconds>       selects the keyboard input source <id> (enabled first when it is not, disabled again
//                                    afterwards), prints a JSON line (previous, selected, enabledHere) and keeps it until
//                                    <seconds> pass or it gets SIGTERM, SIGINT or SIGHUP; then it always selects the
//                                    previous source again and prints a JSON line (restored, current). Exit code 1: no such
//                                    source, 2: it could not be selected (the previous one is selected again) (T-216)
//   apps <bundle id>                 the pids of the running apps with that bundle identifier (case-insensitive) as JSON
//                                    (T-216: the default browser before and after a link opened)
//   terminate <pid>                  asks the app with that pid to quit, as the Quit menu item would
//                                    (NSRunningApplication.terminate, no Apple event permission needed); exit code 1
//                                    when it is still running after 10 s (T-216: the browser a check started)
//   windowsat <x> <y>                the on-screen windows containing a point, front to back: pid, owner, number, layer,
//                                    bounds (whether a click there reaches the window under test)
//   front <x> <y> <seconds> [regular]  a stand-in other app: an accessory app (regular: an ordinary app with a Dock
//                                    icon and a menu bar, T-215) with one small window at a point, activated; prints its
//                                    pid and runs until <seconds> pass or it is killed
//   activate <pid>                   brings the app with that pid to the front through the Accessibility API
//                                    (AXFrontmost), which the cooperative activation of macOS 14+ does not refuse as it
//                                    may refuse the app's own activate(); exit code 1 when it is not in front within 3 s
//   regiondiff <a.png> <b.png> <window width> <x> <y> <w> <h>  JSON: share of pixels (luminance difference > 40), share
//                                    of pixels with a difference > 8 (slightShare, T-215) and mean difference in a
//                                    rectangle (points from the window's top left) of two captures
//   luma <png> [<left> <top> <right> <bottom>]  JSON: mean luminance (0-255) and its deviation of a part of an image
//                                    (shares 0 to 1 of its sides; default the whole image) (T-215)
//   lumawatch <window> <ms> <seconds> <folder>  captures the window (its number) every <ms> into <folder> and prints a
//                                    JSON line per capture: ms, file, mean and deviation of the luminance of the middle
//                                    (20 to 80 % of each side), or the error (needs Screen Recording; T-215)
//   screenrecording                  "true" / "false": whether this process may capture other apps' windows (Screen
//                                    Recording); never asks for it
//   ring <png> <offset> <thickness>  JSON: the mean luminance (0-255) of the band <offset> to <offset + thickness> pixels
//                                    inside each edge of the image (top, right, bottom, left) and of the 2 x 2 pixels in
//                                    each corner (for an outline drawn at the edge of a window capture)
//   localized <bundle> <key> <lang>...  for each language the localization macOS picks for a user with that language
//                                    (Bundle.preferredLocalizations) and the value of <key> in its InfoPlist.strings
//   dlopen <library>...              loads each library with dlopen (RTLD_NOW: every dependency resolved at once) and
//                                    prints "loaded <path>"; the first failure is exit code 1 with the dyld error (T-217:
//                                    the FFmpeg libraries of a release bundle, which the app loads only for a camera)
//   decode <file.mp4>                plays a recording through AVFoundation, as QuickTime Player would (T-210): JSON with
//                                    isPlayable, isReadable, the video track's codec (e.g. avc1), size, duration and
//                                    sample count; every sample decoded by AVAssetReader (decoded frames, monotonic
//                                    presentation times, the reader's error); seeks with zero tolerance to 0, 25, 50,
//                                    75 % and the last frame (AVAssetImageGenerator) with the mean luminance (0-255) and
//                                    its standard deviation of each image. "problems" lists what failed: exit code 1
//                                    when a frame does not decode, a seek fails or an image is black or of one colour
//   icons <out.png> <menu bar icon dir> <app icon or bundle> <system app>...
//                                    a contact sheet PNG: the menu bar template icons on a light and a dark menu bar at
//                                    1x and 2x, and the app icon next to the system app icons at 16 to 128 points

import AppKit
import Carbon
import ApplicationServices
import AVFoundation
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
    // Connected to the window server like an app: without NSApplication a command line tool gets the usable area of a
    // display other than the main one without its menu bar (visibleFrame = frame), unlike the app under test.
    _ = NSApplication.shared
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
    // Only where the element has them (T-210): enabled (AXEnabled) and help (AXHelp, the tooltip).
    if let enabled = attribute(element, kAXEnabledAttribute) as? NSNumber { result["enabled"] = enabled.boolValue }
    let help = text(element, kAXHelpAttribute)
    if !help.isEmpty { result["help"] = help }
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

/// The windows of a process without their contents: title, document (AXDocument) and frame (T-210: which folder a
/// Finder window shows, without reading every file it lists).
func axWindows(_ pid: Int32) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 1.0)
    let focusedWindow = attribute(app, kAXFocusedWindowAttribute).map { $0 as! AXUIElement }
    printJSON((attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []).map { window in
        func flag(_ name: String) -> Bool { (attribute(window, name) as? NSNumber)?.boolValue ?? false }
        // T-215: main and focused (the key window), minimized, and the title bar buttons it has (none: no window frame).
        let buttons = [("close", kAXCloseButtonAttribute), ("minimize", kAXMinimizeButtonAttribute), ("zoom", kAXZoomButtonAttribute), ("fullscreen", "AXFullScreenButton")]
            .filter { attribute(window, $0.1) != nil }.map(\.0)
        return ["title": text(window, kAXTitleAttribute), "document": text(window, kAXDocumentAttribute), "frame": frame(window), "subrole": text(window, kAXSubroleAttribute),
                "main": flag(kAXMainAttribute), "focused": focusedWindow.map { CFEqual($0, window) } ?? false, "minimized": flag(kAXMinimizedAttribute), "buttons": buttons] as [String: Any]
    })
}

/// Closes the window of a process whose AXDocument is the folder or file at `path` (its close button), e.g. the Finder
/// window a check opened (T-210); exit code 1 when there is none or it does not close.
func axClose(_ pid: Int32, _ path: String) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    let wanted = URL(fileURLWithPath: path).resolvingSymlinksInPath().standardizedFileURL.path
    // By AXDocument; a Finder window of macOS 27 has none (first run with an unlocked screen 2026-11-15), then by its
    // title, the name of the folder.
    let leaf = URL(fileURLWithPath: wanted).lastPathComponent
    let matches: (AXUIElement) -> Bool = { window in
        let document = text(window, kAXDocumentAttribute)
        if document.isEmpty { return !leaf.isEmpty && text(window, kAXTitleAttribute) == leaf }
        guard let url = URL(string: document), url.isFileURL else { return false }
        return url.resolvingSymlinksInPath().standardizedFileURL.path == wanted
    }
    guard let window = (attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []).first(where: matches) else { fail("no window of process \(pid) shows \(path)") }
    guard let button = attribute(window, kAXCloseButtonAttribute) else { fail("the window that shows \(path) has no close button") }
    let status = AXUIElementPerformAction(button as! AXUIElement, kAXPressAction as CFString)
    if status != .success { fail("AXPress on the close button failed: \(status.rawValue)") }
    if waitFor(3.0, { (attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []).contains(where: matches) ? nil : true }) == nil {
        fail("the window that shows \(path) is still open")
    }
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

func load(_ libraries: [String]) {
    for library in libraries {
        guard dlopen(library, RTLD_NOW) != nil else { fail("dlopen \(library): \(dlerror().map { String(cString: $0) } ?? "unknown error")") }
        print("loaded \(library)")
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

/// The text an element shows to the Accessibility API: its value, else its title, else its description (Avalonia gives a
/// TextBlock its text as one of them, depending on the version).
/// The texts of an element and its descendants (title, description, value), for finding a notification by its title or body.
func subtreeTexts(_ element: AXUIElement, _ depth: Int = 0) -> [String] {
    var texts = [kAXTitleAttribute, kAXDescriptionAttribute, kAXValueAttribute].map { text(element, $0) }.filter { !$0.isEmpty }
    if depth < 12 { for child in children(element) { texts += subtreeTexts(child, depth + 1) } }
    return texts
}

func actionNames(_ element: AXUIElement) -> [String] {
    var names: CFArray?
    return AXUIElementCopyActionNames(element, &names) == .success ? names as? [String] ?? [] : []
}

/// The deepest element under `element` that can be pressed and whose texts contain `wanted`: the notification itself,
/// not the window or the list around it (their texts contain it too).
func notificationElement(_ element: AXUIElement, _ wanted: String, _ depth: Int = 0) -> AXUIElement? {
    guard depth < 12, subtreeTexts(element).contains(where: { $0.contains(wanted) }) else { return nil }
    for child in children(element) { if let found = notificationElement(child, wanted, depth + 1) { return found } }
    return actionNames(element).contains(kAXPressAction as String) ? element : nil
}

/// A notification of Notification Centre (the banner while it shows, or its entry in the open Notification Centre),
/// found by a text of it; performs AXPress or the custom action of one of its buttons (T-212). macOS names a custom
/// action "Name:<title>\nTarget:…\nSelector:…".
func notificationPress(_ wanted: String, _ action: String, _ seconds: Double) {
    requireTrusted()
    guard let center = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.notificationcenterui").first else {
        fail("Notification Centre (com.apple.notificationcenterui) does not run")
    }
    let app = AXUIElementCreateApplication(center.processIdentifier)
    let found = waitFor(seconds) { () -> AXUIElement? in
        for window in attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? [] {
            if let element = notificationElement(window, wanted) { return element }
        }
        return nil
    }
    guard let element = found else { fail("no notification containing '\(wanted)' in the AX tree of Notification Centre within \(seconds) s") }
    let actions = actionNames(element)
    let chosen = action == (kAXPressAction as String)
        ? action
        : actions.first { $0 == action || $0 == "Name:\(action)" || $0.hasPrefix("Name:\(action)\n") }
    let described: [String: Any] = [
        "role": text(element, kAXRoleAttribute), "subrole": text(element, kAXSubroleAttribute),
        "description": text(element, kAXDescriptionAttribute), "actions": actions, "performed": chosen ?? "",
    ]
    guard let name = chosen else {
        printJSON(described)
        fail("the notification containing '\(wanted)' has no action '\(action)' in AX: \(actions.joined(separator: " | ").replacingOccurrences(of: "\n", with: " "))", code: 4)
    }
    let status = AXUIElementPerformAction(element, name as CFString)
    if status != .success { fail("\(action) on the notification containing '\(wanted)' failed: \(status.rawValue)", code: 2) }
    printJSON(described)
}

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

/// A stand-in "other app" for the checks: an accessory app (no Dock icon; with "regular" an ordinary app) with one small
/// window at a point of the main display (top left origin), activated once; prints its pid and runs until <seconds> pass
/// or it is killed. Unlike Finder it opens no window over the window under test.
func frontApp(_ x: Double, _ y: Double, _ seconds: Double, regular: Bool = false) {
    let application = NSApplication.shared
    // "regular" (T-215): an ordinary app with a Dock icon and its own menu bar, as the user switches to.
    application.setActivationPolicy(regular ? .regular : .accessory)
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
    var changed = 0, slight = 0, total = 0
    var sum = 0.0
    if right > left && bottom > top {
        for row in top..<bottom {
            for column in left..<right {
                let i = (row * aw + column) * 4
                let difference = abs(luminance(pa, i) - luminance(pb, i))
                sum += difference
                if difference > 40 { changed += 1 }
                if difference > 8 { slight += 1 }
                total += 1
            }
        }
    }
    printJSON(["pixels": total, "changed": changed, "share": total > 0 ? Double(changed) / Double(total) : 0, "mean": total > 0 ? sum / Double(total) : 0, "scale": scale,
               "slightShare": total > 0 ? Double(slight) / Double(total) : 0])
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
            // A title ending with "*" matches by its start (an item whose title changes, e.g. a percentage; T-213).
            let matches: (String) -> Bool = title.hasSuffix("*") ? { sameTitle($0).hasPrefix(sameTitle(String(title.dropLast()))) } : { sameTitle($0) == sameTitle(title) }
            guard let item = items.first(where: { matches(text($0, kAXTitleAttribute)) }) else {
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

/// A menu title for comparing: the no-break spaces as plain spaces. NSMenu shows a no-break space of a title (e.g.
/// "Save the last 2\u{00A0}min" of the resources) as a plain space to the Accessibility API, while the menus Avalonia
/// draws itself (the context menu of the camera window) keep it.
func sameTitle(_ title: String) -> String {
    title.replacingOccurrences(of: "\u{00A0}", with: " ").replacingOccurrences(of: "\u{202F}", with: " ").replacingOccurrences(of: "\u{2007}", with: " ")
}

/// Brings an app to the front through the Accessibility API (needs Accessibility): AXFrontmost on the app and AXRaise on
/// its main window. macOS 14+ may refuse the cooperative NSApplication.activate() of a stand-in app started from a
/// terminal while another app is active; this is what an assistive app does instead.
func activateApp(_ pid: Int32) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 1.0)
    let status = AXUIElementSetAttributeValue(app, kAXFrontmostAttribute as CFString, kCFBooleanTrue)
    let main = attribute(app, kAXMainWindowAttribute).map { $0 as! AXUIElement }
    if let window = main ?? (attribute(app, kAXWindowsAttribute) as? [AXUIElement])?.first {
        _ = AXUIElementPerformAction(window, kAXRaiseAction as CFString)
    }
    if waitFor(3.0, { NSRunningApplication(processIdentifier: pid)?.isActive == true ? true : nil }) == nil {
        fail("the app \(pid) is not in front after AXFrontmost (status \(status.rawValue))")
    }
}

/// The result of async work handed from its task to the waiting thread (written once, before the semaphore is signalled).
final class AsyncResult<T>: @unchecked Sendable { var value: Result<T, Error>? }

/// Runs async work to its end on a background task and waits for it (the helper is a synchronous command line tool).
func waitAsync<T>(_ body: @escaping () async throws -> T) -> Result<T, Error> {
    let box = AsyncResult<T>()
    let done = DispatchSemaphore(value: 0)
    Task.detached {
        do { box.value = .success(try await body()) } catch { box.value = .failure(error) }
        done.signal()
    }
    done.wait()
    return box.value!
}

/// The mean luminance (0-255) and its standard deviation of an image, drawn into a small grey bitmap.
func luminance(_ image: CGImage) -> (mean: Double, deviation: Double) {
    let width = min(image.width, 160)
    let height = max(1, image.height * width / max(image.width, 1))
    var pixels = [UInt8](repeating: 0, count: width * height)
    let drawn = pixels.withUnsafeMutableBytes { buffer -> Bool in
        guard let context = CGContext(data: buffer.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width,
                                      space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return false }
        context.interpolationQuality = .medium
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        return true
    }
    if !drawn { return (0, 0) }
    let mean = pixels.reduce(0.0) { $0 + Double($1) } / Double(pixels.count)
    let variance = pixels.reduce(0.0) { $0 + (Double($1) - mean) * (Double($1) - mean) } / Double(pixels.count)
    return (mean, variance.squareRoot())
}

/// Seconds of a CMTime rounded to milliseconds, for the JSON.
func seconds(_ time: CMTime) -> Double { time.isNumeric ? (time.seconds * 1000).rounded() / 1000 : -1 }

/// Calls `visit` with every sample buffer of `output` once reading has started; the error ends the reading.
func readBuffers(_ reader: AVAssetReader, _ output: AVAssetReaderOutput, _ visit: @escaping (CMSampleBuffer) -> Void) -> String? {
    #if compiler(>=6.2)
    if #available(macOS 26, *) {
        // macOS 27 deprecates the synchronous reading; the provider of macOS 26 replaces it.
        let provider = reader.outputProvider(for: output)
        do { try reader.start() } catch { return "the reader did not start: \(error.localizedDescription)" }
        let read = waitAsync { () async throws -> Void in
            while let ready = try await provider.next() { ready.withUnsafeSampleBuffer { visit($0) } }
        }
        if case .failure(let error) = read { return error.localizedDescription }
        return reader.status == .failed ? (reader.error?.localizedDescription ?? "unknown") : nil
    } else {
        return readBuffersBefore26(reader, output, visit)
    }
    #else
    return readBuffersBefore26(reader, output, visit)
    #endif
}

/// The synchronous reading for systems and SDKs before macOS 26 (deprecated in macOS 27), where the provider is missing.
@available(macOS, deprecated: 27.0)
func readBuffersBefore26(_ reader: AVAssetReader, _ output: AVAssetReaderOutput, _ visit: (CMSampleBuffer) -> Void) -> String? {
    reader.add(output)
    guard reader.startReading() else { return "the reader did not start: \(reader.error?.localizedDescription ?? "unknown")" }
    while let sample = output.copyNextSampleBuffer() { visit(sample) }
    return reader.status == .failed ? (reader.error?.localizedDescription ?? "unknown") : nil
}

/// Reads every sample of the track once: without output settings the stored samples, otherwise decoded frames.
func readSamples(_ asset: AVAsset, _ track: AVAssetTrack, decode: Bool) -> (count: Int, times: [CMTime], error: String?) {
    let reader: AVAssetReader
    do { reader = try AVAssetReader(asset: asset) } catch { return (0, [], "AVAssetReader: \(error.localizedDescription)") }
    let settings: [String: Any]? = decode ? [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA] : nil
    let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
    guard reader.canAdd(output) else { return (0, [], "the reader cannot read the video track") }
    final class Tally: @unchecked Sendable { var count = 0; var times: [CMTime] = []; var dropped: String? }
    let tally = Tally()
    let failure = readBuffers(reader, output) { sample in
        if !decode { tally.count += CMSampleBufferGetNumSamples(sample); return }
        // A decoded frame carries its image; a buffer of samples without one is a frame the decoder dropped.
        guard CMSampleBufferGetImageBuffer(sample) != nil else {
            if CMSampleBufferGetNumSamples(sample) > 0 && tally.dropped == nil { tally.dropped = "a sample at \(seconds(CMSampleBufferGetPresentationTimeStamp(sample))) s decoded to no image" }
            return
        }
        tally.count += 1
        tally.times.append(CMSampleBufferGetPresentationTimeStamp(sample))
    }
    let problem = failure.map { "the reader failed after \(tally.count) sample(s): \($0)" } ?? tally.dropped
    return (tally.count, tally.times, problem)
}

func decode(_ path: String) {
    guard FileManager.default.isReadableFile(atPath: path) else { fail("cannot read \(path)") }
    let asset = AVURLAsset(url: URL(fileURLWithPath: path))
    var problems: [String] = []
    var report: [String: Any] = ["file": (path as NSString).lastPathComponent]
    let loaded = waitAsync { () async throws -> (Bool, Bool, CMTime, [AVAssetTrack]) in
        let (playable, readable, duration) = try await asset.load(.isPlayable, .isReadable, .duration)
        return (playable, readable, duration, try await asset.loadTracks(withMediaType: .video))
    }
    guard case .success(let (playable, readable, duration, tracks)) = loaded else {
        if case .failure(let error) = loaded { problems.append("the file does not load: \(error.localizedDescription)") }
        report["problems"] = problems
        printJSON(report)
        exit(1)
    }
    report["isPlayable"] = playable
    report["isReadable"] = readable
    report["duration"] = seconds(duration)
    if !playable { problems.append("not playable") }
    if !readable { problems.append("not readable") }
    guard let track = tracks.first else {
        problems.append("no video track")
        report["problems"] = problems
        printJSON(report)
        exit(1)
    }
    if case .success(let (formats, size, rate)) = waitAsync({ try await track.load(.formatDescriptions, .naturalSize, .nominalFrameRate) }) {
        if let format = formats.first {
            let code = CMFormatDescriptionGetMediaSubType(format)
            report["codec"] = String(bytes: [24, 16, 8, 0].map { UInt8((code >> $0) & 0xFF) }, encoding: .ascii) ?? "\(code)"
        }
        report["width"] = Int(size.width)
        report["height"] = Int(size.height)
        report["nominalFrameRate"] = (Double(rate) * 1000).rounded() / 1000
    } else { problems.append("the video track's format does not load") }

    let stored = readSamples(asset, track, decode: false)
    let decoded = readSamples(asset, track, decode: true)
    report["samples"] = stored.count
    report["decodedFrames"] = decoded.count
    if let error = stored.error { problems.append("reading the samples: \(error)") }
    if let error = decoded.error { problems.append("decoding: \(error)") }
    if decoded.count == 0 { problems.append("no frame decoded") }
    else if decoded.count != stored.count { problems.append("\(decoded.count) of \(stored.count) sample(s) decoded") }
    let backwards = zip(decoded.times, decoded.times.dropFirst()).filter { CMTimeCompare($0.1, $0.0) <= 0 }.count
    report["monotonicPts"] = backwards == 0
    if backwards > 0 { problems.append("\(backwards) presentation time(s) not after the previous one") }

    // Seeks as a player's scrubbing: 0, 25, 50, 75 % of the duration and the last frame, each to the exact time.
    var seeks: [[String: Any]] = []
    if let last = decoded.times.last, duration.isNumeric {
        let generator = AVAssetImageGenerator(asset: asset)
        generator.requestedTimeToleranceBefore = .zero
        generator.requestedTimeToleranceAfter = .zero
        generator.appliesPreferredTrackTransform = true
        let targets: [(String, CMTime)] = [0.0, 0.25, 0.5, 0.75].map { ("\(Int($0 * 100)) %", CMTimeMultiplyByFloat64(duration, multiplier: $0)) } + [("end", last)]
        for (label, time) in targets {
            var seek: [String: Any] = ["at": label, "time": seconds(time)]
            switch waitAsync({ try await generator.image(at: time) }) {
            case .success(let (image, actual)):
                let (mean, deviation) = luminance(image)
                seek["actualTime"] = seconds(actual)
                seek["mean"] = (mean * 10).rounded() / 10
                seek["stdDev"] = (deviation * 10).rounded() / 10
                // A black or single-colour image is a frame the decoder did not really produce.
                if deviation < 2 { problems.append("the image at \(label) is of one colour (mean \(seek["mean"]!), deviation \(seek["stdDev"]!))") }
            case .failure(let error):
                seek["error"] = error.localizedDescription
                problems.append("the seek to \(label) (\(seconds(time)) s) failed: \(error.localizedDescription)")
            }
            seeks.append(seek)
        }
    } else if decoded.count > 0 { problems.append("the duration is not a number: no seek") }
    report["seeks"] = seeks
    report["problems"] = problems
    printJSON(report)
    if !problems.isEmpty { exit(1) }
}

// ---------------------------------------------------------------------------------------------------------------------
// Open panels (T-213)
// ---------------------------------------------------------------------------------------------------------------------

/// Types `text` as Unicode key events (CGEvent, no key codes: any character, whatever the keyboard layout) into the
/// focused element of the front app.
func typeText(_ text: String) {
    requireTrusted()
    let source = CGEventSource(stateID: .hidSystemState)
    // One character per event (a key event carries at most 20 UTF-16 units) keeps every field and completion in step.
    for character in text {
        let chunk = Array(String(character).utf16)
        for down in [true, false] {
            guard let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: down) else { fail("no key event") }
            event.flags = []
            chunk.withUnsafeBufferPointer { event.keyboardSetUnicodeString(stringLength: $0.count, unicodeString: $0.baseAddress) }
            event.post(tap: .cghidEventTap)
        }
        usleep(8_000)
    }
}

/// The process of the open and save panels of sandboxed apps; LayerPeek is not sandboxed, so its panel is in its own
/// process, but a check reports where the panel really was.
let panelServicePrefix = "com.apple.appkit.xpc.openAndSavePanelService"

/// The file browser below `element` (the column browser, or the list, outline or table of a panel), or nil. The column
/// browser first; else the last one depth first: the sidebar (an outline of places, "Recents", "Favourites" …) comes
/// before the file list in the tree (first run with an unlocked screen 2026-11-15: the items were the sidebar's).
func fileBrowser(_ element: AXUIElement) -> AXUIElement? {
    var found: [AXUIElement] = []
    func collect(_ node: AXUIElement, _ depth: Int) {
        let role = text(node, kAXRoleAttribute)
        if role == "AXBrowser" { found.insert(node, at: 0); return }
        if [kAXOutlineRole as String, kAXTableRole as String, kAXListRole as String].contains(role) { found.append(node); return }
        guard depth < 12 else { return }
        for child in children(node) { collect(child, depth + 1) }
    }
    collect(element, 0)
    if let browser = found.first, text(browser, kAXRoleAttribute) == "AXBrowser" { return browser }
    return found.last
}

/// An open or save panel: a window or sheet with a default and a cancel button and a file browser. The sheet of an
/// NSOpenPanel on macOS 27 (identifier "open-panel", first run with an unlocked screen 2026-11-15) has no
/// AXDefaultButton / AXCancelButton: its identifier and file browser tell it then (panelButton finds the buttons).
func isPanel(_ element: AXUIElement) -> Bool {
    guard fileBrowser(element) != nil else { return false }
    if attribute(element, kAXDefaultButtonAttribute) != nil && attribute(element, kAXCancelButtonAttribute) != nil { return true }
    return ["open-panel", "save-panel"].contains(text(element, kAXIdentifierAttribute)) && panelButton(element, kAXDefaultButtonAttribute) != nil
}

/// The default (`kAXDefaultButtonAttribute`) or cancel button of a panel: the attribute, else the bottom row of titled
/// buttons of the panel as AppKit lays it out (the default button rightmost, Cancel left of it).
func panelButton(_ panel: AXUIElement, _ name: String) -> AXUIElement? {
    if let value = attribute(panel, name) { return (value as! AXUIElement) }
    var buttons: [(element: AXUIElement, x: Double, bottom: Double)] = []
    func collect(_ element: AXUIElement, _ depth: Int) {
        guard depth < 12, buttons.count < 100 else { return }
        if text(element, kAXRoleAttribute) == (kAXButtonRole as String), !text(element, kAXTitleAttribute).isEmpty {
            let f = frame(element)
            if f["width", default: 0] > 0 && f["height", default: 0] > 0 { buttons.append((element, f["x", default: 0], f["y", default: 0] + f["height", default: 0])) }
        }
        for child in children(element) { collect(child, depth + 1) }
    }
    collect(panel, 0)
    guard let lowest = buttons.map(\.bottom).max() else { return nil }
    let row = buttons.filter { abs($0.bottom - lowest) <= 4 }.sorted { $0.x > $1.x }
    let index = name == (kAXDefaultButtonAttribute as String) ? 0 : 1
    return row.count >= 2 ? row[index].element : nil
}

/// The panels of a process with the window each belongs to (itself, or the window of a sheet).
func panels(_ pid: Int32) -> [(panel: AXUIElement, window: AXUIElement)] {
    var result: [(panel: AXUIElement, window: AXUIElement)] = []
    for window in attribute(AXUIElementCreateApplication(pid), kAXWindowsAttribute) as? [AXUIElement] ?? [] {
        if isPanel(window) { result.append((window, window)) }
        for child in children(window) where text(child, kAXRoleAttribute) == "AXSheet" && isPanel(child) { result.append((child, window)) }
    }
    return result
}

/// The panels of the process, or else of the panel service (a sandboxed app's panel), waiting up to `seconds` for one.
func findPanels(_ pid: Int32, _ seconds: Double) -> (pid: Int32, found: [(panel: AXUIElement, window: AXUIElement)]) {
    let services = { NSWorkspace.shared.runningApplications.filter { $0.bundleIdentifier?.hasPrefix(panelServicePrefix) == true }.map(\.processIdentifier) }
    let found = waitFor(seconds) { () -> (Int32, [(panel: AXUIElement, window: AXUIElement)])? in
        for candidate in [pid] + services() {
            let list = panels(candidate)
            if !list.isEmpty { return (candidate, list) }
        }
        return nil
    }
    return found.map { (pid: $0.0, found: $0.1) } ?? (pid: pid, found: [])
}

/// The visible texts of a panel outside its file browser (the message), at most 20.
func panelTexts(_ element: AXUIElement, _ depth: Int = 0) -> [String] {
    let role = text(element, kAXRoleAttribute)
    if depth > 0 && ["AXBrowser", kAXOutlineRole as String, kAXTableRole as String, kAXListRole as String].contains(role) { return [] }
    var texts = role == (kAXStaticTextRole as String) ? [text(element, kAXValueAttribute)].filter { !$0.isEmpty } : []
    if depth < 8 { for child in children(element) where texts.count < 20 { texts += panelTexts(child, depth + 1) } }
    return Array(texts.prefix(20))
}

/// The items of the folder the file browser shows: name (AXFilename where AX has it), role, enabled (the first AXEnabled
/// in the row, where AX has it) and alpha (the opacity of the name's text colour, where AX has it). NSOpenPanel reports
/// AXEnabled true also for the items its type filter dims (T-243, macOS 27); the dimmed name has a low alpha (0.25
/// against 0.85 for an item that can be chosen), so the check judges the filter by the alpha. The column view shows the
/// folder in its rightmost list; a list or outline view in rows; an icon view in the children of its list.
func panelItems(_ browser: AXUIElement) -> [[String: Any]] {
    var rows: [AXUIElement] = []
    if text(browser, kAXRoleAttribute) == "AXBrowser" {
        var lists: [AXUIElement] = []
        func collect(_ element: AXUIElement, _ depth: Int) {
            guard depth < 6 else { return }
            if text(element, kAXRoleAttribute) == (kAXListRole as String) { lists.append(element); return }
            for child in children(element) { collect(child, depth + 1) }
        }
        collect(browser, 0)
        if let column = lists.max(by: { frame($0)["x", default: 0] < frame($1)["x", default: 0] }) { rows = Array(children(column).prefix(200)) }
    } else {
        func collect(_ element: AXUIElement, _ depth: Int) {
            guard rows.count < 200, depth < 10 else { return }
            if text(element, kAXRoleAttribute) == (kAXRowRole as String) { rows.append(element); return }
            for child in children(element) { collect(child, depth + 1) }
        }
        collect(browser, 0)
        if rows.isEmpty { rows = Array(children(browser).prefix(200)) }
    }
    func enabled(_ element: AXUIElement, _ depth: Int) -> Bool? {
        if let value = attribute(element, kAXEnabledAttribute) as? NSNumber { return value.boolValue }
        guard depth < 4 else { return nil }
        for child in children(element) { if let found = enabled(child, depth + 1) { return found } }
        return nil
    }
    // The element with the file name: the one with AXFilename, else the first text field or static text with a value.
    func nameElement(_ element: AXUIElement, _ depth: Int) -> AXUIElement? {
        if !text(element, "AXFilename").isEmpty { return element }
        guard depth < 4 else { return nil }
        for child in children(element) { if let found = nameElement(child, depth + 1) { return found } }
        return nil
    }
    func textElement(_ element: AXUIElement, _ depth: Int) -> AXUIElement? {
        if [kAXTextFieldRole as String, kAXStaticTextRole as String].contains(text(element, kAXRoleAttribute)), !text(element, kAXValueAttribute).isEmpty { return element }
        guard depth < 4 else { return nil }
        for child in children(element) { if let found = textElement(child, depth + 1) { return found } }
        return nil
    }
    // The alpha of the foreground colour of the first character (AXAttributedStringForRange), or nil.
    func alpha(_ element: AXUIElement) -> Double? {
        var range = CFRange(location: 0, length: 1)
        guard let parameter = AXValueCreate(.cfRange, &range) else { return nil }
        var value: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(element, kAXAttributedStringForRangeParameterizedAttribute as CFString, parameter, &value) == .success,
              let string = value as? NSAttributedString, string.length > 0,
              let colour = string.attribute(NSAttributedString.Key(kAXForegroundColorTextAttribute.takeUnretainedValue() as String), at: 0, effectiveRange: nil),
              CFGetTypeID(colour as CFTypeRef) == CGColor.typeID else { return nil }
        return Double((colour as! CGColor).alpha)
    }
    return rows.map { row in
        let named = nameElement(row, 0) ?? textElement(row, 0)
        let name = named.map { text($0, "AXFilename") }.flatMap { $0.isEmpty ? nil : $0 } ?? subtreeTexts(row).first ?? ""
        var item: [String: Any] = ["name": name, "role": text(row, kAXRoleAttribute)]
        item["enabled"] = enabled(row, 0) ?? NSNull()
        item["alpha"] = named.flatMap(alpha) ?? NSNull()
        return item
    }
}

func buttonJSON(_ panel: AXUIElement, _ name: String) -> [String: Any] {
    guard let button = panelButton(panel, name) else { return ["title": NSNull(), "enabled": NSNull()] }
    return ["title": text(button, kAXTitleAttribute), "enabled": isEnabled(button)]
}

/// The folder a panel shows: the window's AXDocument (a file URL), else empty; and the title of its path pop-up.
func panelFolder(_ panel: AXUIElement, _ window: AXUIElement) -> (document: String, popup: String) {
    let document = [text(panel, kAXDocumentAttribute), text(window, kAXDocumentAttribute)].first { !$0.isEmpty } ?? ""
    func popup(_ element: AXUIElement, _ depth: Int) -> String? {
        if text(element, kAXRoleAttribute) == (kAXPopUpButtonRole as String) {
            let value = [text(element, kAXValueAttribute), text(element, kAXTitleAttribute)].first { !$0.isEmpty }
            if let value { return value }
        }
        guard depth < 8 else { return nil }
        for child in children(element) { if let found = popup(child, depth + 1) { return found } }
        return nil
    }
    let path = document.isEmpty ? "" : (URL(string: document)?.path ?? document)
    return (path, popup(panel, 0) ?? "")
}

/// JSON of every open panel of the process (or of the panel service): see the usage at the top.
func panelJSON(_ pid: Int32, _ seconds: Double) {
    requireTrusted()
    AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), 1.0)
    let (owner, found) = findPanels(pid, seconds)
    let app = AXUIElementCreateApplication(owner)
    let running = NSRunningApplication(processIdentifier: owner)
    let focusedWindow = attribute(app, kAXFocusedWindowAttribute)
    printJSON(found.map { entry -> [String: Any] in
        let folder = panelFolder(entry.panel, entry.window)
        let browser = fileBrowser(entry.panel)
        return [
            "pid": Int(owner), "process": running?.localizedName ?? "", "bundleIdentifier": running?.bundleIdentifier ?? "",
            "role": text(entry.panel, kAXRoleAttribute), "subrole": text(entry.panel, kAXSubroleAttribute),
            "sheet": text(entry.panel, kAXRoleAttribute) == "AXSheet",
            "title": text(entry.panel, kAXTitleAttribute), "texts": panelTexts(entry.panel),
            "main": (attribute(entry.window, kAXMainAttribute) as? NSNumber)?.boolValue ?? false,
            "focused": focusedWindow.map { CFEqual($0, entry.window) } ?? false,
            "frontmost": running?.isActive ?? false,
            "folder": folder.document, "folderTitle": folder.popup,
            "defaultButton": buttonJSON(entry.panel, kAXDefaultButtonAttribute), "cancelButton": buttonJSON(entry.panel, kAXCancelButtonAttribute),
            "items": browser.map(panelItems) ?? [],
            "frame": frame(entry.panel),
        ]
    })
}

/// Brings the panel's app and window to the front, so that key events reach the panel; whether the app is in front.
func raisePanel(_ owner: Int32, _ window: AXUIElement) -> Bool {
    let app = AXUIElementCreateApplication(owner)
    _ = AXUIElementSetAttributeValue(app, kAXFrontmostAttribute as CFString, kCFBooleanTrue)
    _ = AXUIElementPerformAction(window, kAXRaiseAction as CFString)
    _ = AXUIElementSetAttributeValue(window, kAXMainAttribute as CFString, kCFBooleanTrue)
    return waitFor(2.0) { NSRunningApplication(processIdentifier: owner)?.isActive == true ? true : nil } != nil
}

/// Whether the panel is gone within `seconds`.
func panelCloses(_ owner: Int32, _ panel: AXUIElement, _ seconds: Double) -> Bool {
    waitFor(seconds) { panels(owner).contains { CFEqual($0.panel, panel) } ? nil : true } != nil
}

/// Closes the panel with its cancel button, else Esc; exit code 1 when it stays open.
func cancelPanel(_ owner: Int32, _ entry: (panel: AXUIElement, window: AXUIElement)) {
    if let button = panelButton(entry.panel, kAXCancelButtonAttribute) { _ = AXUIElementPerformAction(button, kAXPressAction as CFString) }
    if panelCloses(owner, entry.panel, 3.0) { return }
    for _ in 0..<3 {
        _ = raisePanel(owner, entry.window)
        key("escape")
        if panelCloses(owner, entry.panel, 1.0) { return }
    }
    fail("the panel is still open after Cancel and Esc")
}

func panelCancel(_ pid: Int32) {
    requireTrusted()
    AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), 1.0)
    let (owner, found) = findPanels(pid, 0)
    for entry in found { cancelPanel(owner, entry) }
    print(found.isEmpty ? "no panel" : "cancelled \(found.count)")
}

/// Chooses `path` in the open panel: Cmd-Shift-G, the path typed, Return, then the default button (Open). Exit code 1: no
/// panel within `seconds`, 2: the Go to field did not open or the panel stayed open, 4: the default button stays
/// disabled (the file cannot be chosen). On every failure the panel is cancelled.
func panelGo(_ pid: Int32, _ path: String, _ seconds: Double) {
    requireTrusted()
    AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), 1.0)
    let (owner, found) = findPanels(pid, seconds)
    guard let entry = found.first else { fail("no open panel of process \(pid) within \(seconds) s") }
    let app = AXUIElementCreateApplication(owner)
    func give(up message: String, code: Int32) -> Never {
        cancelPanel(owner, entry)
        fail(message, code: code)
    }
    guard raisePanel(owner, entry.window) else { give(up: "the app of the panel (pid \(owner)) did not come to the front", code: 2) }
    let focusedField = { () -> AXUIElement? in
        guard let focused = attribute(app, kAXFocusedUIElementAttribute) else { return nil }
        let element = focused as! AXUIElement
        return [kAXTextFieldRole as String, kAXComboBoxRole as String].contains(text(element, kAXRoleAttribute)) ? element : nil
    }
    // The search field of the panel may have the focus before: the Go to field is another one.
    let before = focusedField()
    key("cmd+shift+g")
    guard let field = waitFor(3.0, { () -> AXUIElement? in
        guard let field = focusedField(), before.map({ !CFEqual($0, field) }) ?? true else { return nil }
        return field
    }) else { give(up: "Cmd-Shift-G opened no Go to field within 3 s", code: 2) }
    key("cmd+a")
    typeText(path)
    usleep(300_000)
    let typed = text(field, kAXValueAttribute)
    key("return")
    guard let button = panelButton(entry.panel, kAXDefaultButtonAttribute) else { give(up: "the panel has no default button", code: 2) }
    guard waitFor(3.0, { isEnabled(button) ? true : nil }) != nil else {
        give(up: "the default button '\(text(button, kAXTitleAttribute))' stays disabled after Go to '\(path)' (field: '\(typed)')", code: 4)
    }
    let title = text(button, kAXTitleAttribute)
    AXUIElementSetMessagingTimeout(button, 1.0)
    let status = AXUIElementPerformAction(button, kAXPressAction as CFString)
    if status != .success && status != .cannotComplete { give(up: "AXPress on '\(title)' failed: \(status.rawValue)", code: 2) }
    guard panelCloses(owner, entry.panel, 5.0) else { give(up: "the panel stayed open after '\(title)' (Go to '\(path)', field: '\(typed)')", code: 2) }
    printJSON(["pid": Int(owner), "path": path, "field": typed, "button": title])
}

// ---------------------------------------------------------------------------------------------------------------------
// Windows, the Dock and live captures (T-215)
// ---------------------------------------------------------------------------------------------------------------------

/// The window of a process with that exact title, or its main window (else the first one) for "*".
func axWindow(_ app: AXUIElement, _ title: String) -> AXUIElement? {
    let windows = attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []
    if title == "*" { return attribute(app, kAXMainWindowAttribute).map { $0 as! AXUIElement } ?? windows.first }
    return windows.first { text($0, kAXTitleAttribute) == title }
}

/// minimize / restore (AXMinimized), raise (AXRaise), fullscreen (the green button: AXFullScreenButton, else the zoom
/// button) or close (the close button) on a window of a process; exit code 1 when there is no such window or it fails.
func axWindowAction(_ pid: Int32, _ title: String, _ action: String) {
    requireTrusted()
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 1.0)
    guard let window = axWindow(app, title) else { fail("no window '\(title)' of process \(pid)") }
    AXUIElementSetMessagingTimeout(window, 1.0)
    var status: AXError
    switch action {
    case "minimize", "restore":
        status = AXUIElementSetAttributeValue(window, kAXMinimizedAttribute as CFString, (action == "minimize" ? kCFBooleanTrue : kCFBooleanFalse) as CFTypeRef)
    case "raise":
        status = AXUIElementPerformAction(window, kAXRaiseAction as CFString)
    case "fullscreen", "close":
        let names = action == "close" ? [kAXCloseButtonAttribute] : ["AXFullScreenButton", kAXZoomButtonAttribute]
        guard let button = names.lazy.compactMap({ attribute(window, $0) }).first else { fail("the window '\(title)' has no \(action) button") }
        AXUIElementSetMessagingTimeout(button as! AXUIElement, 1.0)
        status = AXUIElementPerformAction(button as! AXUIElement, kAXPressAction as CFString)
        // A button whose action runs an animation may answer late: the press itself was delivered.
        if status == .cannotComplete { status = .success }
    default:
        fail("unknown window action '\(action)' (minimize, restore, raise, fullscreen, close)")
    }
    if status != .success { fail("\(action) of the window '\(title)' failed: \(status.rawValue)") }
}

/// The menu bar the system shows now: the pid of the focused (active) application and the titles and frames of the top
/// items of its AXMenuBar (T-102 question 3: is the menu of the app back after another app was in front?).
func focusedMenuBar() {
    requireTrusted()
    let system = AXUIElementCreateSystemWide()
    AXUIElementSetMessagingTimeout(system, 1.0)
    // The front app of the window server when the system-wide element names none (macOS 27 with a Screen Sharing display,
    // first run with an unlocked screen 2026-11-15: AXFocusedApplication was missing even with an app in front).
    var pid: pid_t = 0
    let app: AXUIElement
    if let focused = attribute(system, kAXFocusedApplicationAttribute) {
        app = focused as! AXUIElement
        _ = AXUIElementGetPid(app, &pid)
    } else if let front = NSWorkspace.shared.frontmostApplication {
        pid = front.processIdentifier
        app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 1.0)
    } else {
        printJSON(["pid": 0, "items": []])
        return
    }
    let items = attribute(app, kAXMenuBarAttribute).map { children($0 as! AXUIElement) } ?? []
    printJSON(["pid": Int(pid), "items": items.map { ["title": text($0, kAXTitleAttribute), "frame": frame($0)] as [String: Any] }])
}

/// The Dock item of an app: by the path of its bundle (AXURL) when <app> starts with "/", else by its exact title.
func dockItem(_ wanted: String) -> AXUIElement? {
    guard let dock = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.dock").first else { fail("the Dock is not running") }
    let app = AXUIElementCreateApplication(dock.processIdentifier)
    AXUIElementSetMessagingTimeout(app, 1.0)
    let path = wanted.hasPrefix("/") ? URL(fileURLWithPath: wanted).resolvingSymlinksInPath().standardizedFileURL.path : nil
    for list in children(app) {
        for item in children(list) where text(item, kAXRoleAttribute) == "AXDockItem" {
            if let path {
                guard let url = attribute(item, kAXURLAttribute) as? URL else { continue }
                if url.resolvingSymlinksInPath().standardizedFileURL.path == path { return item }
            } else if text(item, kAXTitleAttribute) == wanted {
                return item
            }
        }
    }
    return nil
}

/// The open menu of a Dock item (AXShowMenu), or nil.
func dockMenu(_ item: AXUIElement) -> AXUIElement? {
    let open = { () -> AXUIElement? in children(item).first { text($0, kAXRoleAttribute) == kAXMenuRole && !children($0).isEmpty } }
    if let menu = open() { return menu }
    AXUIElementSetMessagingTimeout(item, 0.5)
    let status = AXUIElementPerformAction(item, kAXShowMenuAction as CFString)
    guard status == .success || status == .cannotComplete else { return nil }
    return waitFor(menuLevelTimeout, open)
}

func closeDockMenu(_ item: AXUIElement, _ menu: AXUIElement) {
    _ = AXUIElementPerformAction(menu, kAXCancelAction as CFString)
    let gone = { waitFor(0.5) { children(item).contains { text($0, kAXRoleAttribute) == kAXMenuRole } ? nil : true } != nil }
    for _ in 0..<3 where !gone() { key("escape") }
}

/// dock <app> info | menu | press | menupress <title>: the Dock item of an app (title, subrole, url, frame, running), the
/// items of its Dock menu (opened with AXShowMenu, read, closed: the open windows of the app are items of it), AXPress on
/// the item (what a click does), or AXPress on the item of its menu with that exact title (e.g. a window). Exit code 1
/// when there is no such item, menu or menu item (the titles of the menu on stderr), 2 when the press fails.
func dockCommand(_ wanted: String, _ action: String, _ title: String?) {
    requireTrusted()
    guard let item = dockItem(wanted) else { fail("no Dock item '\(wanted)'") }
    switch action {
    case "info":
        let url = (attribute(item, kAXURLAttribute) as? URL)?.path ?? ""
        printJSON(["title": text(item, kAXTitleAttribute), "subrole": text(item, kAXSubroleAttribute), "url": url, "frame": frame(item),
                   "running": (attribute(item, "AXIsApplicationRunning") as? NSNumber)?.boolValue ?? false])
    case "press":
        AXUIElementSetMessagingTimeout(item, 1.0)
        let status = AXUIElementPerformAction(item, kAXPressAction as CFString)
        if status != .success && status != .cannotComplete { fail("AXPress on the Dock item failed: \(status.rawValue)", code: 2) }
    case "menu", "menupress":
        guard let menu = dockMenu(item) else { fail("the Dock menu of '\(wanted)' did not open within \(menuLevelTimeout) s") }
        let items = children(menu).filter { text($0, kAXRoleAttribute) == kAXMenuItemRole }
        if action == "menu" {
            let entries = items.map { ["title": text($0, kAXTitleAttribute), "enabled": isEnabled($0), "mark": text($0, "AXMenuItemMarkChar")] as [String: Any] }
            closeDockMenu(item, menu)
            printJSON(entries)
            return
        }
        guard let title, let entry = items.first(where: { sameTitle(text($0, kAXTitleAttribute)) == sameTitle(title) }) else {
            let available = items.map { "'\(text($0, kAXTitleAttribute))'" }.joined(separator: ", ")
            closeDockMenu(item, menu)
            fail("no item '\(title ?? "")' in the Dock menu; its items: \(available)")
        }
        AXUIElementSetMessagingTimeout(entry, 0.5)
        let status = AXUIElementPerformAction(entry, kAXPressAction as CFString)
        if status != .success && status != .cannotComplete { closeDockMenu(item, menu); fail("AXPress on '\(title)' failed: \(status.rawValue)", code: 2) }
        _ = waitFor(1.0) { children(item).contains { text($0, kAXRoleAttribute) == kAXMenuRole } ? nil : true }
    default:
        fail("unknown Dock action '\(action)' (info, menu, press, menupress)")
    }
}

/// A left mouse drag from one point to another (global, top left origin) in even steps over <ms> milliseconds, as a hand
/// drags a grip or a window (needs Accessibility).
func drag(_ x1: Double, _ y1: Double, _ x2: Double, _ y2: Double, _ milliseconds: Int) {
    requireTrusted()
    let source = CGEventSource(stateID: .hidSystemState)
    func post(_ type: CGEventType, _ point: CGPoint) {
        guard let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: .left) else { fail("no mouse event") }
        event.post(tap: .cghidEventTap)
    }
    let start = CGPoint(x: x1, y: y1), end = CGPoint(x: x2, y: y2)
    post(.mouseMoved, start)
    usleep(80_000)
    post(.leftMouseDown, start)
    usleep(80_000)
    let steps = max(10, milliseconds / 16)
    for step in 1...steps {
        let t = Double(step) / Double(steps)
        post(.leftMouseDragged, CGPoint(x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t))
        usleep(useconds_t(max(1, milliseconds * 1000 / steps)))
    }
    usleep(80_000)
    post(.leftMouseUp, end)
    usleep(80_000)
}

/// The mean luminance (0-255) and its deviation of a part of a capture: <left> <top> <right> <bottom> as shares (0 to 1)
/// of the image, default the whole image (a black camera image while the window resizes or switches its mode, T-184).
func regionLuminance(_ path: String, _ region: [Double]) -> [String: Any]? {
    guard let image = NSImage(contentsOfFile: path), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
    let (left, top, right, bottom) = region.count == 4 ? (region[0], region[1], region[2], region[3]) : (0, 0, 1, 1)
    let width = Double(cg.width), height = Double(cg.height)
    let rect = CGRect(x: (left * width).rounded(), y: (top * height).rounded(), width: max(1, ((right - left) * width).rounded()), height: max(1, ((bottom - top) * height).rounded()))
    guard let part = cg.cropping(to: rect) else { return nil }
    let (mean, deviation) = luminance(part)
    return ["mean": mean, "deviation": deviation, "width": cg.width, "height": cg.height]
}

func luma(_ path: String, _ region: [Double]) {
    guard let result = regionLuminance(path, region) else { fail("not an image or not a region of it: \(path)") }
    printJSON(result)
}

/// Captures one window (screencapture -l, needs Screen Recording) every <ms> for <seconds> into <folder> and prints a
/// JSON line per capture: ms since the start, file, mean and deviation of the luminance of the middle of the image (20 to
/// 80 % of each side: no title bar, window buttons or status strip), or the error when the capture failed (the window off
/// screen for a moment). Runs until the time is over or it is killed; every line is flushed at once.
func lumaWatch(_ window: Int, _ milliseconds: Int, _ seconds: Double, _ folder: String) {
    try? FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
    let start = Date()
    var index = 0
    while Date().timeIntervalSince(start) < seconds {
        let began = Date()
        index += 1
        let file = (folder as NSString).appendingPathComponent(String(format: "capture-%04d.png", index))
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        process.arguments = ["-x", "-o", "-l", "\(window)", file]
        let errors = Pipe()
        process.standardError = errors
        process.standardOutput = FileHandle.nullDevice
        var line: [String: Any] = ["ms": Int(began.timeIntervalSince(start) * 1000), "file": (file as NSString).lastPathComponent]
        do {
            try process.run()
            let message = String(decoding: errors.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
            process.waitUntilExit()
            if process.terminationStatus == 0, let result = regionLuminance(file, [0.2, 0.2, 0.8, 0.8]) { line.merge(result) { $1 } }
            else { line["error"] = "screencapture exit code \(process.terminationStatus)\(message.isEmpty ? "" : ": \(message)")" }
        } catch { line["error"] = "\(error)" }
        if let data = try? JSONSerialization.data(withJSONObject: line, options: [.sortedKeys]), let text = String(data: data, encoding: .utf8) {
            print(text)
            fflush(stdout)
        }
        let rest = Double(milliseconds) / 1000 - Date().timeIntervalSince(began)
        if rest > 0 { usleep(useconds_t(rest * 1_000_000)) }
    }
}

// Keyboard input sources (T-216, mac.keys-czech-layout): Text Input Sources of HIToolbox.
func inputSourceID(_ source: TISInputSource) -> String {
    guard let pointer = TISGetInputSourceProperty(source, kTISPropertyInputSourceID) else { return "" }
    return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
}

func inputSourceEnabled(_ source: TISInputSource) -> Bool {
    guard let pointer = TISGetInputSourceProperty(source, kTISPropertyInputSourceIsEnabled) else { return false }
    return CFBooleanGetValue(Unmanaged<CFBoolean>.fromOpaque(pointer).takeUnretainedValue())
}

func inputSources(_ properties: [CFString: Any], includeAllInstalled: Bool) -> [TISInputSource] {
    (TISCreateInputSourceList(properties as CFDictionary, includeAllInstalled)?.takeRetainedValue() as? [TISInputSource]) ?? []
}

func currentKeyboardSourceID() -> String {
    guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue() else { return "" }
    return inputSourceID(source)
}

func inputSourceInfo() {
    let enabled = inputSources([kTISPropertyInputSourceCategory: kTISCategoryKeyboardInputSource as Any], includeAllInstalled: false).map(inputSourceID)
    printJSON(["current": currentKeyboardSourceID(), "enabled": enabled])
}

func useInputSource(_ id: String, _ seconds: Double) {
    let previous = currentKeyboardSourceID()
    guard let source = inputSources([kTISPropertyInputSourceID: id as CFString], includeAllInstalled: true).first else { fail("no input source \(id)") }
    var enabledHere = false
    if !inputSourceEnabled(source) {
        let enabled = TISEnableInputSource(source)
        if enabled != noErr { fail("the input source \(id) could not be enabled (\(enabled))", code: 2) }
        enabledHere = true
    }
    // Always back to the previous source (and the source disabled again when this command enabled it), whatever ends it.
    var restored = false
    func restore() {
        if restored { return }
        restored = true
        if !previous.isEmpty, let back = inputSources([kTISPropertyInputSourceID: previous as CFString], includeAllInstalled: false).first { _ = TISSelectInputSource(back) }
        if enabledHere { _ = TISDisableInputSource(source) }
    }
    let selected = TISSelectInputSource(source)
    if selected != noErr {
        restore()
        fail("the input source \(id) could not be selected (\(selected))", code: 2)
    }
    // One compact JSON line each: the script reads the first while the command still runs.
    func line(_ value: [String: Any]) {
        if let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]), let text = String(data: data, encoding: .utf8) { print(text) }
        fflush(stdout)
    }
    line(["previous": previous, "selected": currentKeyboardSourceID(), "enabledHere": enabledHere])
    func finish() {
        restore()
        line(["restored": previous, "current": currentKeyboardSourceID()])
        exit(0)
    }
    var signals: [DispatchSourceSignal] = []
    for number in [SIGTERM, SIGINT, SIGHUP] {
        signal(number, SIG_IGN)
        let watch = DispatchSource.makeSignalSource(signal: number, queue: .main)
        watch.setEventHandler { finish() }
        watch.resume()
        signals.append(watch)
    }
    DispatchQueue.main.asyncAfter(deadline: .now() + max(seconds, 1)) { finish() }
    withExtendedLifetime(signals) { dispatchMain() }
}

func runningApps(_ bundleIdentifier: String) {
    let wanted = bundleIdentifier.lowercased()
    printJSON(NSWorkspace.shared.runningApplications.filter { $0.bundleIdentifier?.lowercased() == wanted }.map { Int($0.processIdentifier) })
}

func terminateApp(_ pid: Int32) {
    guard let app = NSRunningApplication(processIdentifier: pid) else { print("not running"); return }
    _ = app.terminate()
    let deadline = Date().addingTimeInterval(10)
    while !app.isTerminated && Date() < deadline { RunLoop.current.run(until: Date().addingTimeInterval(0.1)) }
    if !app.isTerminated { fail("the app \(pid) is still running after 10 s") }
    print("terminated")
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard let command = arguments.first else { fail("usage: macos-verify-helper screens|windows|menubar|session|statusitems|menu|menupress|trusted|key|type|panel|panelgo|panelcancel|click|axwin|dock|drag|luma|lumawatch|inputsource|apps|terminate|decode|dlopen|localized|icons ...") }
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
case "type" where arguments.count == 2: typeText(arguments[1])
case "panel" where arguments.count == 2 || arguments.count == 3:
    guard let pid = Int32(arguments[1]), let seconds = arguments.count == 3 ? Double(arguments[2]) : 0 else { fail("not a pid or a time") }
    panelJSON(pid, seconds)
case "panelgo" where arguments.count == 3 || arguments.count == 4:
    guard let pid = Int32(arguments[1]), let seconds = arguments.count == 4 ? Double(arguments[3]) : 5 else { fail("not a pid or a time") }
    panelGo(pid, arguments[2], seconds)
case "panelcancel" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; panelCancel(pid)
case "click" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; click(x, y)
case "rightclick" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; mouse(x, y, [.mouseMoved, .rightMouseDown, .rightMouseUp], .right)
case "ctrlclick" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; controlClick(x, y)
case "move" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; mouse(x, y, [.mouseMoved], .left)
case "ax" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axTree(pid)
case "axwindows" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axWindows(pid)
case "axclose" where arguments.count == 3: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axClose(pid, arguments[2])
case "ncpress" where arguments.count == 3 || arguments.count == 4:
    guard let seconds = arguments.count == 4 ? Double(arguments[3]) : 10 else { fail("not a number of seconds") }
    notificationPress(arguments[1], arguments[2], seconds)
case "axpress" where arguments.count == 3: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axPress(pid, arguments[2])
case "focusedid" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; focusedIdentifier(pid)
case "axwatch" where arguments.count == 4 || arguments.count == 5:
    guard let pid = Int32(arguments[1]), let milliseconds = Int(arguments[3]) else { fail("not a pid or a time") }
    axWatch(pid, arguments[2], milliseconds, arguments.count == 5 ? arguments[4] : nil)
case "activate" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; activateApp(pid)
case "windowsat" where arguments.count == 3: guard let x = Double(arguments[1]), let y = Double(arguments[2]) else { fail("not a point") }; windowsAt(x, y)
case "front" where arguments.count == 4 || (arguments.count == 5 && arguments[4] == "regular"):
    guard let x = Double(arguments[1]), let y = Double(arguments[2]), let seconds = Double(arguments[3]) else { fail("not a point or a time") }
    frontApp(x, y, seconds, regular: arguments.count == 5)
case "regiondiff" where arguments.count == 8:
    let numbers = arguments[3...].compactMap(Double.init)
    guard numbers.count == 5 else { fail("not a number") }
    regionDiff(arguments[1], arguments[2], numbers[0], numbers[1], numbers[2], numbers[3], numbers[4])
case "screenrecording": print(CGPreflightScreenCaptureAccess() ? "true" : "false")
case "ring" where arguments.count == 4: guard let offset = Int(arguments[2]), let thickness = Int(arguments[3]) else { fail("not a number") }; ring(arguments[1], offset, thickness)
case "decode" where arguments.count == 2: decode(arguments[1])
case "dlopen" where arguments.count >= 2: load(Array(arguments[1...]))
case "localized" where arguments.count >= 4: localized(arguments[1], arguments[2], Array(arguments[3...]))
case "icons" where arguments.count >= 4: icons(arguments[1], arguments[2], arguments[3], Array(arguments[4...]))
case "axwin" where arguments.count == 4: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; axWindowAction(pid, arguments[2], arguments[3])
case "focusedmenubar": focusedMenuBar()
case "dock" where arguments.count == 3 || arguments.count == 4: dockCommand(arguments[1], arguments[2], arguments.count == 4 ? arguments[3] : nil)
case "drag" where arguments.count == 5 || arguments.count == 6:
    let numbers = arguments[1...4].compactMap(Double.init)
    guard numbers.count == 4, let milliseconds = arguments.count == 6 ? Int(arguments[5]) : 600 else { fail("not a point or a time") }
    drag(numbers[0], numbers[1], numbers[2], numbers[3], milliseconds)
case "luma" where arguments.count == 2 || arguments.count == 6:
    let region = arguments.dropFirst(2).compactMap(Double.init)
    guard region.count == arguments.count - 2 else { fail("not a region") }
    luma(arguments[1], region)
case "lumawatch" where arguments.count == 5:
    guard let window = Int(arguments[1]), let milliseconds = Int(arguments[2]), let seconds = Double(arguments[3]) else { fail("not a window number or a time") }
    lumaWatch(window, milliseconds, seconds, arguments[4])
case "inputsource" where arguments.count == 1: inputSourceInfo()
case "inputsource" where arguments.count == 3: guard let seconds = Double(arguments[2]) else { fail("not a number of seconds") }; useInputSource(arguments[1], seconds)
case "apps" where arguments.count == 2: runningApps(arguments[1])
case "terminate" where arguments.count == 2: guard let pid = Int32(arguments[1]) else { fail("not a pid") }; terminateApp(pid)
default: fail("unknown command or arguments: \(arguments.joined(separator: " "))")
}
