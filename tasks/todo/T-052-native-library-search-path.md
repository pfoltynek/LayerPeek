---
id: T-052
title: Bezpečnost – omezené hledání nativních knihoven a absolutní cesta k `open` (L3, L4)
status: todo
owner: core-dev
priority: P3
platform: windows
depends_on: [T-014]
---

## Cíl
Opravit dva nálezy nízké závažnosti z bezpečnostní revize (2026-10-21):
- **L3 (Windows):** aplikace nenastavuje omezené hledání DLL. FFmpeg se načítá jen podle názvu: `FFmpeg.AutoGen.Bindings.DynamicallyLinked` (`[DllImport]`, `FFmpegRuntime.cs`) a `[LibraryImport("swscale-10")]` v `src/PrusaTrayDiagnostic.Camera.FFmpeg/NativeMethods.cs:16`. Když knihovna nebo její závislost ve složce aplikace chybí, Windows ji hledají i v dalších složkách (`PATH`), kam může zapisovat někdo jiný. **Zneužitelnost není ověřená:** při standardním pořadí hledání je složka aplikace první a knihovny FFmpeg v ní jsou. Riziko vzniká hlavně u chybějící knihovny nebo instalace do složky, kam může zapisovat uživatel. Jde o zpevnění.
- **L4 (macOS):** `src/PrusaTrayDiagnostic.App/Platform/ProcessUrlLauncher.cs:91` spouští `open` přes `PATH` (`UseShellExecute = false`, jen název). Podvržený `open` v `PATH` by dostal i adresu proxy s tokenem (T-028).

## Rozsah
- **L3:** na Windows co nejdřív v `Program.Main`, jako první příkaz, ještě před testovacím režimem a `SingleInstanceGuard`, zavolat `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)` přes `[LibraryImport("kernel32.dll")]`. Návratovou hodnotu zkontrolovat a selhání zalogovat; aplikace poběží dál.
  - Ověřit, že nic, co se načítá později, neleží mimo složku aplikace a `System32`: Avalonia a Skia / HarfBuzz / ANGLE, FFmpeg a jeho závislosti, toasty (WinRT, T-010), Credential Manager (T-002), COM pro vlastnosti oken (T-043) a Kestrel proxy (T-028). Ověřit v AOT publish i v Debug / `dotnet run` (JIT, sdílený framework mimo složku aplikace).
  - Pokud by globální nastavení něco rozbilo, alternativa je `NativeLibrary.SetDllImportResolver` pro sestavení `FFmpeg.AutoGen.Bindings.DynamicallyLinked` a `PrusaTrayDiagnostic.Camera.FFmpeg` s absolutními cestami z `AppContext.BaseDirectory`. Volbu a důvod zapsat do Poznámek.
  - macOS: načítání `.dylib` z `.app` bundlu (absolutní cesty / `@rpath`) řeší T-026, sem nepatří.
- **L4:** v `ProcessUrlLauncher.CreateStartInfo` použít pro macOS absolutní `/usr/bin/open`. Kód je sdílený a jde otestovat jednotkovým testem na Windows (`UrlLaunchPlatform.MacOS`). Skutečné ověření na Macu je kritérium v T-053. Linux (`xdg-open`) aplikace nepodporuje; podle stejného vzoru zvážit `/usr/bin/xdg-open` a rozhodnutí zapsat do Poznámek.
- Bez nového NuGet balíčku, jen `[LibraryImport]` (AOT).

## Akceptační kritéria
- [ ] `SetDefaultDllDirectories` se volá na začátku `Program.Main` na Windows (nebo je zdokumentovaná alternativa s resolverem); selhání se zaloguje
- [ ] Ruční ověření na Windows v AOT publish i v Debug: tray, okna, toasty, Credential Manager, kamera (FFmpeg) a web přes proxy fungují; E2E testy T-044 a T-030 procházejí
- [ ] Test (pokud jde spolehlivě, v podřízeném procesu): DLL se stejným názvem jako chybějící knihovna, umístěná ve složce z `PATH`, se nenačte
- [ ] Jednotkový test: pro macOS `CreateStartInfo` vrací `FileName == "/usr/bin/open"` a adresu jako jediný argument
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
