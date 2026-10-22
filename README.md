<p align="center">
  <img src="src/PrusaTrayDiagnostic.App/Assets/AppIcon/app-icon.svg" alt="Prusa Tray Diagnostic icon" width="96" height="96">
</p>

<h1 align="center">Prusa Tray Diagnostic</h1>

<p align="center">
  Keep an eye on your Prusa 3D printers from the system tray.
  <br>
  <a href="README.cz.md">Česky</a> ·
  <a href="https://github.com/pfoltynek/PrusaTrayDiagnostic/releases">Releases</a> ·
  <a href="https://github.com/pfoltynek/PrusaTrayDiagnostic/issues">Issues</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--only-blue.svg" alt="License: GPL-3.0-only"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20(planned)-lightgrey.svg" alt="Platform: Windows, macOS planned">
  <img src="https://img.shields.io/badge/.NET-11%20Native%20AOT-512BD4.svg" alt=".NET 11 Native AOT">
</p>

---

**Prusa Tray Diagnostic** is a small tray-only application for monitoring Prusa 3D printers. It shows the state and
progress of your prints in the tray icon, sends system notifications when something happens, and gives you quick
access to diagnostics, temperatures and the printer camera. It doesn't have a main window. Everything starts from the
tray icon.

It talks to the printers over **PrusaLink**, the local REST API built into the printer firmware. Support for
**Prusa Connect** (cloud) is planned.

> [!NOTE]
> This is an independent community project. It is **not** an official product of Prusa Research and is not affiliated
> with or endorsed by Prusa Research a.s. *Prusa*, *PrusaLink* and *Prusa Connect* are trademarks of their respective
> owners.

## Features

- **Status at a glance.** The tray icon shows the printer's state (idle, printing with progress, paused, finished,
  needs attention, error, offline). The tooltip shows the details.
- **Multiple printers.** One combined icon shows the most important state across all printers. Each printer has its
  own submenu.
- **Notifications** when a print starts, finishes, pauses, resumes or stops, on errors, when the printer needs
  attention or goes offline, and a configurable number of minutes before the print ends. Sounds can be off, on for
  important events only, or on for all events.
- **Diagnostics window** with printer and firmware information and a live temperature chart (nozzle and bed) covering
  the last 24 hours.
- **Camera window** for RTSP cameras (for example the Prusa Buddy3D camera), with an overlay showing print progress,
  temperatures and the estimated finish time. It has full-screen, normal and compact modes.
- **One-click PrusaLink web.** The printer's web interface opens in your browser already signed in. Your password is
  never handed to the browser.
- **Secure credential storage.** Passwords are kept in the Windows Credential Manager, never in plain text.
- **Eight languages:** English, Czech, Slovak, German, Spanish, French, Italian and Polish. By default the app follows
  your system language.
- **Lightweight.** It is published as a single Native AOT executable and doesn't need a .NET runtime.

## Platform support

| Platform | Status |
|---|---|
| Windows 10/11 x64 | ✅ supported |
| macOS (Apple Silicon) | 🚧 planned: tray menu, notifications, Keychain and camera are in progress |
| Prusa Connect (cloud) | 🚧 planned |

## Installation

