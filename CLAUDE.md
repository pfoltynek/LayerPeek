# CLAUDE.md

Guidance for Claude Code and all subagents working in this repository.

## Project

**LayerPeek** is a cross-platform (Windows + macOS) **tray-only** application that monitors Prusa 3D printers.
Data sources: **PrusaLink** (local REST API on the printer) and **Prusa Connect** (cloud).
There is no main window — UI lives in the system tray (Windows) / menu bar (macOS); small auxiliary windows (settings, diagnostics) may open from the tray menu.
License: **GPL-3.0-only**, no CLA. Every new dependency must have a GPL-3.0-compatible license (MIT, BSD, Apache-2.0, LGPL…) — verify and state it in the task notes, like AOT compatibility.

## Language rules (strict)

| What | Language |
|---|---|
| Communication with the user | **Czech** |
| `.claude/agents/*.md`, this file, prompts between agents | English |
| Code identifiers, code comments, commit messages | English |
| `tasks/**`, `docs/**`, `README.cz.md` | **Czech** |
| `README.md`, `CHANGELOG.md` | English (public open-source front page; keep `README.cz.md` in sync) |
| UI strings | localized via `Strings.*.resx` (T-029); every new string goes into all languages |

## Decision rule

**Do not make product or technology decisions on your own.** If a task is ambiguous (scope, UX, library choice, packaging, API behaviour you cannot verify), stop and surface the question.
- Main agent: ask the user (AskUserQuestion).
- Subagents: you cannot ask the user — finish with a clear list of questions in your final report and write them into the task's `## Otevřené otázky` section. Do not guess.

## Tech stack

- .NET 11 (SDK pinned in `global.json`, prerelease allowed), C# latest, nullable enabled, warnings as errors.
- Central package management: versions only in `Directory.Packages.props`; `PackageReference` without `Version`.
- UI: Avalonia 12 (`TrayIcon`, `NativeMenu`), MVVM via CommunityToolkit.Mvvm.
- Tests: xUnit v3 on Microsoft.Testing.Platform (`dotnet test`). No VSTest packages.
- Solution file: `LayerPeek.slnx`.
- **Native AOT**: the app is published with `PublishAot=true`; every project under `src/` has `IsAotCompatible=true` (via `src/Directory.Build.props`), so trim/AOT analyzer warnings fail the build.

## Native AOT rules

- No unconstrained reflection: no `Activator.CreateInstance(Type)`, `Type.GetType(string)`, assembly scanning, `dynamic`, runtime `Emit`.
- `System.Text.Json` only through source-generated `JsonSerializerContext` (never the reflection-based `JsonSerializer.Serialize<T>(value)` overloads without type info).
- Configuration binding only with the configuration binding source generator (`EnableConfigurationBindingGenerator`) or manual mapping.
- DI: explicit registrations only (no convention/scanning-based registration).
- Avalonia: compiled bindings only (default on); no reflection bindings, no `x:CompileBindings="False"`.
- No `BuiltInComInteropSupport`; Windows interop via `[LibraryImport]` / source-generated COM (`[GeneratedComInterface]`).
- **Every new NuGet package must be AOT/trim compatible** — verify before proposing it and state it in the task notes.
- Never silence trim/AOT warnings with `UnconditionalSuppressMessage` or `<NoWarn>` without a justification approved by the user.
- AOT is compiled per OS: Windows binaries on Windows, macOS on macOS. Local Windows AOT publish requires the Visual Studio "Desktop development with C++" workload, macOS requires Xcode Command Line Tools; normal `dotnet build`/`run`/debug needs neither on Windows. On macOS the first `dotnet build` also needs the Xcode Command Line Tools, because it builds the LGPL FFmpeg libraries from source into a cache (T-026, `build/macos/build-ffmpeg.sh`). Full list: `docs/prerequisites.md` — keep it up to date when toolchain requirements change.

## Repository layout

