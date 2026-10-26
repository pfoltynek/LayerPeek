# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0.0, minor versions may contain breaking changes
(settings format, behaviour).

## [Unreleased]

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

[Unreleased]: https://github.com/pfoltynek/LayerPeek/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/pfoltynek/LayerPeek/releases/tag/v0.1.0
