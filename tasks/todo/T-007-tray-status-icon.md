---
id: T-007
title: Tray ikona se stavem a progressem
status: todo
owner: ui-dev
priority: P1
depends_on: [T-006]
---

## Cíl
Ikona v liště (Windows) / menu baru (macOS) ukazuje stav tisku na první pohled.

## Rozsah
- Různé ikony/barvy podle `PrinterState` (idle, tiskne, pauza, hotovo, chyba, offline)
- Tooltip: název tiskárny, stav, progress %, zbývající čas, název souboru
- Případně vykreslení progressu přímo do ikony

## Akceptační kritéria
- [ ] Ikona se mění podle stavu z monitorovací služby
- [ ] Na macOS je ikona čitelná ve světlém i tmavém režimu
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Vizuální styl ikon (barevná tečka, vlastní logo, progress kruh)? Pozor na ochrannou známku Prusa.
- Zobrazovat na macOS progress i jako text vedle ikony v menu baru?

## Poznámky k implementaci
