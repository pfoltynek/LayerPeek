---
id: T-139
title: macOS – ikona LayerPeeku v Docku i bez ikony v bundlu
status: done
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-102, T-098]
---

## Cíl
Uživatel 2026-11-01: „ještě poreš ikonu pro okno v dock pásu mac“ (upřesněno: ikona okna v Docku). Když je otevřené
okno, které není navrchu, je LayerPeek běžná aplikace s ikonou v Docku a ⌘Tab (T-102). Ve vývojovém běhu (`dotnet run`,
bez bundlu) a v testovacím bundlu „LayerPeek E2E.app“ (E2E testy a `tools/macos-verify.ps1`, bez `.icns`) se ukazovala
obecná ikona spustitelného souboru („exec“). Avalonia 12.1.4 ikonu aplikace na macOS nenastavuje (v `libAvaloniaNative`
není `setApplicationIconImage:`) a `Window.Icon` (Windows `.ico`) macOS nepoužívá. T-102 otázka 4 („stačí“, 2026-10-29)
tím padá – rozhodnutí uživatele 2026-11-01.

## Rozsah
- `MacAppIcon.ApplyWhenTheBundleHasNone`: když `CFBundleIconFile` hlavního bundlu nevede na existující soubor, nastaví
  `NSApp.applicationIconImage` z 1024 px předlohy ikony pro macOS (`AppIcon.MacMasterUri`, T-098). Vydaná `LayerPeek.app`
  (`LayerPeek.icns`) zůstává beze změny.
- Volá se při startu UI a při každém přechodu na běžnou aplikaci (`MacAppPresence`): obrázek nastavený, dokud je aplikace
  doplňková (bez dlaždice v Docku), Dock do nové dlaždice nepřevezme (ověřeno – jen při startu zůstala ikona „exec“).
- `app-icon-macos-1024.png` je nově v binárce (75 kB), 16 a 32 px dál ne.

## Akceptační kritéria
- [x] V Docku je při otevřeném okně kamery ikona LayerPeeku i bez ikony v bundlu (vývojový běh)
- [x] Vydaný bundle s `LayerPeek.icns` beze změny (kód nastaví ikonu jen bez ikony v bundlu)
- [x] UI test seznamu zabalených souborů a předlohy 1024 × 1024 px; `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- Ověřeno 2026-11-01 na macOS 27.2 snímkem Docku: Debug aplikace spuštěná bez bundlu, otevřené normální okno kamery (typ
  LaunchServices `Foreground`) – před opravou ikona „exec“, s nastavením jen při startu také „exec“, s nastavením i po
  přechodu na běžnou aplikaci ikona LayerPeeku. Minimalizované okno v Docku nese stejnou ikonu aplikace (neověřováno
  zvlášť).
- Vydaný bundle snímkem neověřen (kontrola `CFBundleIconFile` → `pathForResource:ofType:` najde `LayerPeek.icns`, takže se
  nic nenastaví); případný rozdíl by ukázal T-126.
- `dotnet build` 0 upozornění, `dotnet test` 8482 testů, 0 selhalo.
