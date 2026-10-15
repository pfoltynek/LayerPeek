# CLAUDE.md

Guidance for Claude Code and all subagents working in this repository.

## Project

**PrusaTrayDiagnostic** is a cross-platform (Windows + macOS) **tray-only** application that monitors Prusa 3D printers.
Data sources: **PrusaLink** (local REST API on the printer) and **Prusa Connect** (cloud).
There is no main window — UI lives in the system tray (Windows) / menu bar (macOS); small auxiliary windows (settings, diagnostics) may open from the tray menu.

## Language rules (strict)

| What | Language |
|---|---|
| Communication with the user | **Czech** |
| `.claude/agents/*.md`, this file, prompts between agents | English |
| Code identifiers, code comments, commit messages | English |
| `tasks/**`, `docs/**`, `README.md` | **Czech** |
| UI strings | to be decided (see open questions in tasks) — do not localize until decided |

## Decision rule

**Do not make product or technology decisions on your own.** If a task is ambiguous (scope, UX, library choice, packaging, API behaviour you cannot verify), stop and surface the question.
- Main agent: ask the user (AskUserQuestion).
- Subagents: you cannot ask the user — finish with a clear list of questions in your final report and write them into the task's `## Otevřené otázky` section. Do not guess.

## Tech stack

- .NET 11 (SDK pinned in `global.json`, prerelease allowed), C# latest, nullable enabled, warnings as errors.
- Central package management: versions only in `Directory.Packages.props`; `PackageReference` without `Version`.
- UI: Avalonia 12 (`TrayIcon`, `NativeMenu`), MVVM via CommunityToolkit.Mvvm.
- Tests: xUnit v3 on Microsoft.Testing.Platform (`dotnet test`). No VSTest packages.
- Solution file: `PrusaTrayDiagnostic.slnx`.
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
- AOT is compiled per OS: Windows binaries on Windows, macOS on macOS. Local Windows AOT publish requires the Visual Studio "Desktop development with C++" workload, macOS requires Xcode Command Line Tools; normal `dotnet build`/`run`/debug needs neither. Full list: `docs/prerequisites.md` — keep it up to date when toolchain requirements change.

## Repository layout

```
src/
  PrusaTrayDiagnostic.Core          domain model, abstractions (IPrinterClient...), monitoring, events — no UI, no HTTP specifics
  PrusaTrayDiagnostic.PrusaLink     PrusaLink REST client (implements Core abstractions)
  PrusaTrayDiagnostic.PrusaConnect  Prusa Connect cloud client (implements Core abstractions)
  PrusaTrayDiagnostic.App           Avalonia tray app, composition root, platform specifics
tests/
  <Project>.Tests                   one xUnit v3 project per src library
docs/                               architecture & API notes (Czech)
tasks/                              task backlog (Czech), see tasks/README.md
.claude/agents/                     subagent definitions
```

Dependency direction: `App -> PrusaLink, PrusaConnect -> Core`. Core must never reference Avalonia or concrete HTTP clients.

## Commands

```bash
dotnet build                                  # whole solution
dotnet test                                   # all tests (MTP runner)
dotnet run --project src/PrusaTrayDiagnostic.App
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64     # Native AOT (needs MSVC on Windows)
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r osx-arm64   # Native AOT (on macOS only)
```

## Task workflow

Tasks are Markdown files in `tasks/{todo,in-progress,done}/T-XXX-slug.md` (format in `tasks/README.md`).
1. Pick a task from `tasks/todo/` whose dependencies are in `done/`.
2. `git mv` it to `tasks/in-progress/`, set `status: in-progress`.
3. Implement + tests. `dotnet build` and `dotnet test` must pass with zero warnings.
4. Tick acceptance criteria, add a short `## Poznámky k implementaci`, `git mv` to `tasks/done/`, set `status: done`.
5. Never mark a task done with failing tests or unticked criteria — leave it in progress and report why.

## Agents

| Agent | Responsibility |
|---|---|
| `architect` | breaks down work, maintains `tasks/` and `docs/architecture.md`, reviews design boundaries |
| `core-dev` | Core domain, monitoring service, PrusaLink and Prusa Connect clients |
| `ui-dev` | Avalonia tray app, menus, notifications, auxiliary windows, Windows/macOS specifics |
| `qa-reviewer` | tests, code review, local AOT publish checks |

## Conventions

- Async all the way; accept `CancellationToken`; no `.Result`/`.Wait()`.
- `HttpClient` via `IHttpClientFactory`; never `new HttpClient()` per request.
- Secrets (API keys, passwords, tokens) never in logs, never committed, never in plain-text settings once T-002 decides the secure storage.
- Platform-specific code isolated behind interfaces in Core, implemented in App (e.g. `INotificationService`).
- Tests for clients use recorded JSON fixtures (`tests/**/Fixtures/*.json`) and a fake `HttpMessageHandler` — no real network in tests.
