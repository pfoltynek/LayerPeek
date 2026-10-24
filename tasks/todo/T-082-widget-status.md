---
id: T-082
title: Widget Stav (název tiskárny, pilulka stavu, průběh)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-081]
---

## Cíl
Widget **Stav**: název tiskárny, „pilulka“ stavu s barevnou tečkou, velké procento průběhu a pruh průběhu. Velikosti S/M/L a zvýraznění klíčového údaje podle schváleného návrhu T-076.

## Kontext
- Framework a sdílené prvky karet: T-081. Pravidla z T-036: název tiskárny vždy (zkrácený na 40 znaků), stav po prvním dotazu, průběh jen u dostupné tiskárny s běžící / pozastavenou / čekající úlohou (`HasActiveJob` nebo `Attention`).
- Barvy tečky stavu navázat na barvy tray ikony (`App/Tray`, T-007) – podle návrhu T-076.

## Rozsah
- View model + šablona widgetu, jedna registrace v `OverlayWidgetRegistry`.
- Obsah ve všech stavech `PrinterState` (Unknown, Idle, Busy, Printing, Paused, Finished, Stopped, Error, Attention, Offline, Unauthorized); bez úlohy jen název + pilulka (nebo podle návrhu).
- Testy pravidel zobrazení přenesené z T-036 (`CameraPrintContentTests`), které se týkají názvu, stavu a průběhu.
- Nové texty ve všech 8 jazycích (pokud návrh nějaké přidá).

## Akceptační kritéria
- [ ] App.Tests: obsah ve všech stavech, chybějící data, velikosti S/M/L, zvýraznění, kultury cs/de/fr, levné porovnání, přepnutí jazyka
- [ ] Headless UI testy: widget ve všech velikostech, se zvýrazněním i bez, nejdelší jazyk a dlouhý název tiskárny bez přetečení, barvy tečky pro každý stav; snímky reference (vizuálně zkontrolované)
- [ ] `AutomationId` / `AutomationProperties.Name` podle návrhu (čtečka přečte stav a průběh)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Zvýraznění: klíčový údaj = **procento průběhu**, vykreslený akcentovou barvou `#FA6831`.
- Normální okno a celá obrazovka sdílejí rozložení; v malém náhledu se karta nekreslí (průběh ukazuje pás pod obrazem, T-089).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
