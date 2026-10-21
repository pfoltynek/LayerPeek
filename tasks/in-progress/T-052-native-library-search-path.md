---
id: T-052
title: Bezpečnost – omezené hledání nativních knihoven a absolutní cesta k `open` (L3, L4)
status: in-progress
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
- [x] `SetDefaultDllDirectories` se volá na začátku `Program.Main` na Windows (nebo je zdokumentovaná alternativa s resolverem); selhání se zaloguje
- [ ] Ruční ověření na Windows v AOT publish i v Debug: tray, okna, toasty, Credential Manager, kamera (FFmpeg) a web přes proxy fungují; E2E testy T-044 a T-030 procházejí
- [x] Test (pokud jde spolehlivě, v podřízeném procesu): DLL se stejným názvem jako chybějící knihovna, umístěná ve složce z `PATH`, se nenačte
- [x] Jednotkový test: pro macOS `CreateStartInfo` vrací `FileName == "/usr/bin/open"` a adresu jako jediný argument
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci

**L3 – omezené hledání DLL (Windows)**
- Zvoleno globální `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)`, ne resolver: chrání i závislosti DLL (např. chybějící `avutil` u `swscale`), na které `NativeLibrary.SetDllImportResolver` nedosáhne, a nic se tím nerozbilo (viz ověření níže).
- `Platform/Windows/NativeLibrarySearch.cs`: `Restrict()` (`[LibraryImport("kernel32.dll", SetLastError = true)]`) vrací 0 nebo kód chyby Win32; `Program.Main` ji volá jako první příkaz (před `SingleInstanceGuard`), selhání zaloguje varováním přes `startupLogging` a aplikace běží dál se standardním pořadím. Pozor: `startupLogging` zatím nemá žádného providera, takže se varování projeví až po T-024; rozsah a kritérium jsou do T-024 doplněné.
- Knihovny načítané plnou cestou (runtime a sdílený framework .NET v Debug, COM/WinRT servery, Skia/HarfBuzz/ANGLE z `runtimes/win-x64/native` přes `deps.json`) omezení neovlivní. FFmpeg si hardwarové knihovny (`nvcuda`, `vulkan-1`, `d3d11`…) načítá sám ze System32.
- Možný vedlejší účinek (neověřeno, žádný konkrétní případ): rozšíření Průzkumníka třetích stran načtená do dialogu výběru souboru (T-039), která hledají své závislosti přes `PATH`, se nenačtou – dialog funguje dál, jen bez nich.
- Test v podřízeném procesu: Debug-only přepínač `PRUSATRAYDIAGNOSTIC_NATIVE_LIBRARY_PROBE` (stejný vzor jako `ForwardOnlyVariable` z T-043) spustí místo aplikace sondu: načte knihovnu jako `[DllImport]` nejdřív se standardním hledáním (kontrola, že je knihovna dosažitelná), uvolní ji, zavolá `Restrict()` a načte ji znovu; výsledek vrátí exit kódem. Sonda je proto v Debug před omezením, v Release neexistuje. Testy (`NativeLibrarySearchTests`): DLL jen ve složce z `PATH` se nenačte; závislost DLL načtené plnou cestou, která je jen v `PATH`, se nenačte (kopie `swscale-10.dll` s importem `avutil-61.dll` přejmenovaným na unikátní název stejné délky); FFmpeg ze složky aplikace a `credui` ze System32 se načtou dál. Ověřeno i obráceně: s vypnutým `Restrict()` oba testy s `PATH` selžou. Soubory jdou do rezervované složky testů (úklid i po pádu, T-032).

**L4 – absolutní cesty (macOS / Linux)**
- `CreateStartInfo` spouští `/usr/bin/open`, adresa zůstává jediným argumentem (test `MacOSStartsOpenByAbsolutePathWithAddressAsOnlyArgument` + upravené teorie).
- Linux (nepodporovaný): stejný vzor, `/usr/bin/xdg-open` – adresa proxy s tokenem nesmí dostat podvržený program z `PATH`. Na distribucích bez `/usr/bin/xdg-open` (např. NixOS) se prohlížeč neotevře, chyba se jen zaloguje a menu funguje dál; u nepodporované platformy přijatelné.

**Ověření (Windows, 2026-10-21)**
- `dotnet build` 0 varování; testy po projektech: Core 1290/1290, PrusaLink 265 + 1 přeskočený, PrusaConnect 1/1, Camera.FFmpeg 39/39, App 2124/2124, App.UiTests 400/400, E2E (T-030) 16/16. `PrusaSimulator.Tests` nespuštěny (známé zaseknutí, patří jiné session).
- Debug (`bin/Debug/.../PrusaTrayDiagnostic.exe`): aplikace nastartuje; seznam načtených modulů je s omezením i bez něj totožný (149 modulů, vč. `libSkiaSharp`, `av_libGLESv2`, `d3d11`). Sonda s omezením načte `avformat-63`, `avcodec-63`, `swresample-7`, `swscale-10`, `libSkiaSharp`, `libHarfBuzzSharp` i `av_libglesv2`.
- AOT publish win-x64 bez varování; publikovaný exe nastartuje a načte `libSkiaSharp`, `av_libglesv2`, `d3d11`.
- **Zbývá ručně** (kritérium 2 proto nezaškrtnuto): toasty, Credential Manager, kamera (FFmpeg) a web přes proxy klikáním v Debug i AOT; E2E testy T-044 nejsou v `develop` (větev `feature/T-044-windows-e2e-tests` není sloučená), takže tady spustit nešly. Ověření `/usr/bin/open` na Macu je v T-053.

- **E2E běh 2026-10-21 (develop po sloučení T-044, Debug):** 23 z 25 testů T-044 prošlo, 1 se přeskočil (skutečná kamera nedostupná). Selhal `TrayMenuTests` (ikona `[E2E]` nenalezena), a to dvakrát po sobě. Příčina je v prostředí: okno skrytých ikon Windows (`TopLevelWindowForOverflowXamlIsland`) se otevře a ikony ukazuje, ale přes UI Automation zpřístupní jen prázdný `Pane`/`Custom` bez tlačítek. Načtení nativních knihoven to není (aplikace běžela, log je v pořádku). Ruční část kritéria zbývá.
- **Po restartu Exploreru (2026-10-21):** `TrayMenuTests` prošel (1 z 1). Selhání bylo jen v prostředí. Všechny E2E testy T-044 tedy na `develop` procházejí (skutečná kamera v samostatném běhu `CameraWindowTests`, 2 z 2). Zbývá ruční proklikání v Debug i AOT.
- **Ruční ověření 2026-10-21 (uživatel, Windows 11, skutečná tiskárna):** web PrusaLink přes proxy funguje v Debugu (`dotnet run`, `develop` 05b03ed) i v AOT (`dotnet publish -c Release -r win-x64`, 0 varování). Načtení bez hesla, nahrání a smazání souboru a ovládání tisku proběhly bez chyby. **Zbývá** ověřit se simulátorem (`tools/PrusaSimulator`) v AOT i v Debugu: tray, okna, toasty, Správce přihlašovacích údajů a kameru (FFmpeg). Port 8080 může být obsazený (např. Aspire), pak použít `--port 18080 --camera-port 18554`.
