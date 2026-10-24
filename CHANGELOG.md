# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0.0, minor versions may contain breaking changes
(settings format, behaviour).

## [Unreleased]

## [0.1.0] - unreleased

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
- "Start after signing in to Windows" option (off by default) and "Offer pre-release versions" option in the new
  Startup and updates tab of the settings.

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

[Unreleased]: https://github.com/pfoltynek/LayerPeek/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/pfoltynek/LayerPeek/releases/tag/v0.1.0
