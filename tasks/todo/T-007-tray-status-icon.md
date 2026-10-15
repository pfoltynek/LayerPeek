---
id: T-007
title: Tray ikona se stavem a progressem
status: todo
owner: ui-dev
priority: P1
platform: all
depends_on: [T-006]
---

## Cíl
Ikona v liště (Windows) / menu baru (macOS) ukazuje stav tisku na první pohled.

Tento úkol pokrývá společnou část (Avalonia `TrayIcon`, mapování stavu na ikonu, tooltip) a barevnou sadu ikon pro Windows. Monochromatická „template“ ikona pro menu bar na macOS je samostatný úkol T-018.

## Rozsah
- Různé ikony/barvy podle `PrinterState` (idle, tiskne, pauza, hotovo, chyba, offline)
- Mapování stavu na ikonu oddělené od konkrétní sady ikon, aby šlo na macOS dodat jinou sadu (T-018)
- Tooltip: název tiskárny, stav, progress %, zbývající čas, název souboru
- Případně vykreslení progressu přímo do ikony

## Akceptační kritéria
- [ ] Ikona se mění podle stavu z monitorovací služby
- [ ] Mapování stavu na ikonu je pokryté testy
- [ ] Ověřeno na Windows (oznamovací oblast)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Vizuální styl ikon (barevná tečka, vlastní logo, progress kruh)? Pozor na ochrannou známku Prusa.

## Poznámky k implementaci
