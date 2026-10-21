# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0.0, minor versions may contain breaking changes
(settings format, behaviour).

## [Unreleased]

## [0.1.0] - unreleased

First public preview. Windows x64 only, PrusaLink only, builds are not code-signed and there is no installer yet.

### Added

- Tray-only app for Windows: the tray icon shows the printer state and print progress, and the tooltip shows the
  details.
- Monitoring of multiple printers over PrusaLink, with one combined icon and a submenu for each printer.
- Windows toast notifications for print events (started, finished, paused, resumed, stopped, error, attention,
  offline, minutes remaining), with configurable sounds.
- Diagnostics window with printer and firmware information and a 24-hour temperature chart.
- Camera window for RTSP/H.264 cameras (e.g. Buddy3D) through FFmpeg, with a print overlay and full-screen, normal
  and compact modes.
- PrusaLink web opened already signed in through a local proxy. The password never reaches the browser.
- Settings window: printers, connection test, camera, notifications, polling interval and language.
- Passwords stored in the Windows Credential Manager.
- Localization: English, Czech, Slovak, German, Spanish, French, Italian and Polish.
- About window with the third-party licences.
- Single instance: starting the app again doesn't open a second tray icon.
- Native AOT publishing (single executable, no .NET runtime required).

[Unreleased]: https://github.com/pfoltynek/PrusaTrayDiagnostic/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/pfoltynek/PrusaTrayDiagnostic/releases/tag/v0.1.0