```
src/
  LayerPeek.Core           domain model, abstractions (IPrinterClient...), monitoring, events — no UI, no HTTP specifics
  LayerPeek.PrusaLink      PrusaLink REST client (implements Core abstractions)
  LayerPeek.PrusaConnect   Prusa Connect cloud client (implements Core abstractions)
  LayerPeek.Camera.FFmpeg  RTSP/H.264 camera feed via FFmpeg (implements ICameraFeedFactory); native LGPL FFmpeg DLLs from build/FFmpeg.targets
  LayerPeek.App            Avalonia tray app, composition root, platform specifics
tests/
  <Project>.Tests          one xUnit v3 project per src library
  LayerPeek.E2E.Tests      cross-cutting browser E2E tests (Playwright + installed Edge, skipped without Edge)
  LayerPeek.App.UiTests    cross-cutting Avalonia headless UI tests (T-031)
  Shared/                  test helpers shared by several test projects (linked files, no copies)
  PrusaSimulator.Tests     tests of the developer simulator (tools/PrusaSimulator)
tools/
  PrusaSimulator           dev tool: simulated PrusaLink printers + RTSP camera (docs/simulator.md); not shipped
  migrate-from-prusatraydiagnostic.ps1  one-time migration of dev machines from the former name (T-074)
  render-macos-icon.swift  regenerates the macOS app icon PNGs from their SVGs (T-098, needs Xcode Command Line Tools)
docs/                      architecture & API notes (Czech)
tasks/                     task backlog (Czech), see tasks/README.md
.claude/agents/            subagent definitions
```

Dependency direction: `App -> PrusaLink, PrusaConnect, Camera.FFmpeg -> Core`. Core must never reference Avalonia, concrete HTTP clients or FFmpeg.

## Commands

```bash
dotnet build                                  # whole solution
dotnet test                                   # all tests (MTP runner)
dotnet run --project src/LayerPeek.App
dotnet publish src/LayerPeek.App -c Release -r win-x64     # Native AOT (needs MSVC on Windows)
dotnet publish src/LayerPeek.App -c Release -r osx-arm64   # Native AOT (on macOS only)
```

## Task workflow

Tasks are Markdown files in `tasks/{todo,in-progress,done}/T-XXX-slug.md` (format in `tasks/README.md`).
1. Pick a task from `tasks/todo/` whose dependencies are in `done/` and whose `platform` is `all` or matches the current OS; `macos` tasks are implemented only on macOS.
2. Create the task's feature branch from an up-to-date `develop`: `git switch develop && git switch -c feature/T-XXX-slug` (slug = task file name).
3. `git mv` the task to `tasks/in-progress/`, set `status: in-progress`.
4. Implement + tests. `dotnet build` and `dotnet test` must pass with zero warnings.
5. Tick acceptance criteria, add a short `## Poznámky k implementaci`, `git mv` to `tasks/done/`, set `status: done`.
6. Never mark a task done with failing tests or unticked criteria — leave it in progress and report why.
7. When the user asks, commit on the feature branch and merge it into `develop` with `git merge --no-ff`, then delete the feature branch.

## Git workflow (Gitflow)

| Branch | Purpose |
|---|---|
| `main` | released versions only, each merge tagged `vX.Y.Z`; never commit directly |
| `develop` | integration branch, base for all work; never commit directly |
| `feature/T-XXX-slug` | exactly one branch per task, from `develop`, merged back with `--no-ff` |
| `feature/slug` | non-task changes (docs, tooling), same rules as task branches |
| `release/X.Y.Z` | from `develop`; stabilization only; merged into `main` (tagged) and back into `develop` |
| `hotfix/X.Y.Z` | from `main`; merged into `main` (tagged) and `develop` |

- Commit, merge, tag and push only when the user asks.
- Windows refs are case-insensitive: never create two branches whose names differ only in case.

## Agents

| Agent | Responsibility |
|---|---|
| `architect` | breaks down work, maintains `tasks/` and `docs/architecture.md`, reviews design boundaries |
| `core-dev` | Core domain, monitoring service, PrusaLink and Prusa Connect clients, FFmpeg camera feed |
| `ui-dev` | Avalonia tray app, menus, notifications, auxiliary windows, Windows/macOS specifics |
| `ui-designer` | window layouts, visual hierarchy, sizing for small/high-DPI screens and long translations, icons/artwork; Windows and macOS design guidelines; design spec + layout-only XAML |
| `qa-reviewer` | tests, code review, local AOT publish checks |

## Conventions

- Async all the way; accept `CancellationToken`; no `.Result`/`.Wait()`.
- `HttpClient` via `IHttpClientFactory`; never `new HttpClient()` per request.
- Secrets (API keys, passwords, tokens) never in logs, never committed, never in plain-text settings once T-002 decides the secure storage.
- Platform-specific code isolated behind interfaces in Core, implemented in App (e.g. `INotificationService`).
- Tests for clients use recorded JSON fixtures (`tests/**/Fixtures/*.json`) and a fake `HttpMessageHandler` or loopback-only servers (127.0.0.1, e.g. the simulator) — no real network in tests.