> [!IMPORTANT]
> There is no packaged release yet. An installer with automatic updates is planned.
> Until then, [build the app from source](#building-from-source).

Releases will be published on the [Releases](https://github.com/pfoltynek/PrusaTrayDiagnostic/releases) page.
Early builds are not code-signed, so Windows SmartScreen may show an "Unknown publisher" warning.

## Getting started

### 1. Enable PrusaLink on the printer

On the printer, open **Settings → Network → PrusaLink**. Check that PrusaLink is enabled and note the **user name**
(the default is `maker`), the **password** and the printer's **IP address**.

### 2. Add the printer

1. Start the app. A Prusa icon appears in the system tray. On Windows you may need to pin it from the hidden-icons
   area (`^`).
2. Open the tray menu, choose **Add printer…** (or **Settings…** → **Printers** → **Add printer**) and fill in:
   - **Name:** any name you like.
   - **Address:** an IP address or host name, e.g. `192.168.1.20` or `http://prusa-mk4.local`.
   - **User name** and **Password:** the values from step 1.
3. Click **Test connection**, then **Save**.

The icon starts showing the printer's state immediately.

### 3. Optional settings

- **Camera:** in the printer's settings, enter the camera's RTSP stream address, e.g. `rtsp://192.168.0.227/live` for the
  Buddy3D camera. Don't put a user name or password in the URL. Turn the camera on in the printer's submenu in the
  tray menu.
- **Notifications:** choose which events you want, the sound mode and the "minutes remaining" reminder.
- **General:** the polling interval (how often the printers are asked for their state) and the language.

### Where data is stored

| What | Where (Windows) |
|---|---|
| Settings | `%APPDATA%\PrusaTrayDiagnostic\settings.json` |
| Passwords | Windows Credential Manager |
| Logs | `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs` |

The temperature history is kept only in memory and is never written to disk.

### Reporting a problem

The **About** window has a **Report a problem** group: save a diagnostic bundle (a ZIP with the logs, version and system
information and the settings without passwords) or open the log folder. Detailed logging can be turned on in the settings
(General tab). See [docs/troubleshooting.md](docs/troubleshooting.md) (in Czech) for what the bundle contains.

## Building from source

### Prerequisites

- [.NET SDK 11](https://dotnet.microsoft.com/download/dotnet/11.0). The exact version is pinned in
  [`global.json`](global.json) and preview versions are allowed.
- Git.
- For a **Native AOT publish** only: on Windows the Visual Studio Build Tools with the *Desktop development with
  C++* workload, on macOS the Xcode Command Line Tools.
- The first build on Windows downloads the LGPL FFmpeg libraries for the camera (~77 MB, verified by SHA-256).

See [docs/prerequisites.md](docs/prerequisites.md) for details (in Czech).

### Build, test, run

```bash
git clone https://github.com/pfoltynek/PrusaTrayDiagnostic.git
cd PrusaTrayDiagnostic

dotnet build                                      # build the whole solution
dotnet test                                       # run all tests
dotnet run --project src/PrusaTrayDiagnostic.App  # start the tray app
```

### Native AOT publish

```bash
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64     # on Windows
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r osx-arm64   # on macOS
```

AOT code can't be cross-compiled between operating systems: build Windows binaries on Windows and macOS binaries on
macOS.

### Making a release

Releases are built locally (no CI) with PowerShell 7.2+ scripts: `scripts/release-build.ps1` (build, tests, AOT publish,
smoke test, archives, SHA-256 checksums), `scripts/release-upload.ps1` (draft GitHub release) and
`scripts/release-publish.ps1`. The version comes from `<Version>` in `Directory.Build.props` and the release notes
from [CHANGELOG.md](CHANGELOG.md). See [docs/release.md](docs/release.md) for the full procedure (in Czech).

### Project structure

| Path | Contents |
|---|---|
| `src/PrusaTrayDiagnostic.Core` | domain model, abstractions, monitoring and event detection (no UI, no HTTP specifics) |
| `src/PrusaTrayDiagnostic.PrusaLink` | PrusaLink REST client and the local web proxy |
| `src/PrusaTrayDiagnostic.PrusaConnect` | Prusa Connect cloud client (planned) |
| `src/PrusaTrayDiagnostic.Camera.FFmpeg` | RTSP/H.264 camera feed via FFmpeg |
| `src/PrusaTrayDiagnostic.App` | Avalonia tray app, windows, platform specifics |
| `tests/` | xUnit v3 unit, UI (Avalonia headless) and E2E tests |
| `tools/` | developer tools, e.g. the PrusaLink printer and camera simulator |
| `docs/` | architecture and API notes (in Czech) |
| `tasks/` | task backlog (in Czech) |

Built with [.NET](https://dotnet.microsoft.com/), [Avalonia UI](https://avaloniaui.net/),
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) and [FFmpeg](https://ffmpeg.org/).

## Contributing

Contributions are welcome: bug reports, ideas, translations and pull requests.

- **Bugs and ideas:** open an [issue](https://github.com/pfoltynek/PrusaTrayDiagnostic/issues). Include your printer
  model, firmware version and OS.
- **Pull requests:** the project uses Gitflow. Branch from `develop` and open the PR against `develop`.
  `dotnet build` and `dotnet test` must pass with **zero warnings**, because warnings are treated as errors.
- **Native AOT:** no unconstrained reflection, `System.Text.Json` only through source-generated contexts, and any new
  NuGet package must be trim/AOT compatible. See [CLAUDE.md](CLAUDE.md) for the full coding rules.
- **Language:** code, comments and commit messages are in English. Task and design notes in `tasks/` and `docs/` are
  in Czech.

There is no CLA. By submitting a contribution you agree that it is licensed under the project's license
(GPL-3.0-only).

### Security

Please don't report security issues in public issues. Use GitHub's
[private vulnerability reporting](https://github.com/pfoltynek/PrusaTrayDiagnostic/security/advisories/new) instead.

## License

Copyright © Petr Foltýnek

This program is free software: you can redistribute it and/or modify it under the terms of the
**GNU General Public License, version 3 only** (SPDX: `GPL-3.0-only`). See [LICENSE](LICENSE) for the full text.

This program is distributed in the hope that it will be useful, but **without any warranty**; without even the
implied warranty of merchantability or fitness for a particular purpose.

### Third-party components

The app bundles the native **FFmpeg** libraries, which are licensed under the LGPL. They are dynamically linked and
can be replaced. The FFmpeg license text ships with the app as `FFmpeg-LICENSE.txt`. All third-party components,
their versions and full license texts are listed in the app under **About → Third-party licences…**.
