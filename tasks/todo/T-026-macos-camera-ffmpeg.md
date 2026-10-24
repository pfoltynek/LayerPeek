---
id: T-026
title: macOS – FFmpeg pro živý stream kamery
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-014, T-019]
---

## Cíl
Zprovoznit na macOS živý stream z kamery (RTSP/H.264, Prusa Buddy3D) z T-014. Kód dekodéru je sdílený. Na macOS je potřeba přibalit nativní knihovny FFmpeg do `.app` bundlu a ověřit Native AOT publish `osx-arm64`.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- Nativní knihovny FFmpeg (`.dylib`) ve stejné verzi a stejném LGPL buildu jako na Windows (T-014), pro `osx-arm64`.
- Umístění knihoven do `.app` bundlu (např. `Contents/Frameworks`) a jejich načtení z bindingu FFmpeg. Žádná závislost na systémovém FFmpeg (Homebrew).
- Úprava `install_name` / `rpath`, aby se knihovny načetly z bundlu.
- Podepsání knihoven spolu s aplikací (navazuje na T-019). Ověřit, že Gatekeeper / hardened runtime načtení knihoven nezablokuje.
- Případné rozdíly chování okna na macOS: malý náhled vždy nahoře (`Topmost`), celá obrazovka (macOS má vlastní fullscreen prostor), okno bez rámečku.
- Případný přístup k místní síti (macOS „Local Network“ oprávnění) pro RTSP na kameru v LAN.

## Akceptační kritéria
- [ ] Stream z Buddy3D se na macOS zobrazí ve všech režimech okna (normální, celá obrazovka, malý náhled vždy nahoře s překryvem stavu tisku)
- [ ] FFmpeg knihovny jsou součástí `.app` bundlu a načtou se bez systémového FFmpeg
- [ ] Podepsaný bundle (T-019) se spustí a stream funguje na čistém Macu
- [ ] Lokální Native AOT publish `osx-arm64` bez trim/AOT varování z kódu projektu
- [ ] Licence LGPL a verze FFmpeg uvedené v poznámkách / distribuci stejně jako na Windows
- [ ] Windows build a testy beze změny chování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Odkud vzít LGPL build FFmpeg pro `osx-arm64` (vlastní sestavení, nebo hotový balíček)? (`osx-x64` ani universal binary se nepodporuje – rozhodnuto 2026-10-21, viz T-019.)
- Využít v FFmpeg hardwarové dekódování přes VideoToolbox, nebo stačí softwarové?
- Vyžaduje macOS pro RTSP v lokální síti oprávnění „Local Network“ (`NSLocalNetworkUsageDescription` v `Info.plist`)?

## Poznámky k implementaci

### Podklady z T-014 (code review, kolo 1) – 2026-10-18
- **Názvy nativních knihoven jsou zatím windowsové:** `NativeMethods.Scale` v `src/LayerPeek.Camera.FFmpeg/NativeMethods.cs` volá `[LibraryImport("swscale-10")]` a binding FFmpeg.AutoGen.Bindings.DynamicallyLinked používá názvy `avformat-63`, `avcodec-63` atd. Na macOS se knihovny jmenují jinak (např. `libswscale.10.dylib`), takže je potřeba ověřit nebo upravit resolvování (např. `NativeLibrary.SetDllImportResolver`).
- **`build/FFmpeg.targets` je jen pro Windows:** přesun hotové cache používá `move` a krok běží jen pro `win-x64`. Pro macOS doplnit stažení a ověření `.dylib` a přesun (`mv`).
- **Testy FFmpeg se mimo win-x64 přeskakují** (`[FFmpegFact]`, rozhodnutí T-014), dokud FFmpeg na platformě nejde načíst. Po dodání knihoven na macOS musí testy na Macu běžet a projít.
