# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0.0, minor versions may contain breaking changes
(settings format, behaviour).

## [Unreleased]

## [0.3.1] - unreleased

> **macOS 0.3.0:** that version cannot update itself: drag the new LayerPeek.app from the 0.3.1 disk image over the old
> one once (settings and saved passwords stay). From 0.3.1 on, LayerPeek updates itself.

### Added

- **Update check** (Windows and macOS): shortly after the start and then once a day LayerPeek asks GitHub whether a new
  version was released (anonymously, one request a day). A newer version appears as **New version X.Y.Z available** in
  the tray / menu bar menu (above *About*) and once per version as a notification. A click does the update: the
  installed Windows version and LayerPeek.app on macOS download it, quit normally and start the new version; the ZIP
  version and development builds open the release page. The *Startup and updates* tab has **Check for updates
  automatically** (on by default), **Offer pre-release versions** (now also in the ZIP version and on macOS) and **Check
  now** with its result next to it. A LayerPeek.app that runs straight from the disk image or Downloads is told to move
  to Applications first. Failures (no network, the GitHub limit) are only logged. **Settings:** `checkForUpdates`
  (older files load with the check on); what the check remembers is in `update-state.json` next to the settings.

- macOS: keyboard shortcuts as on other Mac apps. Camera window: **⌃⌘F** toggles full screen (F11, which macOS
  reserves for Show Desktop and Mac keyboards send only with fn, stays as a fallback), and the full screen button and
  the image description name ⌃⌘F. Settings: **⌘⇧]** / **⌘⇧[** switch the tabs, **⌘⌥→** / **⌘⌥←** the tabs of a
  printer (Ctrl+Tab and Ctrl+PageDown still work). Every auxiliary window (Settings, Diagnostics, About, licences,
  camera) closes with **⌘W**, and **⌘.** works like Esc (in Settings both are Cancel). Windows is unchanged.
- **Send test notification** on the *General* tab of the settings (Windows and macOS): shows a sample notification
  the way printer events are shown, with the sound unless *Sound* is set to Off (the choice shown in the window counts,
  even unsaved), also while *Show notifications* is unchecked. A new test notification replaces the previous one, and a
  click on it brings the settings window to the front on the *General* tab. The result is shown next to the button:
  sent (with a hint to check Focus / Do not disturb if nothing appears) or failed. On macOS, when LayerPeek has not
  asked yet, the button asks for permission first (macOS asks only once; after a denial the tab offers System Settings
  → Notifications).
- Windows: when LayerPeek's notifications are turned off in Settings → System → Notifications, the *General* tab says
  so and offers to open that page (checked every time the settings window opens or comes back to the front, as on
  macOS); when a policy of the organization turned them off, the tab says that instead.
- macOS: while a window is open (Settings, Diagnostics, About, licences, a camera window that is not always on top),
  LayerPeek has a Dock icon and is in the ⌘Tab switcher, so a window that fell behind other apps can be brought back
  (clicking the Dock icon, or the window list of its menu); with only always-on-top windows (the mini preview by
  default) or no window it is back in the menu bar only.
- The menu of a printer with an open camera window (menu bar on macOS, notification area on Windows) has **Show
  camera**, which brings the window to the front, restoring it when minimized; also a window that is always on top and
  so has no Dock icon or taskbar button.
- Mini preview: the strip below the image shows when the print finishes, after a flag next to the remaining time:
  "Done at 7:50 PM" when there is room, otherwise only "7:50 PM" (on another day of the week "Fri 7:50 PM"), and
  in the narrowest window only in the tooltip. The time is never cut off. Only while printing (not while paused).
- Mini preview: after a print the bar on the top edge of the strip shows how far the nozzle has cooled down (blue),
  and once it has cooled it is full and green and the strip says "Cooled down – safe to turn off" like the Cool-down
  widget (in a narrow window "Cooled down · 48 °C"; the tooltip always has both). While cooling, a narrow strip drops
  the word "Cooling down…" (the snowflake icon stands for it) before the estimate.
- Mini preview: choose what its info strip shows, per printer, under **Mini preview** at the end of the list on the
  *Widgets* tab of the settings: the progress bar at the top, the percentage, the time remaining, the finish time, the
  cool-down after printing, and the print name and temperature chart rows (all on by default, as before). The printer
  state is always shown. A row that is switched off has no button in the strip, which leaves more room for the rest
  (e.g. without the remaining time the finish time fits even the narrowest window); the tooltip names only what is on.
  An open mini preview changes as soon as the settings are saved. *Restore defaults* and *Use layout for all printers*
  include these choices. **Settings:** stored as `printers[*].camera.miniStrip` (only the items that are off); older
  settings files load unchanged.

### Changed

- Settings: a field with an invalid value now has a red outline (2 px, also in the dark theme), so it is clear which
  field "Fix the highlighted fields" means, e.g. the nozzle or the bed threshold of the *Cool-down* widget, which share
  one message. After **Save** (and **Apply** of the camera window, and **Test connection** / **Test camera** refused
  because of a field) the keyboard focus moves to the first invalid field, and screen readers (Narrator, NVDA,
  VoiceOver) read its error as the help text of the field; a valid field gives its hint. The fields keep their size,
  so the window still fits without scrolling.
- macOS: LayerPeek.app is packed and signed by Velopack (still ad hoc, with the hardened runtime and the entitlements of
  0.3.0), so that it can update itself; the release also carries the Velopack package and update feed of `osx-arm64`.
  The `.dmg` and `.tar.gz` stay; there is no `.pkg`.
- Cool-down: a printer now counts as cooled down only when **both the nozzle and the bed** are at or below their
  thresholds (before, only the nozzle decided). Each printer has a nozzle and a bed threshold, 40 °C each by default,
  set in the *Cool-down* widget on the *Widgets* tab of the settings for the printer selected there, with **Use
  thresholds for all printers**. The estimate ("about 6 min") and the progress bar follow the slower of the two. The
  *Cool-down* widget names both thresholds, the "Printer cooled down" notification shows both temperatures, and the mini
  preview strip shows the temperature that is still holding it up (both in its tooltip). A heater switched on again,
  also the bed's, ends the cool-down. **Settings:** the former nozzle threshold is not taken over; every printer starts
  with 40 °C / 40 °C (the log notes it once; older settings files still load).
- Cool-down widget, small (S) size: while cooling, the temperature has the icon of its sensor in front of it (the
  nozzle cone or a heated bed with heat waves, in their colours), so it is clear whether the nozzle or the bed is
  holding the cool-down up.
- Windows: a camera window has a taskbar button exactly when it is not always on top. With the default options nothing
  changes (the mini preview is on top and has none); a normal window with "Always on top" now has no taskbar button, a
  mini preview without it now has one. Changing "Always on top" of an open window may make it disappear and reappear
  once (how Avalonia moves a window off the taskbar).
- The small temperature charts in the camera window (the *Temperatures* widget and the chart row of the mini preview)
  no longer look like flat lines: the nozzle and the bed each get their own band (nozzle on top, bed below), scaled to
  their own values over the last 60 minutes with at least 6 °C, so a fluctuation of 1–2 °C is visible while sensor
  noise is not. A target line is drawn only where it falls within its band. In the large (L) *Temperatures* widget
  each band shows its range next to the chart (e.g. "212–218 °C"). The chart in the Diagnostics window keeps its axis
  from 0 °C. Windows and macOS.
- Update check of the ZIP version and of LayerPeek.app outside Applications: a release is offered only when it already
  has the files of the operating system LayerPeek runs on (its `SHA256SUMS-windows.txt` / `SHA256SUMS-macos.txt`), so
  the Windows ZIP version is no longer told about a version that so far has only macOS files, and the other way round.
  The installed versions (Velopack) already skipped such a release.
- Releases: the Windows and macOS files of one version can be published at different times. The OS that goes first
  publishes with `scripts/release-publish.ps1 -AllowIncomplete`; the other OS later adds its files to the published
  release with `scripts/release-upload.ps1 -AddToPublished`, which never replaces a file, uploads the checksum file
  and the update feed last and then checks the whole release. `scripts/release.ps1` runs on macOS as well as on
  Windows. `release-publish.ps1 -AllowIncomplete` no longer fails with "Cannot bind argument to parameter
  'ExpectedOs'".

### Security

- The PrusaLink web proxy (the printer's web opened logged in from the menu) no longer lets the printer's content
  register a Service Worker, which could have outlived the proxy on its `127.0.0.1` port: such requests are refused
  and the proxy's policy forbids workers. Only the response headers the PrusaLink web needs are passed from the printer
  to the browser (content type and encoding, download name, caching, file information); everything else, e.g.
  `Service-Worker-Allowed`, `Access-Control-*`, `Link` or `Refresh`, is dropped. Other pages can no longer frame the
  proxied web or keep a handle on its window (`X-Frame-Options`, `frame-ancestors`, `Cross-Origin-Opener-Policy`).

### Security

- macOS: LayerPeek.app is signed with the hardened runtime, so macOS ignores `DYLD_INSERT_LIBRARIES` and the other
  `DYLD_*` variables when it starts LayerPeek. Before, another program running as the same user could start LayerPeek with
  its own library injected and read the printer passwords from the Keychain without a prompt. The only entitlement is
  `disable-library-validation`, which the ad-hoc signed app needs to load its own bundled libraries. After updating to
  this version, macOS may ask once more to allow LayerPeek access to its Keychain items.

## [0.3.0] - 2026-10-29

### Added

- macOS (Apple Silicon): `LayerPeek.app` without a Dock icon (menu bar only), ad-hoc signed, distributed as a `.dmg`
  (drag the app to Applications) and a `.tar.gz`. The builds are not notarized: the first start has to be allowed in
  **System Settings → Privacy & Security → Open Anyway** (see the README).
- macOS: **Open at Login** in the settings (tab *Startup and updates*), using the system Login Items. Off by default.
  When LayerPeek was turned off in System Settings → General → Login Items, the tab says so under the option (checked
  every time the settings window opens or comes back to the front) and offers to open System Settings; saving then
  reports that Open at login has to be allowed there.
- macOS: windows opened from the menu bar (Settings, Diagnostics, About, camera) come to the front with the keyboard focus.
- macOS: the app icon follows Apple's app icon grid (a margin around the rounded square with Apple's continuous corners
  and a soft drop shadow), so in Finder, Launchpad and System Settings it is as large as the other apps' icons; the 16
  and 32 px sizes use the simplified design, like on Windows. The Windows icon is unchanged.
- macOS: system notifications (User Notifications) with the same texts, sound setting and click target as on Windows; a
  click opens the printer's web interface. macOS asks for permission with the first notification. When notifications
  are turned off in System Settings, the *General* tab of the settings says so and offers to open System Settings →
  Notifications.
- macOS: the live camera stream (RTSP/H.264, e.g. Buddy3D). The app bundles its own LGPL build of FFmpeg 9.0.2,
  compiled from the official source (software decoding), and needs no FFmpeg from Homebrew. The third-party licences
  window links the source release and the build script used, at the release tag of the app version. Building on a Mac
  for the first time needs the Xcode Command Line Tools (a clear build error says so when they are missing).
- macOS 15+: the Local Network permission dialog explains why LayerPeek connects to printers and cameras on the local
  network, in all eight app languages. When a connection to a printer or camera on the local network fails with
  "no route to host" (what a denied permission looks like), the diagnostics window, the connection and camera tests
  and the camera window say that Local Network access may be turned off in **System Settings → Privacy & Security →
  Local Network**, and the log says so once per printer or camera. The diagnostics window, the connection test and
  the camera test offer an **Open System Settings** button next to that message.
- macOS: when LayerPeek starts while no display is active (display asleep, lid closed without a monitor, Open at Login
  while the display sleeps), it waits and appears in the menu bar once a display wakes, instead of quitting at start.
  Printer monitoring and notifications already run while it waits (a click on a notification opens the printer's web
  interface); the menu bar item then shows the current state at once. Opening the app again while it waits does what
  it always does (the last window or the settings) once the menu bar item is there.
- Maintainers: `build/update-ffmpeg.ps1` verifies a new FFmpeg version for both platforms before it is pinned (macOS:
  official source tarball against the Homebrew formula and the GPG signature; Windows: the BtbN zip against the
  release's `checksums.sha256`, its folder, LGPL licence and libraries) and with `-Update` writes the pinned values.
- macOS: the monitor chosen for a camera window is recognised by the display's persistent identifier, so the window
  still opens on the right one of two identical monitors after macOS numbers them differently ("(1)", "(2)") at the
  next start or reconnect, and on the built-in display after the system language changed.

### Fixed

- macOS: the borderless mini camera preview can be resized with its edges and corners (the window follows the
  pointer itself; macOS offers no resize for borderless windows there). The height follows the width when the edge is
  released, as on Windows.

## [0.2.0] - 2026-10-26

### Added

- Camera overlay widgets instead of a single text block: Status (printer name, state, progress), Times (remaining,
  finish time, print time), Temperatures (nozzle and bed with a small chart), Print name (title and material chips
  read from the PrusaSlicer file name, the G-code thumbnail and the slicer estimate) and Cool-down. Each widget can be
  shown or hidden, has a size S/M/L and can be emphasized.
- Layout mode in the camera window: drag the widgets to one of six anchors, change their size, reset to the default
  layout. Works with the keyboard too.
- New Widgets tab in the settings: visibility and settings of each widget, card background opacity, "Edit layout in
  the camera window…", reset, and applying the layout to all printers.
- Cool-down after a finished or stopped print: the Cool-down widget shows the nozzle and bed temperatures, the
  progress to the threshold and an estimate, then "Cooled down – you can switch it off". An optional "Printer cooled
  down" notification (off by default). The nozzle threshold is set per printer (default 50 °C) and can be applied to
  all printers.
- Mini preview info strip below the image (progress bar, percentage and remaining time) instead of text over the
  image. The print name row and a temperature chart row can be expanded or collapsed right in the mini window and stay
  as you left them.
- The Settings and Diagnostics windows can be maximized and remember their size and maximized state.

### Changed

- The camera overlay settings moved from the printer's Overlay sub-tab to the new Widgets tab. Existing overlay
  settings (items and corner) are converted to the widget layout automatically. The settings file format is now
  version 2. If you go back to LayerPeek 0.1.0, it only logs a warning and drops the widget layout when it saves the
  settings.
- Settings and Diagnostics windows are a little taller by default (780 × 532).
- Numbers and their units (`50 °C`, `2 h 35 min`, `960 × 540`) are never split across lines.

### Fixed

- The mini preview size box offers its own presets (320 × 180, 480 × 270, 640 × 360, 960 × 540). Before, the default
  320 × 180 disappeared from the list once another size was picked.

## [0.1.0] - 2026-10-24

First public preview. Windows x64 only, PrusaLink only, builds are not code-signed.

The project was called *Prusa Tray Diagnostic* (`PrusaTrayDiagnostic`) during development and was renamed to
LayerPeek before this first release, so that the name does not start with the Prusa trademark. No earlier version was
released, so the app does not migrate anything; developers who ran the old builds can use the one-time script
`tools/migrate-from-prusatraydiagnostic.ps1`.

### Added

- Tray-only app for Windows: the tray icon shows the printer state and print progress, and the tooltip shows the
  details.
- Monitoring of multiple printers over PrusaLink, with one combined icon and a submenu for each printer.
- Windows toast notifications for print events (started, finished, paused, resumed, stopped, error, attention,
  offline, minutes remaining), with configurable sounds.
- Diagnostics window with printer and firmware information, temperatures, fans, speed, flow and Z height, and a
  temperature chart for the last 30 minutes, 2 hours or 24 hours.
- Printer picture chosen by the printer model reported by PrusaLink, or a custom image.
- Camera window for RTSP/H.264 cameras (e.g. Buddy3D) through FFmpeg, with automatic reconnection and full-screen,
  normal and mini (always on top) modes.
- Camera overlay with configurable items (name, state, progress, temperatures, remaining time, finish time, print
  time, file name) and corner.
- Camera window settings for each printer: start mode, size, position, monitor and always on top.
- PrusaLink web opened already signed in through a local proxy. The password never reaches the browser.
- Settings window: printers, connection test, camera with a live preview, notifications, polling interval and
  language.
- Passwords stored in the Windows Credential Manager.
- Localization: English, Czech, Slovak, German, Spanish, French, Italian and Polish.
- App icon for the executable, the windows and the tray. On Windows 11 the tray icon is shown in the visible part of
  the taskbar instead of the hidden-icons area.
- File logging with a "Detailed logging (Debug)" setting. Unhandled exceptions are written to the log before the app
  ends.
- About window with the third-party licences and a "Report a problem" group: save a diagnostic bundle (ZIP without
  passwords), copy a short report to the clipboard, open the log folder.
- Single instance: starting the app again doesn't open a second tray icon.
- Native AOT publishing (single executable, no .NET runtime required).
- Windows installer (Velopack): installs for the current user without administrator rights, adds a Start menu
  shortcut, and its uninstall removes the app's notifications, notification settings and system registrations (settings
  and passwords stay). A ZIP is still available.
- "Start automatically when I sign in to Windows" option (installed version only, off by default) and "Offer
  pre-release versions" option in the new Startup and updates tab of the settings.

### Fixed

- Settings, camera window positions and printer images are saved even when another program (antivirus, backup)
  briefly holds the file. A detected printer model that still can't be saved is tried again every minute, up to five
  times.
- The monitor chosen for the camera window is found again after it is plugged into another connector, and its new
  connector is remembered. A switched-off monitor is still not confused with an identical one next to it.

### Security

- FFmpeg accepts only RTSP over TCP with H.264, nothing else.
- The PrusaLink web proxy rejects write requests from other origins.
- Native libraries are loaded only from the application folder and the system folder.

[Unreleased]: https://github.com/pfoltynek/LayerPeek/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/pfoltynek/LayerPeek/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/pfoltynek/LayerPeek/releases/tag/v0.1.0
