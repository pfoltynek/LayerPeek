---
id: T-038
title: macOS – ověření umístění okna kamery, monitoru a „vždy navrchu“
status: todo
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-026, T-035, T-037]
---

## Cíl
Ověřit a případně doladit na macOS chování z T-035 a T-037: výběr monitoru, rohy, celá obrazovka na zvoleném monitoru a `Topmost`.

## Rozsah
- Co vrací `Screen.DisplayName` na macOS a jestli je stabilní mezi spuštěními (identifikace monitoru z T-035).
- Pracovní plocha s menu barem a Dockem, rohy okna, okno bez rámečku (malý náhled).
- Celá obrazovka na zvoleném monitoru (macOS přesouvá okno do vlastního Space).
- `Topmost` nad jinými aplikacemi a nad aplikacemi na celé obrazovce.
- Nastavení bez posuvníku (T-033) na MacBooku 13" (1440×900 / 1470×956 bodů).

## Akceptační kritéria
- [ ] Ruční ověření na macOS s jedním a se dvěma monitory, výsledky v poznámkách
- [ ] Případné opravy pokryté testy (App.Tests / App.UiTests)
- [ ] `dotnet build` a `dotnet test` na macOS bez chyb a varování

## Otevřené otázky
- (žádné – vyplní se po ověření)

## Poznámky k implementaci
(vyplní agent po dokončení)
