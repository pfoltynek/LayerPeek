---
id: T-083
title: Widget Časy (zbývá, čas dokončení, doba tisku)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-081]
---

## Cíl
Widget **Časy**: „Zbývá“ jako velké číslo, čas dokončení a doba tisku s ikonami (bez textových popisků). Velikosti S/M/L a zvýraznění podle návrhu T-076.

## Kontext
- Formáty a pravidla z T-036 (sdílené formátovače přesunuté v T-081): zbývající čas zaokrouhlený nahoru na minutu, čas dokončení v místním čase z `TimeProvider.LocalTimeZone`, jiný den „Hotovo 17. října v 8:00“ (slova a pořadí podle jazyka aplikace, čas podle regionu), absurdní odhad (`TimeSpan.MaxValue`) nepřeteče. Údaje jen během úlohy.
- Při ikonách místo popisků musí čtečka obrazovky dostat celý text („Zbývá 1 h 5 min“, „Hotovo v 13:05“).

## Rozsah
- View model + šablona, jedna registrace v `OverlayWidgetRegistry`.
- Testy pravidel zobrazení z T-036 týkající se časů (časová pásma, letní čas, jiný den, přetečení) přenesené k widgetu.
- Ikony z Fluent UI System Icons podle návrhu T-076 (geometrie ze sdíleného `ResourceDictionary` z T-081).

## Akceptační kritéria
- [ ] App.Tests: každý údaj jen se známou hodnotou a během úlohy, časová pásma a přechod letního času, jiný den, přetečení, kultury cs/en/de/fr, velikosti S/M/L, zvýraznění, levné porovnání, přepnutí jazyka
- [ ] Headless UI testy: widget ve všech velikostech v nejdelším jazyce bez přetečení, tabulková čísla (šířka se při změně číslic nemění), `AutomationProperties.Name` s celým textem; snímky reference
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Zvýraznění: klíčový údaj = **„Zbývá“**, vykreslený akcentovou barvou `#FA6831`.
- Ikony z Fluent UI System Icons (T-076, T-081).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)
