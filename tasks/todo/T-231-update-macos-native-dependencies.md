---
id: T-231
title: macOS – aktualizace nativních závislostí a nástrojů (FFmpeg ze zdrojů, Xcode, runner CI)
status: todo
owner: qa-reviewer
priority: P2
platform: macos
depends_on: []
---

## Cíl
Zadání uživatele 2026-11-15: aktualizovat všechny knihovny na Macu i na Windows. Tento úkol pokrývá nativní závislosti
a nástroje, které používá jen macOS; spravované balíčky řeší T-230, Windows T-232.

## Rozsah
- **FFmpeg** (`build/FFmpeg.targets`, `build/macos/build-ffmpeg.sh`): dnes oficiální zdroj 9.0.2 (2026-11-15
  nejnovější vydání řady 9.0 na https://ffmpeg.org/releases/). Ověřit, jestli nevyšlo novější vydání (oprava řady 9.0,
  nebo nová hlavní verze – ta jen po rozhodnutí uživatele, viz otázky), aktualizovat URL, SHA-256 a složku, nechat
  přestavět cache, ověřit licenci (LGPL v3 or later, `--enable-version3`), věty v okně O aplikaci (T-199) a zdroj
  k vydání. Testy kamery (`LayerPeek.Camera.FFmpeg.Tests` i s hardwarovým dekódováním, T-203) a měření z T-205 namátkou.
- **Xcode / Command Line Tools a Swift** (`tools/macos-verify-helper.swift`, `tools/render-macos-icon.swift`): build
  na aktuální verzi bez varování; `docs/prerequisites.md` aktualizovat, mění-li se minimální verze.
- **Runner CI** (T-218): obraz macOS (`xcode-27`, zatím preview) – přejít na stabilní označení, jakmile ho GitHub nabídne;
  připnuté SHA akcí aktualizuje Dependabot (T-218), zkontrolovat, že jeho PR procházejí.
- Swift helper ani skripty nesmí záviset na Homebrew.

## Akceptační kritéria
- [ ] FFmpeg na nejnovějším vydání zvolené řady (nebo zdůvodněno, proč beze změny); cache, licence, zdroj k vydání OK
- [ ] Testy kamery a `dotnet test` bez chyb; AOT publish osx-arm64 bez varování; `mac.release-bundle` (T-217) PASS
- [ ] Swift nástroje se přeloží bez varování na aktuálním Xcode
- [ ] `docs/prerequisites.md` odpovídá

## Otevřené otázky
1. Přechod na novou **hlavní** verzi FFmpeg (pokud vyjde) – jen po rozhodnutí uživatele (dopad na API, Windows build
   T-202 musí jít stejnou verzí).

## Poznámky k implementaci
(vyplní agent po dokončení)
