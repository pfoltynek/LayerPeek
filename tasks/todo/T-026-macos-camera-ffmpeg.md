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
- Odkud vzít LGPL build FFmpeg pro `osx-arm64` (vlastní sestavení, nebo hotový balíček)? Má se podporovat i `osx-x64` / universal binary?
- Využít v FFmpeg hardwarové dekódování přes VideoToolbox, nebo stačí softwarové?
- Vyžaduje macOS pro RTSP v lokální síti oprávnění „Local Network“ (`NSLocalNetworkUsageDescription` v `Info.plist`)?

## Poznámky k implementaci
